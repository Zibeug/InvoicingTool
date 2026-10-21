namespace InvoicingTool.Core.Models;

/// <summary>
/// Règles et usages propres à un pays : taux de taxe, mentions légales, libellés des identifiants,
/// format des nombres et arrondi. Choisir un pays dans les paramètres applique ces valeurs par défaut,
/// que l'utilisateur peut ensuite ajuster.
/// </summary>
public sealed record CountryProfile
{
    public required string Code { get; init; }
    public required string Name { get; init; }

    /// <summary>Culture utilisée pour formater les nombres et les dates sur les documents.</summary>
    public required string CultureName { get; init; }

    public required string DefaultCurrency { get; init; }

    /// <summary>Symbole de la devise avant le montant (« CHF 1'250.00 ») plutôt qu'après (« 1 250,00 € »).</summary>
    public bool CurrencyBeforeAmount { get; init; }

    /// <summary>Nom de la taxe imprimé sur les documents (TVA, MWST…).</summary>
    public string TaxName { get; init; } = "TVA";

    public required IReadOnlyList<decimal> TaxRates { get; init; }
    public required decimal DefaultTaxRate { get; init; }

    /// <summary>Aide à la saisie : quel taux pour quel type de travaux.</summary>
    public string? TaxRateHint { get; init; }

    /// <summary>Mention obligatoire lorsque l'entreprise n'est pas assujettie à la taxe.</summary>
    public required string TaxExemptionMention { get; init; }

    public required string CompanyIdLabel { get; init; }
    public required string VatNumberLabel { get; init; }

    /// <summary>Exemples de format affichés dans les champs de saisie (non imprimés).</summary>
    public string? CompanyIdExample { get; init; }
    public string? VatNumberExample { get; init; }
    public string InsuranceLabel { get; init; } = "Assurance professionnelle";

    public required string DefaultPaymentTerms { get; init; }

    public string QuoteAcceptanceText { get; init; } = "Bon pour accord — date et signature du client :";

    /// <summary>Pas d'arrondi du total TTC (0,05 en Suisse, 0,01 ailleurs).</summary>
    public decimal RoundingIncrement { get; init; } = 0.01m;
}

public static class CountryProfiles
{
    public static readonly CountryProfile France = new()
    {
        Code = "FR",
        Name = "France",
        CultureName = "fr-FR",
        DefaultCurrency = "EUR",
        TaxRates = [20m, 10m, 5.5m, 2.1m],
        DefaultTaxRate = 10m,
        TaxRateHint = "10 % : rénovation d'un logement de plus de 2 ans · 5,5 % : rénovation énergétique · 20 % : autres cas.",
        TaxExemptionMention = "TVA non applicable, art. 293 B du CGI.",
        CompanyIdLabel = "SIRET",
        CompanyIdExample = "123 456 789 00012",
        VatNumberLabel = "N° TVA intracommunautaire",
        VatNumberExample = "FR12 123456789",
        InsuranceLabel = "Assurance décennale",
        DefaultPaymentTerms =
            "En cas de retard de paiement, des pénalités au taux de 3 fois le taux d'intérêt légal seront appliquées, " +
            "ainsi qu'une indemnité forfaitaire pour frais de recouvrement de 40 €. Pas d'escompte pour paiement anticipé.",
        QuoteAcceptanceText =
            "Bon pour accord — date et signature du client, précédées de la mention « Devis reçu avant l'exécution des travaux » :",
    };

    public static readonly CountryProfile Switzerland = new()
    {
        Code = "CH",
        Name = "Suisse",
        CultureName = "de-CH", // séparateur de milliers « ' » et point décimal, usage courant dans toute la Suisse
        DefaultCurrency = "CHF",
        CurrencyBeforeAmount = true,
        TaxRates = [8.1m, 3.8m, 2.6m],
        DefaultTaxRate = 8.1m,
        TaxRateHint = "8,1 % : taux normal · 3,8 % : hébergement · 2,6 % : taux réduit.",
        TaxExemptionMention = "Non assujetti à la TVA.",
        CompanyIdLabel = "IDE",
        CompanyIdExample = "CHE-123.456.789",
        VatNumberLabel = "N° TVA",
        VatNumberExample = "CHE-123.456.789 TVA",
        InsuranceLabel = "Assurance RC professionnelle",
        DefaultPaymentTerms = "Payable à 30 jours net.",
        RoundingIncrement = 0.05m,
    };

    public static readonly CountryProfile Belgium = new()
    {
        Code = "BE",
        Name = "Belgique",
        CultureName = "fr-BE",
        DefaultCurrency = "EUR",
        TaxRates = [21m, 12m, 6m, 0m],
        DefaultTaxRate = 21m,
        TaxRateHint = "6 % : rénovation d'un logement privé de plus de 10 ans · 21 % : autres cas.",
        TaxExemptionMention = "Régime particulier de franchise des petites entreprises. TVA non applicable.",
        CompanyIdLabel = "N° d'entreprise",
        CompanyIdExample = "0123.456.789",
        VatNumberLabel = "N° TVA",
        VatNumberExample = "BE0123.456.789",
        InsuranceLabel = "Assurance RC professionnelle",
        DefaultPaymentTerms =
            "En cas de retard de paiement, des intérêts de retard et une indemnité forfaitaire pourront être réclamés " +
            "conformément à la loi du 2 août 2002.",
    };

    public static readonly CountryProfile Luxembourg = new()
    {
        Code = "LU",
        Name = "Luxembourg",
        CultureName = "fr-LU",
        DefaultCurrency = "EUR",
        TaxRates = [17m, 14m, 8m, 3m],
        DefaultTaxRate = 17m,
        TaxExemptionMention = "TVA non applicable — régime de franchise des petites entreprises.",
        CompanyIdLabel = "N° RCS",
        CompanyIdExample = "B123456",
        VatNumberLabel = "N° TVA",
        VatNumberExample = "LU12345678",
        DefaultPaymentTerms = "En cas de retard de paiement, des intérêts de retard au taux légal seront appliqués.",
    };

    public static readonly CountryProfile Other = new()
    {
        Code = "XX",
        Name = "Autre pays",
        CultureName = "fr-FR",
        DefaultCurrency = "EUR",
        TaxRates = [20m, 10m, 0m],
        DefaultTaxRate = 20m,
        TaxExemptionMention = "TVA non applicable.",
        CompanyIdLabel = "N° d'entreprise",
        VatNumberLabel = "N° de TVA",
        DefaultPaymentTerms = "Payable à 30 jours.",
    };

    public static readonly IReadOnlyList<CountryProfile> All = [France, Switzerland, Belgium, Luxembourg, Other];

    public static CountryProfile Get(string? code) =>
        All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)) ?? France;
}
