using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

public sealed record VatBreakdown(decimal Rate, decimal BaseExclTax, decimal VatAmount);

public sealed record DocumentTotals(
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    IReadOnlyList<VatBreakdown> VatBreakdown,
    decimal RoundingAdjustment = 0m);

/// <summary>Calculs des montants. Arrondi au centime par ligne puis par taux de TVA.</summary>
public static class DocumentCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Arrondit au multiple le plus proche (ex. 0,05 pour les 5 centimes suisses).</summary>
    public static decimal RoundTo(decimal value, decimal increment) =>
        increment <= 0.01m ? Round(value) : Math.Round(value / increment, MidpointRounding.AwayFromZero) * increment;

    public static decimal LineTotalExclTax(DocumentLine line) =>
        Round(line.Quantity * line.UnitPrice * (1m - line.DiscountPercent / 100m));

    /// <summary>Totaux selon les règles fiscales enregistrées sur le document.</summary>
    public static DocumentTotals Compute(CommercialDocument document, IEnumerable<DocumentLine> lines) =>
        Compute(lines, document.VatExempt, document.RoundingIncrement);

    public static DocumentTotals Compute(IEnumerable<DocumentLine> lines, bool vatExempt, decimal roundingIncrement = 0.01m)
    {
        var breakdown = lines
            .GroupBy(l => vatExempt ? 0m : l.VatRate)
            .Select(g =>
            {
                var baseAmount = g.Sum(LineTotalExclTax);
                return new VatBreakdown(g.Key, baseAmount, Round(baseAmount * g.Key / 100m));
            })
            .OrderByDescending(b => b.Rate)
            .ToList();

        var totalExclTax = breakdown.Sum(b => b.BaseExclTax);
        var totalVat = breakdown.Sum(b => b.VatAmount);
        var exact = totalExclTax + totalVat;
        var rounded = RoundTo(exact, roundingIncrement);
        return new DocumentTotals(totalExclTax, totalVat, rounded, breakdown, rounded - exact);
    }

    public static void ApplyTotals(CommercialDocument document, DocumentTotals totals)
    {
        document.TotalExclTax = totals.TotalExclTax;
        document.TotalVat = totals.TotalVat;
        document.TotalInclTax = totals.TotalInclTax;
    }
}
