using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

public sealed record VatBreakdown(decimal Rate, decimal BaseExclTax, decimal VatAmount);

public sealed record DocumentTotals(
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    IReadOnlyList<VatBreakdown> VatBreakdown);

/// <summary>Calculs des montants. Arrondi au centime par ligne puis par taux de TVA.</summary>
public static class DocumentCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal LineTotalExclTax(DocumentLine line) =>
        Round(line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m));

    public static DocumentTotals Compute(IEnumerable<DocumentLine> lines, bool vatExempt)
    {
        var breakdown = lines
            .GroupBy(l => vatExempt ? 0m : l.VatRate)
            .Select(g =>
            {
                var baseAmount = g.Sum(LineTotalExclTax);
                return new VatBreakdown(g.Key, baseAmount, Round(baseAmount * g.Key / 100m));
            })
            .OrderBy(b => b.Rate)
            .ToList();

        var totalExclTax = breakdown.Sum(b => b.BaseExclTax);
        var totalVat = breakdown.Sum(b => b.VatAmount);
        return new DocumentTotals(totalExclTax, totalVat, totalExclTax + totalVat, breakdown);
    }

    public static void ApplyTotals(CommercialDocument document, DocumentTotals totals)
    {
        document.TotalExclTax = totals.TotalExclTax;
        document.TotalVat = totals.TotalVat;
        document.TotalInclTax = totals.TotalInclTax;
    }
}
