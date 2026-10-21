using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

public sealed record PrintableLine(
    string Description, string Quantity, string Unit, string UnitPrice, string Discount, string TaxRate, string Total);

public sealed record PrintableTotal(string Label, string Value, bool IsGrandTotal = false);

/// <summary>
/// Contenu d'un devis ou d'une facture, déjà formaté selon le pays et la devise.
/// Partagé par le rendu HTML (aperçu à l'écran) et le rendu PDF, qui affichent ainsi la même chose.
/// </summary>
public sealed record PrintableDocument
{
    public required string Heading { get; init; }
    public string? Banner { get; init; }
    public required string CompanyName { get; init; }
    public required IReadOnlyList<string> CompanyLines { get; init; }
    public required string ClientName { get; init; }
    public required IReadOnlyList<string> ClientLines { get; init; }
    public required IReadOnlyList<(string Label, string Value)> Dates { get; init; }
    public string? Subject { get; init; }
    public string? SiteAddress { get; init; }
    public required string TaxName { get; init; }
    public required bool ShowTax { get; init; }
    public required bool ShowDiscount { get; init; }
    public required IReadOnlyList<PrintableLine> Lines { get; init; }
    public required IReadOnlyList<PrintableTotal> Totals { get; init; }
    public string? TaxExemptionMention { get; init; }
    public string? Notes { get; init; }
    public string? PaidMention { get; init; }
    /// <summary>Petits caractères en bas de document : règlement, conditions, assurance.</summary>
    public required IReadOnlyList<string> FinePrint { get; init; }
    public string? SignatureText { get; init; }
    public required string FileNameWithoutExtension { get; init; }

    public static PrintableDocument Build(CommercialDocument doc, IReadOnlyList<DocumentLine> lines, CompanySettings company)
    {
        var fmt = MoneyFormatter.For(doc);
        var profile = CountryProfiles.Get(doc.CountryCode);
        var totals = DocumentCalculator.Compute(doc, lines);
        var isQuote = doc.Type == DocumentType.Quote;
        var title = isQuote ? "DEVIS" : "FACTURE";
        var showTax = !doc.VatExempt;

        var companyLines = NonEmpty(
            company.OwnerName,
            company.Address,
            Join(" ", company.PostalCode, company.City),
            company.Phone,
            company.Email,
            Prefixed($"{profile.CompanyIdLabel} : ", company.Siret),
            showTax ? Prefixed($"{profile.VatNumberLabel} : ", company.VatNumber) : null);

        var dates = new List<(string, string)> { ("Date", fmt.Date(doc.IssueDate)) };
        if (doc.DueDate is { } due)
            dates.Add((isQuote ? "Valable jusqu'au" : "Échéance", fmt.Date(due)));

        var printableLines = lines.Select(l => new PrintableLine(
            l.Description,
            fmt.Number(l.Quantity),
            l.Unit,
            fmt.Format(l.UnitPrice),
            fmt.Number(l.DiscountPercent) + " %",
            fmt.Number(l.VatRate) + " %",
            fmt.Format(DocumentCalculator.LineTotalExclTax(l)))).ToList();

        var totalRows = new List<PrintableTotal> { new("Total HT", fmt.Format(totals.TotalExclTax)) };
        if (showTax)
        {
            foreach (var vat in totals.VatBreakdown)
                totalRows.Add(new($"{profile.TaxName} {fmt.Number(vat.Rate)} % sur {fmt.Format(vat.BaseExclTax)}", fmt.Format(vat.VatAmount)));
        }
        if (totals.RoundingAdjustment != 0)
            totalRows.Add(new("Arrondi", fmt.Format(totals.RoundingAdjustment)));
        totalRows.Add(new(showTax ? "Total TTC" : "Total", fmt.Format(totals.TotalInclTax), IsGrandTotal: doc.DepositAmount <= 0));
        if (doc.DepositAmount > 0)
        {
            totalRows.Add(new("Acompte versé", "- " + fmt.Format(doc.DepositAmount)));
            totalRows.Add(new("Reste à payer", fmt.Format(totals.TotalInclTax - doc.DepositAmount), IsGrandTotal: true));
        }

        var finePrint = new List<string>();
        if (!isQuote)
        {
            if (!string.IsNullOrWhiteSpace(company.Iban))
                finePrint.Add($"Règlement par virement — IBAN : {company.Iban}");
            if (!string.IsNullOrWhiteSpace(company.PaymentTerms))
                finePrint.Add(company.PaymentTerms);
        }
        if (!string.IsNullOrWhiteSpace(company.InsuranceInfo))
            finePrint.Add($"{profile.InsuranceLabel} : {company.InsuranceInfo}");

        return new PrintableDocument
        {
            Heading = $"{title} n° {doc.DisplayNumber}",
            Banner = doc.Status switch
            {
                DocumentStatus.Draft => "BROUILLON — document non émis",
                DocumentStatus.Cancelled => "ANNULÉ",
                _ => null,
            },
            CompanyName = company.CompanyName,
            CompanyLines = companyLines,
            ClientName = doc.ClientName,
            ClientLines = NonEmpty(doc.ClientAddress, Prefixed($"{profile.CompanyIdLabel} / TVA : ", doc.ClientCompanyId)),
            Dates = dates,
            Subject = Blank(doc.Title),
            SiteAddress = Blank(doc.SiteAddress),
            TaxName = profile.TaxName,
            ShowTax = showTax,
            ShowDiscount = lines.Any(l => l.DiscountPercent != 0),
            Lines = printableLines,
            Totals = totalRows,
            TaxExemptionMention = doc.VatExempt
                ? Blank(company.VatExemptionMention) ?? profile.TaxExemptionMention
                : null,
            Notes = Blank(doc.Notes),
            PaidMention = !isQuote && doc.Status == DocumentStatus.Paid && doc.PaidDate is { } paid
                ? $"Facture acquittée le {fmt.Date(paid)}"
                : null,
            FinePrint = finePrint,
            SignatureText = isQuote ? profile.QuoteAcceptanceText : null,
            FileNameWithoutExtension = FileName($"{(isQuote ? "Devis" : "Facture")}_{doc.Number ?? "brouillon"}_{doc.ClientName}"),
        };
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IReadOnlyList<string> NonEmpty(params string?[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();

    private static string? Join(string separator, params string?[] parts)
    {
        var joined = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return joined.Length == 0 ? null : joined;
    }

    private static string? Prefixed(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : prefix + value;

    private static string FileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat([' ', '/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        return new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
