using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using InvoicingTool.Api.Data;
using InvoicingTool.Sync.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InvoicingTool.Api.Auth;

/// <summary>Jetons d'accès (JWT, courts) et jetons de renouvellement (aléatoires, renouvelés à chaque usage).</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public const string DeviceClaim = "device";

    private readonly JwtOptions _options = options.Value;

    public static SymmetricSecurityKey SigningKey(string secret) => new(Encoding.UTF8.GetBytes(secret));

    /// <summary>Crée une paire de jetons pour un appareil et enregistre l'empreinte du jeton de renouvellement.</summary>
    public AuthResponse Issue(SyncDbContext db, Account account, Device device)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Issuer,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new Claim(DeviceClaim, device.Id.ToString()),
            ]),
            NotBefore = now,
            IssuedAt = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(SigningKey(_options.Secret), SecurityAlgorithms.HmacSha256),
        });

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(new RefreshToken
        {
            AccountId = account.Id,
            DeviceId = device.Id,
            TokenHash = Hash(refresh),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        });

        return new AuthResponse(accessToken, expires, refresh, account.Id, device.Id, device.SeriesLetter);
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static (Guid AccountId, Guid DeviceId) Identity(ClaimsPrincipal user) =>
        (Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!),
         Guid.Parse(user.FindFirstValue(DeviceClaim)!));
}
