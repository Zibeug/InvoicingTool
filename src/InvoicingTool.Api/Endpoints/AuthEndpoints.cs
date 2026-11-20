using System.Net.Mail;
using InvoicingTool.Api.Auth;
using InvoicingTool.Api.Data;
using InvoicingTool.Sync.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InvoicingTool.Api.Endpoints;

/// <summary>Création de compte, connexion, renouvellement des jetons, déconnexion et suppression du compte.</summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    private static readonly PasswordHasher<Account> Hasher = new();

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting(RateLimitPolicy);

        auth.MapPost("/register", async (RegisterRequest request, SyncDbContext db, TokenService tokens, TimeProvider clock) =>
        {
            if (ValidateCredentials(request.Email, request.Password) is { } error)
                return Problem(StatusCodes.Status400BadRequest, error);

            var normalized = Normalize(request.Email);
            if (await db.Accounts.AnyAsync(a => a.NormalizedEmail == normalized))
                return Problem(StatusCodes.Status409Conflict, "Un compte existe déjà avec cette adresse e-mail.");

            var now = clock.GetUtcNow().UtcDateTime;
            var account = new Account { Email = request.Email.Trim(), NormalizedEmail = normalized, CreatedAt = now };
            account.PasswordHash = Hasher.HashPassword(account, request.Password);
            // Premier appareil : il garde la numérotation habituelle (pas de lettre de série).
            var device = new Device { AccountId = account.Id, Name = DeviceName(request.DeviceName), CreatedAt = now, LastSeenAt = now };
            account.Devices.Add(device);
            db.Accounts.Add(account);
            var response = tokens.Issue(db, account, device);
            await db.SaveChangesAsync();
            return Results.Ok(response);
        });

        auth.MapPost("/login", async (LoginRequest request, SyncDbContext db, TokenService tokens, TimeProvider clock) =>
        {
            var normalized = Normalize(request.Email ?? "");
            var account = await db.Accounts.Include(a => a.Devices).SingleOrDefaultAsync(a => a.NormalizedEmail == normalized);
            if (account is null || Hasher.VerifyHashedPassword(account, account.PasswordHash, request.Password ?? "") ==
                PasswordVerificationResult.Failed)
                return Problem(StatusCodes.Status401Unauthorized, "Adresse e-mail ou mot de passe incorrect.");

            var now = clock.GetUtcNow().UtcDateTime;
            // Reconnexion d'un appareil déjà connu : il garde sa série de numérotation.
            var device = request.DeviceId is { } knownId ? account.Devices.FirstOrDefault(d => d.Id == knownId) : null;
            if (device is null)
            {
                device = new Device
                {
                    AccountId = account.Id,
                    Name = DeviceName(request.DeviceName),
                    SeriesLetter = NextSeriesLetter(account.Devices.Count),
                    CreatedAt = now,
                };
                db.Devices.Add(device);
            }
            device.LastSeenAt = now;
            var response = tokens.Issue(db, account, device);
            await db.SaveChangesAsync();
            return Results.Ok(response);
        });

        auth.MapPost("/refresh", async (RefreshRequest request, SyncDbContext db, TokenService tokens, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == TokenService.Hash(request.RefreshToken ?? ""));
            if (stored is null || stored.ExpiresAt <= now)
                return Problem(StatusCodes.Status401Unauthorized, "Session expirée : reconnectez-vous.");

            if (stored.RevokedAt is not null)
            {
                // Jeton déjà utilisé : il a peut-être été volé. Toutes les sessions de cet appareil sont fermées.
                await db.RefreshTokens.Where(t => t.DeviceId == stored.DeviceId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
                return Problem(StatusCodes.Status401Unauthorized, "Session expirée : reconnectez-vous.");
            }

            var account = await db.Accounts.SingleAsync(a => a.Id == stored.AccountId);
            var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == stored.DeviceId);
            if (device is null)
                return Problem(StatusCodes.Status401Unauthorized, "Cet appareil a été retiré du compte.");

            stored.RevokedAt = now;
            device.LastSeenAt = now;
            var response = tokens.Issue(db, account, device);
            await db.SaveChangesAsync();
            return Results.Ok(response);
        });

        auth.MapPost("/logout", async (RefreshRequest request, SyncDbContext db, TimeProvider clock) =>
        {
            var hash = TokenService.Hash(request.RefreshToken ?? "");
            await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow().UtcDateTime));
            return Results.NoContent();
        });

        // Droit à l'effacement (nLPD) : supprime le compte et toutes ses données synchronisées.
        app.MapDelete("/api/account", async (HttpContext http, SyncDbContext db) =>
        {
            var (accountId, _) = TokenService.Identity(http.User);
            await db.Accounts.Where(a => a.Id == accountId).ExecuteDeleteAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string? ValidateCredentials(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !MailAddress.TryCreate(email.Trim(), out _))
            return "Adresse e-mail invalide.";
        if (string.IsNullOrEmpty(password) || password.Length < SyncLimits.MinPasswordLength)
            return $"Le mot de passe doit contenir au moins {SyncLimits.MinPasswordLength} caractères.";
        return null;
    }

    private static string DeviceName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Appareil" : name.Trim()[..Math.Min(name.Trim().Length, 100)];

    /// <summary>2e appareil → « B », 3e → « C »… (le premier n'a pas de lettre).</summary>
    public static string NextSeriesLetter(int existingDevices) =>
        existingDevices < 26 ? ((char)('A' + existingDevices)).ToString() : $"Z{existingDevices - 25}";

    public static IResult Problem(int status, string detail) => Results.Problem(detail: detail, statusCode: status);
}
