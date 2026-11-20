using System.IO.Compression;
using System.Text;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;

namespace InvoicingTool.Tests;

public class DashboardAndExportTests
{
    private static async Task<CommercialDocument> InvoiceAsync(TestDatabase t, Client client, decimal amount, decimal vat,
        int month, int day, bool issue = true)
    {
        t.Clock.SetUtcNow(new DateTimeOffset(2026, month, day, 9, 0, 0, TimeSpan.Zero));
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Prestation", Quantity = 1, UnitPrice = amount, VatRate = vat }]);
        if (issue)
            await t.Service.IssueAsync(invoice);
        return invoice;
    }

    private static async Task<Client> SetupAsync(TestDatabase t, bool vatExempt)
    {
        var company = new CompanySettings { CompanyName = "Atelier Nova" };
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        company.VatExempt = vatExempt;
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "Studio; Lumen" };
        await t.Clients.SaveAsync(client);
        return client;
    }

    [Fact]
    public async Task Dashboard_sums_revenue_collected_outstanding_and_overdue()
    {
        await using var t = new TestDatabase(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var client = await SetupAsync(t, vatExempt: false);
        var january = await InvoiceAsync(t, client, 1000m, 8.1m, 1, 10);     // payée en février
        t.Clock.SetUtcNow(new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero));
        await t.Service.SetStatusAsync(january, DocumentStatus.Paid);
        var march = await InvoiceAsync(t, client, 2000m, 8.1m, 3, 1);        // échue le 31.03
        var june = await InvoiceAsync(t, client, 500m, 8.1m, 6, 5);          // pas encore échue
        await InvoiceAsync(t, client, 9999m, 8.1m, 6, 6, issue: false);      // brouillon : ignoré
        var cancelled = await InvoiceAsync(t, client, 300m, 8.1m, 6, 7);
        await t.Service.SetStatusAsync(cancelled, DocumentStatus.Cancelled); // annulée : ignorée
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await t.Service.SaveDraftAsync(quote, [new() { Description = "Offre", Quantity = 1, UnitPrice = 700m, VatRate = 8.1m }]);
        await t.Service.IssueAsync(quote);

        t.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.Zero));
        var summary = await t.Dashboard.ComputeAsync();

        Assert.Equal(3500m, summary.RevenueYear);
        Assert.Equal(500m, summary.RevenueMonth);
        Assert.Equal(january.TotalInclTax, summary.CollectedYear);
        Assert.Equal(2, summary.OutstandingCount);
        Assert.Equal(march.TotalInclTax + june.TotalInclTax, summary.OutstandingTotal);
        var overdue = Assert.Single(summary.Overdue);
        Assert.Equal((march.Id, 81, 1), (overdue.Invoice.Id, overdue.DaysLate, overdue.NextReminderLevel!.Value));
        Assert.Equal((1, quote.TotalInclTax), (summary.PendingQuotesCount, summary.PendingQuotesTotal));
        Assert.Equal(12, summary.Last12Months.Count);
        Assert.Equal(("juin", 500m), (summary.Last12Months[^1].Label, summary.Last12Months[^1].Amount));
        Assert.Equal(1000m, summary.Last12Months.Single(m => m.Month == new DateTime(2026, 1, 1)).Amount);
        Assert.Equal(VatThresholdAlert.None, summary.VatAlert); // assujetti : pas d'alerte
    }

    [Theory]
    [InlineData(50_000, VatThresholdAlert.None)]
    [InlineData(85_000, VatThresholdAlert.Approaching)]
    [InlineData(100_000, VatThresholdAlert.Exceeded)]
    public async Task Vat_threshold_alert_for_non_registered_swiss_company(int revenue, VatThresholdAlert expected)
    {
        await using var t = new TestDatabase(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var client = await SetupAsync(t, vatExempt: true);
        await InvoiceAsync(t, client, revenue, 0m, 3, 1);

        var summary = await t.Dashboard.ComputeAsync();

        Assert.Equal(100_000m, summary.VatThreshold);
        Assert.Equal(expected, summary.VatAlert);
    }

    [Fact]
    public async Task Export_contains_csv_vat_banana_journal_pdfs_and_readme()
    {
        await using var t = new TestDatabase(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var client = await SetupAsync(t, vatExempt: false);
        var paid = await InvoiceAsync(t, client, 1000m, 8.1m, 2, 1);
        await InvoiceAsync(t, client, 400m, 8.1m, 3, 1);
        var cancelled = await InvoiceAsync(t, client, 300m, 8.1m, 3, 2);
        await t.Service.SetStatusAsync(cancelled, DocumentStatus.Cancelled);
        await InvoiceAsync(t, client, 777m, 8.1m, 3, 3, issue: false);   // brouillon : exclu
        t.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        await t.Service.SetStatusAsync(paid, DocumentStatus.Paid);

        Assert.Equal([2026], await t.Export.GetYearsAsync());
        var zipBytes = await t.Export.ExportYearAsync(2026);

        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        Assert.Equal(
            ["LISEZMOI.txt", "factures-2026.csv", "factures-pdf/F-2026-0001.pdf", "factures-pdf/F-2026-0002.pdf",
             "factures-pdf/F-2026-0003.pdf", "journal-banana-2026.txt", "tva-2026.csv"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));

        var csv = Read(zip, "factures-2026.csv");
        Assert.Equal(4, csv.Length); // en-tête + 3 factures émises
        Assert.Equal("F-2026-0001;01.02.2026;03.03.2026;\"Studio; Lumen\";;;Payée;CHF;1000.00;81.00;1081.00;10.03.2026;0", csv[1]);
        Assert.EndsWith("Annulée;CHF;300.00;24.30;324.30;;0", csv[3]);

        var vat = Read(zip, "tva-2026.csv");
        Assert.Equal("F-2026-0002;01.03.2026;Impayée;8.10;400.00;32.40", vat[2]);

        var journal = Read(zip, "journal-banana-2026.txt");
        Assert.Equal("Date\tDoc\tDescription\tAccountDebit\tAccountCredit\tAmount", journal[0]);
        Assert.Contains("2026-02-01\tF-2026-0001\tFacture F-2026-0001 – Studio; Lumen\t1100\t3400\t1000.00", journal);
        Assert.Contains("2026-02-01\tF-2026-0001\tTVA 8.1 % – Facture F-2026-0001 – Studio; Lumen\t1100\t2200\t81.00", journal);
        Assert.Contains("2026-03-10\tF-2026-0001\tPaiement facture F-2026-0001 – Studio; Lumen\t1020\t1100\t1081.00", journal);
        Assert.DoesNotContain(journal, l => l.Contains("F-2026-0003")); // annulée : pas d'écriture
        // Arrondi suisse : 400 + 32.40 = 432.40, déjà un multiple de 0.05, donc pas de ligne d'arrondi.
        Assert.DoesNotContain(journal, l => l.Contains("Arrondi"));
    }

    [Fact]
    public async Task Csv_uses_decimal_comma_outside_switzerland()
    {
        await using var t = new TestDatabase(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        await t.Settings.SaveAsync(new CompanySettings { CompanyName = "Atelier Nova" }); // France
        var client = new Client { Name = "Dupont" };
        await t.Clients.SaveAsync(client);
        await InvoiceAsync(t, client, 100m, 20m, 2, 1);

        using var zip = new ZipArchive(new MemoryStream(await t.Export.ExportYearAsync(2026)));

        Assert.EndsWith("EUR;100,00;20,00;120,00;;0", Read(zip, "factures-2026.csv")[1]);
    }

    private static string[] Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
