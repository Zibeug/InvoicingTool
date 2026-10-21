namespace InvoicingTool.Core.Models;

public sealed record Currency(string Code, string Name, string Symbol)
{
    public string DisplayName => $"{Code} — {Name}";
}

public static class Currencies
{
    public static readonly IReadOnlyList<Currency> All =
    [
        new("EUR", "Euro", "€"),
        new("CHF", "Franc suisse", "CHF"),
        new("USD", "Dollar américain", "$"),
        new("CAD", "Dollar canadien", "$ CA"),
        new("GBP", "Livre sterling", "£"),
        new("XOF", "Franc CFA (UEMOA)", "FCFA"),
        new("XAF", "Franc CFA (CEMAC)", "FCFA"),
        new("MAD", "Dirham marocain", "DH"),
        new("TND", "Dinar tunisien", "DT"),
        new("XPF", "Franc Pacifique", "XPF"),
    ];

    public const string Default = "EUR";

    /// <summary>Retourne la devise connue, ou une devise construite à partir du code (le symbole est alors le code).</summary>
    public static Currency Get(string? code)
    {
        code = string.IsNullOrWhiteSpace(code) ? Default : code.Trim().ToUpperInvariant();
        return All.FirstOrDefault(c => c.Code == code) ?? new Currency(code, code, code);
    }
}
