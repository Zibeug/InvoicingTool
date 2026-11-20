using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.Tests;

public class DashboardAndRemindersViewModelTests
{
    private static async Task<CommercialDocument> OverdueInvoiceAsync(TestDatabase t, CompanySettings? company = null)
    {
        company ??= new CompanySettings { CompanyName = "Atelier Nova", ReminderFee2 = 20m };
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "Studio Lumen" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Mandat", Quantity = 1, UnitPrice = 1000m, VatRate = 20m }]);
        await t.Service.IssueAsync(invoice); // 15.03.2026, échéance 14.04.2026
        t.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero));
        return invoice;
    }

    private static DashboardViewModel Dashboard(TestDatabase t, FakeNavigation nav, FakeDialogs dialogs) =>
        new(t.Dashboard, t.Reminders, t.Service, t.Documents, t.Settings, nav, t.Clock, dialogs);

    [Fact]
    public async Task Dashboard_shows_figures_chart_and_overdue_invoice()
    {
        await using var t = new TestDatabase();
        await OverdueInvoiceAsync(t);
        var vm = Dashboard(t, new FakeNavigation(), new FakeDialogs());

        await vm.OnAppearingAsync();

        Assert.Equal("Bonjour, Atelier Nova", vm.Greeting);
        Assert.Equal("Chiffre d'affaires 2026", vm.YearLabel);
        Assert.Equal("1 000,00 €", vm.RevenueYearDisplay);
        Assert.Equal("1 facture à encaisser", vm.OutstandingCountText);
        Assert.Equal(12, vm.Months.Count);
        Assert.Equal(90, vm.Months.Single(m => m.Label == "mars").Height);
        Assert.True(vm.Months[^1].IsCurrentMonth);
        var row = Assert.Single(vm.Overdue);
        Assert.Equal(("Studio Lumen · F-2026-0001", "En retard de 10 jours", "Aucun rappel envoyé", "Envoyer le 1er rappel"),
            (row.Title, row.LateText, row.ReminderText, row.ActionText));
        Assert.False(vm.HasVatThreshold); // France, assujetti
        Assert.True(vm.IsBackupOverdue);
    }

    [Fact]
    public async Task Dashboard_remind_creates_reminder_and_opens_its_pdf()
    {
        await using var t = new TestDatabase();
        var invoice = await OverdueInvoiceAsync(t);
        var nav = new FakeNavigation();
        var vm = Dashboard(t, nav, new FakeDialogs { ConfirmResult = true });
        await vm.OnAppearingAsync();

        await vm.RemindCommand.ExecuteAsync(vm.Overdue[0]);

        var reminder = Assert.Single(await t.Documents.GetRemindersAsync(invoice.Id));
        var (route, parameters) = nav.History.Single();
        Assert.Equal(Routes.DocumentPreview, route);
        Assert.Equal(reminder.Id, parameters![Routes.ReminderParameter]);
    }

    [Fact]
    public async Task Dashboard_warns_when_swiss_vat_threshold_is_near()
    {
        await using var t = new TestDatabase();
        var company = new CompanySettings { CompanyName = "Léa Morel" };
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        company.VatExempt = true;
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "X" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Mandat annuel", Quantity = 1, UnitPrice = 90_000m }]);
        await t.Service.IssueAsync(invoice);
        var vm = Dashboard(t, new FakeNavigation(), new FakeDialogs());

        await vm.OnAppearingAsync();

        Assert.True(vm.HasVatThreshold);
        Assert.True(vm.IsVatAlert);
        Assert.Equal("Vous approchez du seuil de TVA", vm.VatTitle);
        Assert.Equal(0.9, vm.VatProgress, 3);
        Assert.Contains("CHF 100'000.00", vm.VatText);
    }

    [Fact]
    public async Task Invoice_screen_shows_overdue_state_and_sends_reminders()
    {
        await using var t = new TestDatabase();
        var invoice = await OverdueInvoiceAsync(t);
        var nav = new FakeNavigation();
        var vm = new DocumentEditViewModel(t.Documents, t.Service, t.Settings, t.Catalog, t.Reminders, nav, new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = invoice.Id });
        await vm.OnAppearingAsync();

        Assert.Equal("En retard de 10 j", vm.PaymentInfo);
        Assert.True(vm.CanRemind);
        Assert.Equal("Envoyer le 1er rappel", vm.RemindButtonText);

        await vm.RemindCommand.ExecuteAsync(null);

        Assert.False(vm.CanRemind); // le 2e rappel attend la fin du nouveau délai
        Assert.Equal("1er rappel du 24/04/2026 · 1 200,00 € à payer d'ici au 04/05/2026", Assert.Single(vm.Reminders).Text);
        Assert.Equal("En retard de 10 j · délai du 1er rappel jusqu'au 04/05/2026", vm.PaymentInfo);
        Assert.Equal(Routes.DocumentPreview, nav.History.Last().Route);
    }

    [Fact]
    public async Task Preview_renders_the_reminder_instead_of_the_invoice()
    {
        await using var t = new TestDatabase();
        var invoice = await OverdueInvoiceAsync(t);
        var reminder = await t.Reminders.CreateNextAsync(invoice);
        var vm = new DocumentPreviewViewModel(t.Documents, t.Service, t.Settings, new FakeShare(), new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = invoice.Id, [Routes.ReminderParameter] = reminder.Id });

        await vm.OnAppearingAsync();

        Assert.Equal("1er rappel F-2026-0001", vm.Title);
        Assert.Contains("RAPPEL — Facture n", vm.Html);
        Assert.StartsWith("Rappel1_", vm.PdfFileName);
    }

    [Fact]
    public async Task Invoice_list_shows_overdue_status()
    {
        await using var t = new TestDatabase();
        var invoice = await OverdueInvoiceAsync(t);
        Assert.Equal("En retard (10 j)", DisplayText.StatusFor(invoice, new DateTime(2026, 4, 24)));
        Assert.Equal("À payer", DisplayText.StatusFor(invoice, new DateTime(2026, 4, 14)));
    }

    [Fact]
    public async Task Settings_save_reminder_fees_threshold_and_export_the_year()
    {
        await using var t = new TestDatabase();
        await OverdueInvoiceAsync(t);
        var share = new FakeShare();
        var vm = t.CreateSettingsViewModel(share: share);
        await vm.OnAppearingAsync();
        Assert.Equal([2026], vm.ExportYears);

        vm.ReminderFee2Text = "25";
        vm.ReminderDelayDaysText = "14";
        vm.VatThresholdText = "0";
        await vm.SaveCommand.ExecuteAsync(null);
        var settings = await t.Settings.GetSettingsAsync();
        Assert.Equal((25m, 14, 0m), (settings.ReminderFee2, settings.ReminderDelayDays, settings.VatRegistrationThreshold!.Value));

        await vm.ExportAccountingCommand.ExecuteAsync(null);
        var (fileName, content, contentType) = Assert.Single(share.Shared);
        Assert.Equal(("export-fiduciaire-2026.zip", "application/zip"), (fileName, contentType));
        Assert.Equal("PK"u8.ToArray(), content[..2]);
    }

    [Fact]
    public async Task Settings_reject_invalid_reminder_delay()
    {
        await using var t = new TestDatabase();
        var dialogs = new FakeDialogs();
        var vm = t.CreateSettingsViewModel(dialogs);
        await vm.OnAppearingAsync();

        vm.ReminderDelayDaysText = "0";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("rappel", Assert.Single(dialogs.Alerts));
    }
}
