namespace InvoicingTool.Presentation;

/// <summary>Pays proposé dans la fiche client.</summary>
public sealed record CountryOption(string Code, string Name)
{
    public static readonly IReadOnlyList<CountryOption> All =
    [
        new("CH", "Suisse"),
        new("LI", "Liechtenstein"),
        new("FR", "France"),
        new("DE", "Allemagne"),
        new("IT", "Italie"),
        new("AT", "Autriche"),
        new("BE", "Belgique"),
        new("LU", "Luxembourg"),
        new("ES", "Espagne"),
        new("PT", "Portugal"),
        new("GB", "Royaume-Uni"),
        new("US", "États-Unis"),
        new("CA", "Canada"),
    ];

    /// <summary>Pays du code, ou Suisse si le code est inconnu (ex. « Autre pays » de l'entreprise).</summary>
    public static CountryOption Get(string? code) =>
        All.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
