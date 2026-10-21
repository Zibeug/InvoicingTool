using System.Globalization;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>Formate les montants selon la devise et les usages du pays (« 1 250,00 € », « CHF 1'250.00 »).</summary>
public sealed class MoneyFormatter
{
    private readonly CultureInfo _culture;
    private readonly Currency _currency;
    private readonly bool _symbolFirst;

    public MoneyFormatter(string? currencyCode, string? countryCode)
    {
        var profile = CountryProfiles.Get(countryCode);
        _culture = CultureInfo.GetCultureInfo(profile.CultureName);
        _currency = Currencies.Get(currencyCode);
        _symbolFirst = profile.CurrencyBeforeAmount;
    }

    public static MoneyFormatter For(CommercialDocument document) => new(document.CurrencyCode, document.CountryCode);

    public static MoneyFormatter For(CompanySettings settings) => new(settings.CurrencyCode, settings.CountryCode);

    public CultureInfo Culture => _culture;

    public string CurrencySymbol => _currency.Symbol;

    public string Format(decimal amount)
    {
        var number = Number(amount, "N2");
        return _symbolFirst ? $"{_currency.Symbol} {number}" : $"{number} {_currency.Symbol}";
    }

    /// <summary>Quantité, taux… : jusqu'à 2 décimales, sans zéros inutiles.</summary>
    public string Number(decimal value, string format = "0.##") =>
        // Espaces fines et apostrophes typographiques remplacées par des caractères présents dans toutes les polices.
        value.ToString(format, _culture).Replace(' ', ' ').Replace('’', '\'');

    /// <summary>Date au format du pays : 21.10.2026 en Suisse, 21/10/2026 ailleurs.</summary>
    public string Date(DateTime date) =>
        date.ToString(_culture.Name == "de-CH" ? "dd.MM.yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);
}
