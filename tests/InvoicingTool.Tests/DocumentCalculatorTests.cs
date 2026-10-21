using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;

namespace InvoicingTool.Tests;

public class DocumentCalculatorTests
{
    [Fact]
    public void Line_total_applies_quantity_price_and_discount_rounded_to_cents()
    {
        var line = new DocumentLine { Quantity = 12.5m, UnitPrice = 18.333m, DiscountPercent = 10m };
        // 12.5 * 18.333 * 0.9 = 206.24625
        Assert.Equal(206.25m, DocumentCalculator.LineTotalExclTax(line));
    }

    [Fact]
    public void Totals_group_vat_by_rate()
    {
        var lines = new[]
        {
            new DocumentLine { Quantity = 40, UnitPrice = 22m, VatRate = 10m },   // 880 HT
            new DocumentLine { Quantity = 3, UnitPrice = 45.5m, VatRate = 20m },  // 136.50 HT
            new DocumentLine { Quantity = 1, UnitPrice = 120m, VatRate = 10m },   // 120 HT
        };

        var totals = DocumentCalculator.Compute(lines, vatExempt: false);

        Assert.Equal(1136.50m, totals.TotalExclTax);
        Assert.Equal(2, totals.VatBreakdown.Count);
        Assert.Equal(new VatBreakdown(10m, 1000m, 100m), totals.VatBreakdown[0]);
        Assert.Equal(new VatBreakdown(20m, 136.50m, 27.30m), totals.VatBreakdown[1]);
        Assert.Equal(127.30m, totals.TotalVat);
        Assert.Equal(1263.80m, totals.TotalInclTax);
    }

    [Fact]
    public void Vat_exempt_company_charges_no_vat()
    {
        var lines = new[] { new DocumentLine { Quantity = 2, UnitPrice = 50m, VatRate = 20m } };

        var totals = DocumentCalculator.Compute(lines, vatExempt: true);

        Assert.Equal(100m, totals.TotalExclTax);
        Assert.Equal(0m, totals.TotalVat);
        Assert.Equal(100m, totals.TotalInclTax);
    }

    [Fact]
    public void Empty_document_has_zero_totals()
    {
        var totals = DocumentCalculator.Compute([], vatExempt: false);
        Assert.Equal(0m, totals.TotalInclTax);
        Assert.Empty(totals.VatBreakdown);
    }
}
