using System.Text.RegularExpressions;
using Codecrete.SwissQRBill.Generator;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>
/// QR-facture suisse (section paiement) selon les normes SIX, générée avec la bibliothèque SwissQRBill (MIT).
/// Uniquement pour les factures en CHF avec un IBAN suisse ou liechtensteinois « normal » (pas de QR-IBAN) :
/// la référence est alors une référence internationale « RF… » tirée du numéro de facture.
/// </summary>
public static class SwissQrBill
{
    /// <summary>Police admise par les normes, intégrée au PDF (Liberation Sans, licence SIL OFL).</summary>
    public const string FontFamily = "Liberation Sans";

    /// <summary>Ce qui manque dans les paramètres de l'entreprise pour imprimer la QR-facture (vide = tout est prêt).</summary>
    public static IReadOnlyList<string> CompanyIssues(CompanySettings company)
    {
        var issues = new List<string>();
        if (company.CountryCode != CountryProfiles.Switzerland.Code)
            issues.Add("Le pays de l'entreprise doit être la Suisse.");
        if (company.CurrencyCode != "CHF")
            issues.Add("La devise doit être le franc suisse (CHF).");

        var iban = Iban.Normalize(company.Iban);
        if (iban.Length == 0)
            issues.Add("Indiquez votre IBAN.");
        else if (!Iban.IsValid(iban))
            issues.Add("L'IBAN n'est pas valide.");
        else if (!iban.StartsWith("CH") && !iban.StartsWith("LI"))
            issues.Add("L'IBAN doit être suisse (CH) ou liechtensteinois (LI).");
        else if (Payments.IsQrIban(iban))
            issues.Add("Cet IBAN est un QR-IBAN : indiquez votre IBAN habituel.");

        if (string.IsNullOrWhiteSpace(company.CompanyName))
            issues.Add("Indiquez le nom de l'entreprise.");
        if (string.IsNullOrWhiteSpace(company.PostalCode) || string.IsNullOrWhiteSpace(company.City))
            issues.Add("Indiquez le code postal et la ville de l'entreprise.");
        return issues;
    }

    /// <summary>
    /// QR-facture d'un document, ou null si elle ne s'applique pas (devis, autre devise, facture payée ou annulée,
    /// réglage désactivé, informations de l'entreprise incomplètes).
    /// </summary>
    public static Bill? Create(CommercialDocument doc, CompanySettings company)
    {
        if (!company.QrBillEnabled || doc.Type != DocumentType.Invoice ||
            doc.Status is not (DocumentStatus.Draft or DocumentStatus.Issued) ||
            doc.CurrencyCode != "CHF" || doc.TotalInclTax <= 0 || CompanyIssues(company).Count > 0)
            return null;

        var bill = new Bill
        {
            Account = Iban.Normalize(company.Iban),
            Creditor = StructuredAddress(company.CompanyName, company.Address, company.PostalCode, company.City,
                CountryProfiles.Switzerland.Code),
            Amount = doc.TotalInclTax,
            Currency = "CHF",
            Debtor = Debtor(doc),
            UnstructuredMessage = doc.Number is null ? "Facture" : $"Facture {doc.Number}",
            Format = new BillFormat
            {
                Language = Language.FR,
                SeparatorType = SeparatorType.DashedLineWithScissors,
                OutputSize = OutputSize.A4PortraitSheet,
                GraphicsFormat = GraphicsFormat.PDF,
                FontFamily = FontFamily,
            },
        };
        if (doc.Number is not null && CreditorReferenceSource(doc.Number) is { Length: > 0 } raw)
            bill.CreateAndSetCreditorReference(raw);

        // Un débiteur invalide (adresse incomplète, pays inconnu…) est retiré : il est facultatif.
        if (bill.Debtor is not null && QRBill.Validate(bill).HasErrors)
            bill.Debtor = null;
        return QRBill.Validate(bill).HasErrors ? null : bill;
    }

    /// <summary>Contenu du code QR (pour les tests et le contrôle).</summary>
    public static string QrCodeText(Bill bill) => QRBill.EncodeQrCodeText(bill);

    /// <summary>Section paiement seule (210 × 105 mm) en SVG, pour l'aperçu à l'écran.</summary>
    public static byte[] Svg(Bill bill)
    {
        var original = bill.Format;
        bill.Format = new BillFormat(original) { OutputSize = OutputSize.QrBillOnly, GraphicsFormat = GraphicsFormat.SVG };
        try
        {
            return QRBill.Generate(bill);
        }
        finally
        {
            bill.Format = original;
        }
    }

    /// <summary>« F-2026-0012 » → « F20260012 » : base de la référence RF (lettres et chiffres, 21 au maximum).</summary>
    public static string CreditorReferenceSource(string number)
    {
        var raw = Regex.Replace(number.ToUpperInvariant(), "[^A-Z0-9]", "");
        return raw.Length > 21 ? raw[^21..] : raw;
    }

    private static Address? Debtor(CommercialDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.ClientName) || string.IsNullOrWhiteSpace(doc.ClientPostalCode) ||
            string.IsNullOrWhiteSpace(doc.ClientCity))
            return null;
        var country = string.IsNullOrWhiteSpace(doc.ClientCountryCode) ? CountryProfiles.Switzerland.Code : doc.ClientCountryCode;
        return StructuredAddress(doc.ClientName, doc.ClientStreet, doc.ClientPostalCode, doc.ClientCity, country);
    }

    private static Address StructuredAddress(string name, string? streetLine, string? postalCode, string? town, string country)
    {
        var (street, houseNo) = SplitStreet(streetLine);
        return new Address
        {
            Name = name.Trim(),
            Street = street,
            HouseNo = houseNo,
            PostalCode = postalCode?.Trim(),
            Town = town?.Trim(),
            CountryCode = country.Trim().ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Sépare la rue et le numéro, comme l'exigent les adresses structurées :
    /// « Rue du Lac 4 » → (« Rue du Lac », « 4 ») ; « 12bis avenue de la Gare » → (« avenue de la Gare », « 12bis »).
    /// </summary>
    public static (string? Street, string? HouseNo) SplitStreet(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return (null, null);
        var text = line.Trim();
        const string number = @"\d+\s?(?:[a-zA-Z]|bis|ter)?";
        var trailing = Regex.Match(text, $@"^(?<street>.*\D)[\s,]+(?<no>{number})$");
        if (trailing.Success)
            return (trailing.Groups["street"].Value.Trim().TrimEnd(','), trailing.Groups["no"].Value.Replace(" ", ""));
        var leading = Regex.Match(text, $@"^(?<no>{number})[\s,]+(?<street>\D.*)$");
        if (leading.Success)
            return (leading.Groups["street"].Value.Trim(), leading.Groups["no"].Value.Replace(" ", ""));
        return (text, null);
    }
}
