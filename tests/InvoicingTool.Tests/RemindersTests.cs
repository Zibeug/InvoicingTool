using InvoicingTool.Core.Models;
using InvoicingTool.Core.Pdf;
using InvoicingTool.Core.Services;
using PdfSharp.Pdf.IO;

namespace InvoicingTool.Tests;

public class RemindersTests
{
    /// <summary>Facture suisse de CHF 1 000, émise le 15.03.2026, échéance à 30 jours (14.04.2026).</summary>
    private static async Task<(CommercialDocument Invoice, CompanySettings Company)> IssueAsync(TestDatabase t)
    {
        var company = new CompanySettings
        {
            CompanyName = "Atelier Nova", Address = "Rue du Lac 4", PostalCode = "1003", City = "Lausanne",
            Iban = "CH93 0076 2011 6238 5295 7", VatExempt = true, ReminderFee1 = 0m, ReminderFee2 = 20m, ReminderFee3 = 30m,
        };
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        company.VatExempt = true;
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "Studio Lumen", PostalCode = "1003", City = "Lausanne" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Mandat", Quantity = 1, UnitPrice = 1000m }]);
        await t.Service.IssueAsync(invoice);
        return (invoice, company);
    }

    private static void GoTo(TestDatabase t, int month, int day) =>
        t.Clock.SetUtcNow(new DateTimeOffset(2026, month, day, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task No_reminder_before_due_date()
    {
        await using var t = new TestDatabase();
        var (invoice, _) = await IssueAsync(t);
        GoTo(t, 4, 14); // jour de l'échéance

        var state = await t.Reminders.GetStateAsync(invoice);

        Assert.False(state.IsOverdue);
        Assert.Null(state.NextLevel);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => t.Reminders.CreateNextAsync(invoice));
        Assert.Contains("pas encore échue", error.Message);
    }

    [Fact]
    public async Task Three_reminders_with_cumulative_fees_and_new_deadlines()
    {
        await using var t = new TestDatabase();
        var (invoice, _) = await IssueAsync(t);

        GoTo(t, 4, 20);
        var state = await t.Reminders.GetStateAsync(invoice);
        Assert.Equal((true, 6, 1), (state.IsOverdue, state.DaysLate, state.NextLevel!.Value));
        var first = await t.Reminders.CreateNextAsync(invoice);
        Assert.Equal((1, 0m, 1000m, new DateTime(2026, 4, 30)), (first.Level, first.Fee, first.TotalDue, first.NewDueDate));

        // Pas de 2e rappel avant la fin du délai du 1er.
        GoTo(t, 4, 30);
        Assert.Null((await t.Reminders.GetStateAsync(invoice)).NextLevel);

        GoTo(t, 5, 1);
        var second = await t.Reminders.CreateNextAsync(invoice);
        Assert.Equal((2, 20m, 1020m), (second.Level, second.Fee, second.TotalDue));

        GoTo(t, 5, 20);
        var third = await t.Reminders.CreateNextAsync(invoice);
        Assert.Equal((3, 30m, 1050m), (third.Level, third.Fee, third.TotalDue));

        GoTo(t, 6, 30);
        Assert.Null((await t.Reminders.GetStateAsync(invoice)).NextLevel);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => t.Reminders.CreateNextAsync(invoice));
        Assert.Contains("dernier rappel", error.Message);
    }

    [Fact]
    public async Task Paid_invoice_is_never_reminded()
    {
        await using var t = new TestDatabase();
        var (invoice, _) = await IssueAsync(t);
        GoTo(t, 5, 1);
        await t.Service.SetStatusAsync(invoice, DocumentStatus.Paid);

        var state = await t.Reminders.GetStateAsync(invoice);

        Assert.False(state.IsOverdue);
        Assert.Null(state.NextLevel);
    }

    [Fact]
    public async Task Reminder_document_shows_fees_deadline_and_qr_bill_with_same_reference()
    {
        await using var t = new TestDatabase();
        var (invoice, company) = await IssueAsync(t);
        GoTo(t, 4, 20);
        await t.Reminders.CreateNextAsync(invoice);
        GoTo(t, 5, 1);
        var second = await t.Reminders.CreateNextAsync(invoice);
        var all = await t.Documents.GetRemindersAsync(invoice.Id);

        var printable = ReminderService.BuildPrintable(second, all, invoice, company);

        Assert.Equal("2e RAPPEL — Facture n° F-2026-0001", printable.Heading);
        Assert.False(printable.ShowQuantities);
        Assert.Equal(["Facture n° F-2026-0001 du 15.03.2026, échue le 14.04.2026", "Frais de 2e rappel"],
            printable.Lines.Select(l => l.Description));
        Assert.Equal("CHF 1'020.00", Assert.Single(printable.Totals).Value);
        Assert.Contains("11.05.2026", printable.Notes);

        var bill = printable.QrBill!;
        Assert.Equal(1020m, bill.Amount);
        Assert.Equal("2e rappel - Facture F-2026-0001", bill.UnstructuredMessage);
        Assert.Equal(SwissQrBill.Create(invoice, company)!.Reference, bill.Reference);

        using var pdf = PdfReader.Open(new MemoryStream(PdfDocumentRenderer.Render(printable)), PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdf.PageCount);
        Assert.StartsWith("Rappel2_Facture_F-2026-0001", printable.FileNameWithoutExtension);
    }

    [Fact]
    public async Task Reminders_are_part_of_the_backup()
    {
        await using var source = new TestDatabase();
        var (invoice, _) = await IssueAsync(source);
        GoTo(source, 4, 20);
        await source.Reminders.CreateNextAsync(invoice);
        var file = await source.Backup.ExportAsync();

        await using var target = new TestDatabase();
        await target.Backup.RestoreAsync(BackupService.Read(new MemoryStream(file)));

        Assert.Equal(1, Assert.Single(await target.Documents.GetRemindersAsync(invoice.Id)).Level);
    }
}
