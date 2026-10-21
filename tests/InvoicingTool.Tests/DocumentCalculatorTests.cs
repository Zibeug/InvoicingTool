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
        Assert.Equal(new VatBreakdown(20m, 136.50m, 27.30m), totals.VatBreakdown[0]);
        Assert.Equal(new VatBreakdown(10m, 1000m, 100m), totals.VatBreakdown[1]);
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

    [Theory]
    [InlineData(100.02, 100.00)]
    [InlineData(100.03, 100.05)]
    [InlineData(100.07, 100.05)]
    [InlineData(100.08, 100.10)]
    public void Swiss_rounding_to_five_centimes(decimal exact, decimal expected)
    {
        var lines = new[] { new DocumentLine { Quantity = 1, UnitPrice = exact, VatRate = 0m } };

        var totals = DocumentCalculator.Compute(lines, vatExempt: false, roundingIncrement: 0.05m);

        Assert.Equal(expected, totals.TotalInclTax);
        Assert.Equal(expected - exact, totals.RoundingAdjustment);
    }

    [Fact]
    public void Swiss_vat_8_1_percent_then_rounded()
    {
        // 3 h × 85 CHF = 255 HT ; TVA 8,1 % = 20,655 → 20,66 ; total 275,66 → arrondi 275,65
        var lines = new[] { new DocumentLine { Quantity = 3, UnitPrice = 85m, VatRate = 8.1m } };

        var totals = DocumentCalculator.Compute(lines, vatExempt: false, roundingIncrement: 0.05m);

        Assert.Equal(20.66m, totals.TotalVat);
        Assert.Equal(275.65m, totals.TotalInclTax);
        Assert.Equal(-0.01m, totals.RoundingAdjustment);
    }
}
