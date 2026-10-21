namespace InvoicingTool.Core.Models;

/// <summary>Informations de l'entreprise de l'artisan, imprimées sur les devis et factures.</summary>
public class CompanySettings : EntityBase
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    public CompanySettings() => Id = SingletonId;

    public string CompanyName { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>Identifiant de l'entreprise : SIRET (FR), IDE (CH), BCE (BE)… selon le pays.</summary>
    public string? Siret { get; set; }
    public string? VatNumber { get; set; }

    /// <summary>Assurance décennale (France) ou RC professionnelle.</summary>
    public string? InsuranceInfo { get; set; }

    public string? Iban { get; set; }

    /// <summary>Code du pays (<see cref="CountryProfiles"/>) qui détermine les règles de TVA et les mentions.</summary>
    public string CountryCode { get; set; } = CountryProfiles.France.Code;

    /// <summary>Devise des nouveaux documents (code ISO 4217).</summary>
    public string CurrencyCode { get; set; } = Currencies.Default;

    /// <summary>Entreprise non assujettie à la TVA (franchise en base, petite entreprise…).</summary>
    public bool VatExempt { get; set; }

    /// <summary>Mention imprimée lorsque l'entreprise n'est pas assujettie à la TVA.</summary>
    public string? VatExemptionMention { get; set; } = CountryProfiles.France.TaxExemptionMention;

    /// <summary>Taux de TVA proposés, séparés par « ; » (ex. « 20;10;5.5;2.1 »).</summary>
    public string TaxRatesText { get; set; } = FormatRates(CountryProfiles.France.TaxRates);

    public decimal DefaultVatRate { get; set; } = CountryProfiles.France.DefaultTaxRate;

    /// <summary>Pas d'arrondi du total TTC : 0,01 (au centime) ou 0,05 (usage suisse).</summary>
    public decimal RoundingIncrement { get; set; } = 0.01m;
    public int QuoteValidityDays { get; set; } = 30;
    public int PaymentTermDays { get; set; } = 30;

    public string QuotePrefix { get; set; } = "D";
    public string InvoicePrefix { get; set; } = "F";

    public string? PaymentTerms { get; set; } = CountryProfiles.France.DefaultPaymentTerms;

    public CountryProfile Country => CountryProfiles.Get(CountryCode);

    public IReadOnlyList<decimal> TaxRates => ParseRates(TaxRatesText);

    /// <summary>Applique les valeurs par défaut d'un pays (devise, taux, mentions, arrondi).</summary>
    public void ApplyCountryDefaults(CountryProfile profile)
    {
        CountryCode = profile.Code;
        CurrencyCode = profile.DefaultCurrency;
        TaxRatesText = FormatRates(profile.TaxRates);
        DefaultVatRate = profile.DefaultTaxRate;
        VatExemptionMention = profile.TaxExemptionMention;
        PaymentTerms = profile.DefaultPaymentTerms;
        RoundingIncrement = profile.RoundingIncrement;
    }

    public static string FormatRates(IEnumerable<decimal> rates) =>
        string.Join(";", rates.Select(r => r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

    public static IReadOnlyList<decimal> ParseRates(string? text) =>
        (text ?? string.Empty)
            .Split([';', ' ', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => decimal.TryParse(t.Replace(',', '.').TrimEnd('%'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
            .Where(v => v is >= 0 and <= 100)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();
}
