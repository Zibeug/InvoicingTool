using System.Threading.RateLimiting;
using InvoicingTool.Api.Auth;
using InvoicingTool.Api.Data;
using InvoicingTool.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (variables d'environnement dans Docker : ConnectionStrings__Default, Jwt__Secret…) ---
// Lue au moment où elle sert (et non au démarrage du programme), pour que les tests puissent la remplacer.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));

builder.Services.AddDbContext<SyncDbContext>((services, o) => o.UseNpgsql(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default manquant (variable d'environnement ConnectionStrings__Default).")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Issuer,
            IssuerSigningKey = TokenService.SigningKey(jwt.Value.Secret),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// Limite les tentatives de connexion par adresse IP (protection contre les essais de mots de passe en série).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimit:AuthPerMinute", 20),
            Window = TimeSpan.FromMinutes(1),
        }));
});

var app = builder.Build();

// Refuse de démarrer sans clé de signature solide.
if (app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.Secret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret doit contenir au moins 32 caractères (variable d'environnement Jwt__Secret).");

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSyncEndpoints();

// Crée ou met à jour les tables au démarrage (PostgreSQL peut mettre quelques secondes à être prêt).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception ex) when (attempt < 10 && ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException)
        {
            app.Logger.LogWarning("Base de données indisponible (tentative {Attempt}), nouvel essai dans 3 s…", attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

app.Run();

/// <summary>Point d'entrée, exposé pour les tests d'intégration.</summary>
public partial class Program;
