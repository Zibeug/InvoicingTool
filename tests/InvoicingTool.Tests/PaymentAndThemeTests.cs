using InvoicingTool.Core.Models;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.Abstractions;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.Tests;

public class PaymentAndThemeTests
{
    private static async Task<CommercialDocument> InvoiceAsync(TestDatabase t, bool issue = true)
    {
        var client = new Client { Name = "Studio Lumen" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Mandat", Quantity = 1, UnitPrice = 500m }]);
        if (issue)
            await t.Service.IssueAsync(invoice); // 15.03.2026
        return invoice;
    }

    private static async Task<DocumentEditViewModel> OpenAsync(TestDatabase t, CommercialDocument doc, FakeDialogs? dialogs = null)
    {
        var vm = new DocumentEditViewModel(t.Documents, t.Service, t.Settings, t.Catalog, t.Reminders, new FakeNavigation(), dialogs ?? new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = doc.Id });
        await vm.OnAppearingAsync();
        return vm;
    }

    [Fact]
    public async Task Invoice_can_be_marked_paid_at_a_chosen_date_and_undone()
    {
        await using var t = new TestDatabase();
        var invoice = await InvoiceAsync(t);
        t.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero));
        var vm = await OpenAsync(t, invoice, new FakeDialogs { ConfirmResult = true });
        Assert.True(vm.CanMarkPaid);
        Assert.False(vm.CanUndoPayment);

        vm.PaymentDate = new DateTime(2026, 3, 18); // payé il y a deux jours
        await vm.MarkPaidCommand.ExecuteAsync(null);

        var saved = (await t.Documents.GetAsync(invoice.Id))!;
        Assert.Equal((DocumentStatus.Paid, new DateTime(2026, 3, 18)), (saved.Status, saved.PaidDate!.Value));
        Assert.Equal("Payée", vm.StatusLabel);
        Assert.Equal("Payée le 18/03/2026", vm.PaymentInfo);
        Assert.False(vm.CanMarkPaid);
        Assert.True(vm.CanUndoPayment);

        await vm.UndoPaymentCommand.ExecuteAsync(null);

        Assert.Equal(DocumentStatus.Issued, (await t.Documents.GetAsync(invoice.Id))!.Status);
        Assert.True(vm.CanMarkPaid);
    }

    [Theory]
    [InlineData(2026, 3, 25, "futur")]
    [InlineData(2026, 3, 1, "précéder")]
    public async Task Payment_date_must_be_plausible(int y, int m, int d, string expected)
    {
        await using var t = new TestDatabase();
        var invoice = await InvoiceAsync(t);
        t.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero));
        var dialogs = new FakeDialogs();
        var vm = await OpenAsync(t, invoice, dialogs);

        vm.PaymentDate = new DateTime(y, m, d);
        await vm.MarkPaidCommand.ExecuteAsync(null);

        Assert.Contains(expected, Assert.Single(dialogs.Alerts));
        Assert.Equal(DocumentStatus.Issued, (await t.Documents.GetAsync(invoice.Id))!.Status);
    }

    [Fact]
    public async Task Draft_invoice_explains_it_must_be_issued_first()
    {
        await using var t = new TestDatabase();
        var draft = await InvoiceAsync(t, issue: false);

        var vm = await OpenAsync(t, draft);

        Assert.False(vm.CanMarkPaid);
        Assert.Contains("émettez la facture", vm.PaymentInfo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.MarkPaidAsync(draft, new DateTime(2026, 3, 15)));
    }

    [Fact]
    public async Task Overdue_invoice_can_be_marked_paid_from_the_dashboard()
    {
        await using var t = new TestDatabase();
        var invoice = await InvoiceAsync(t);
        t.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero));
        var vm = new DashboardViewModel(t.Dashboard, t.Reminders, t.Service, t.Documents, t.Settings, new FakeNavigation(), t.Clock,
            new FakeDialogs { ConfirmResult = true });
        await vm.OnAppearingAsync();

        await vm.MarkPaidCommand.ExecuteAsync(Assert.Single(vm.Overdue));

        Assert.Empty(vm.Overdue);
        var saved = (await t.Documents.GetAsync(invoice.Id))!;
        Assert.Equal((DocumentStatus.Paid, new DateTime(2026, 4, 30)), (saved.Status, saved.PaidDate!.Value));
    }

    [Fact]
    public async Task Theme_is_the_app_own_setting_saved_with_the_data()
    {
        await using var t = new TestDatabase();
        var theme = new FakeTheme();
        var vm = t.CreateSettingsViewModel(theme: theme);
        await vm.OnAppearingAsync();
        Assert.Equal(["Clair", "Sombre"], vm.ThemeOptions.Select(o => o.Name)); // pas de « comme l'appareil »
        Assert.Equal(ThemeChoice.Light, vm.SelectedTheme.Choice);

        vm.SelectedTheme = vm.ThemeOptions.Single(o => o.Choice == ThemeChoice.Dark);

        Assert.Equal(ThemeChoice.Dark, theme.Current); // appliqué sans attendre « Enregistrer »
        // L'enregistrement se fait en arrière-plan : on attend qu'il soit visible (au plus 2 s).
        for (var i = 0; i < 40 && (await t.Settings.GetSettingsAsync()).AppTheme != "Dark"; i++)
            await Task.Delay(50);
        Assert.Equal("Dark", (await t.Settings.GetSettingsAsync()).AppTheme);

        // « Enregistrer » ne remet pas l'ancien thème.
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Dark", (await t.Settings.GetSettingsAsync()).AppTheme);
    }

    [Fact]
    public async Task Saved_theme_is_applied_at_startup_and_follows_a_restore()
    {
        await using var source = new TestDatabase();
        await source.Settings.SaveAsync(new CompanySettings { AppTheme = "Dark" });
        var file = await source.Backup.ExportAsync();

        await using var target = new TestDatabase();
        await target.Settings.SaveAsync(new CompanySettings { TermsAcceptedVersion = TermsOfUse.Version });
        await target.Backup.RestoreAsync(InvoicingTool.Core.Services.BackupService.Read(new MemoryStream(file)));
        var theme = new FakeTheme();
        var startup = new StartupCoordinator(target.Settings, target.Backup, new FakeNavigation(), new FakeShare(), theme, new FakeDialogs());

        await startup.RunAsync();

        Assert.Equal(ThemeChoice.Dark, theme.Current);
    }
}
