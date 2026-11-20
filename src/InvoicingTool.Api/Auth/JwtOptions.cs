namespace InvoicingTool.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>Clé de signature des jetons : au moins 32 caractères, à garder secrète (variable d'environnement).</summary>
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "invoicingtool-api";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 60;
}
