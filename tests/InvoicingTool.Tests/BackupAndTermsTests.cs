using System.Text;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.Tests;

public class BackupAndTermsTests
{
    private static async Task<CommercialDocument> SeedAsync(TestDatabase t)
    {
        await t.Settings.SaveAsync(new CompanySettings { CompanyName = "Atelier Nova", CountryCode = "CH", CurrencyCode = "CHF" });
        var client = new Client { Name = "Studio Lumen", City = "Genève" };
        await t.Clients.SaveAsync(client);
        await t.Catalog.SaveAsync(new CatalogItem { Label = "Heure de conseil", Unit = "h", UnitPrice = 120m, VatRate = 8.1m });
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Atelier de formation", Quantity = 3.5m, Unit = "h", UnitPrice = 120m, VatRate = 8.1m }]);
        await t.Service.IssueAsync(invoice);
        return invoice;
    }

    [Fact]
    public async Task Backup_restores_everything_on_another_phone()
    {
        await using var source = new TestDatabase();
        var invoice = await SeedAsync(source);
        var file = await source.Backup.ExportAsync();

        await using var target = new TestDatabase();
        await target.Clients.SaveAsync(new Client { Name = "Donnée à remplacer" });
        await target.Backup.RestoreAsync(BackupService.Read(new MemoryStream(file)));

        var clients = await target.Clients.GetAllAsync();
        Assert.Equal("Studio Lumen", Assert.Single(clients).Name);
        var restored = (await target.Documents.GetAsync(invoice.Id))!;
        Assert.Equal("F-2026-0001", restored.Number);
        Assert.Equal(invoice.TotalInclTax, restored.TotalInclTax);
        Assert.Equal("CHF", restored.CurrencyCode);
        Assert.Equal(3.5m, Assert.Single(await target.Service.GetLinesAsync(invoice.Id)).Quantity);
        Assert.Equal("Heure de conseil", Assert.Single(await target.Catalog.GetAllAsync()).Label);
        Assert.Equal("Atelier Nova", (await target.Settings.GetSettingsAsync()).CompanyName);

        // La numérotation continue après la restauration : pas de doublon de numéro de facture.
        var next = await target.Service.CreateDraftAsync(DocumentType.Invoice, clients[0]);
        await target.Service.SaveDraftAsync(next, [new() { Description = "Suivi", Quantity = 1, UnitPrice = 50m }]);
        await target.Service.IssueAsync(next);
        Assert.Equal("F-2026-0002", next.Number);
    }

    [Fact]
    public async Task Backup_file_is_readable_json_without_computed_fields()
    {
        await using var t = new TestDatabase();
        await SeedAsync(t);

        var json = Encoding.UTF8.GetString(await t.Backup.ExportAsync());

        Assert.Contains("\"format\": \"InvoicingTool.Backup\"", json);
        Assert.Contains("\"type\": \"Invoice\"", json);
        Assert.Contains("Studio Lumen", json);
        Assert.DoesNotContain("totalInclTaxDisplay", json);
        Assert.DoesNotContain("fullAddress", json);
    }

    [Fact]
    public async Task Export_records_backup_date_and_clears_reminder()
    {
        await using var t = new TestDatabase();
        await SeedAsync(t);
        Assert.True(await t.Backup.IsBackupOverdueAsync(TimeSpan.FromDays(30)));

        await t.Backup.ExportAsync();

        Assert.False(await t.Backup.IsBackupOverdueAsync(TimeSpan.FromDays(30)));
        t.Clock.Advance(TimeSpan.FromDays(31));
        Assert.True(await t.Backup.IsBackupOverdueAsync(TimeSpan.FromDays(30)));
    }

    [Fact]
    public async Task No_reminder_while_there_is_nothing_to_lose()
    {
        await using var t = new TestDatabase();
        Assert.False(await t.Backup.IsBackupOverdueAsync(TimeSpan.FromDays(30)));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("{\"format\":\"autre chose\"}")]
    [InlineData("{\"format\":\"InvoicingTool.Backup\",\"version\":99}")]
    public void Invalid_backup_files_are_rejected_before_anything_is_deleted(string content) =>
        Assert.Throws<InvalidOperationException>(() => BackupService.Read(new MemoryStream(Encoding.UTF8.GetBytes(content))));

    [Fact]
    public async Task Restore_keeps_terms_acceptance_of_this_phone()
    {
        await using var source = new TestDatabase();
        await SeedAsync(source);
        var file = await source.Backup.ExportAsync();

        await using var target = new TestDatabase();
        await target.Settings.SaveAsync(new CompanySettings { TermsAcceptedVersion = TermsOfUse.Version, TermsAcceptedAt = DateTime.UtcNow });
        await target.Backup.RestoreAsync(BackupService.Read(new MemoryStream(file)));

        Assert.True(await TermsViewModel.AreAcceptedAsync(target.Settings));
    }

    [Fact]
    public async Task Settings_restore_asks_confirmation_then_replaces_data()
    {
        await using var source = new TestDatabase();
        await SeedAsync(source);
        var picker = new FakeFilePicker { Content = await source.Backup.ExportAsync() };

        await using var target = new TestDatabase();
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var vm = new SettingsViewModel(target.Settings, target.Backup, new FakeShare(), picker, new FakeNavigation(), dialogs);
        await vm.OnAppearingAsync();

        await vm.RestoreBackupCommand.ExecuteAsync(null);
        Assert.Empty(await target.Clients.GetAllAsync());

        dialogs.ConfirmResult = true;
        await vm.RestoreBackupCommand.ExecuteAsync(null);
        Assert.Single(await target.Clients.GetAllAsync());
        Assert.Equal("Atelier Nova", vm.CompanyName);
    }

    [Fact]
    public async Task Settings_export_shares_a_json_file()
    {
        await using var t = new TestDatabase();
        await SeedAsync(t);
        var share = new FakeShare();
        var vm = new SettingsViewModel(t.Settings, t.Backup, share, new FakeFilePicker(), new FakeNavigation(), new FakeDialogs());

        await vm.ExportBackupCommand.ExecuteAsync(null);

        var (fileName, content, contentType) = Assert.Single(share.Shared);
        Assert.StartsWith("sauvegarde-devis-factures-2026-03-15", fileName);
        Assert.Equal("application/json", contentType);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task Saving_settings_after_export_keeps_backup_date()
    {
        await using var t = new TestDatabase();
        await SeedAsync(t);
        var vm = new SettingsViewModel(t.Settings, t.Backup, new FakeShare(), new FakeFilePicker(), new FakeNavigation(), new FakeDialogs());
        await vm.OnAppearingAsync();

        await vm.ExportBackupCommand.ExecuteAsync(null);
        vm.Phone = "079 000 00 00";
        await vm.SaveCommand.ExecuteAsync(null);

        var settings = await t.Settings.GetSettingsAsync();
        Assert.NotNull(settings.LastBackupAt);
        Assert.Equal("079 000 00 00", settings.Phone);
    }

    [Fact]
    public async Task First_launch_opens_terms_and_acceptance_is_remembered()
    {
        await using var t = new TestDatabase();
        var nav = new FakeNavigation();
        var startup = new StartupCoordinator(t.Settings, t.Backup, nav, new FakeShare(), new FakeDialogs());

        await startup.RunAsync();
        Assert.Equal(Routes.Terms, nav.History.Single().Route);

        var terms = new TermsViewModel(t.Settings, nav, t.Clock, new FakeDialogs());
        await terms.OnAppearingAsync();
        Assert.True(terms.MustAccept);
        await terms.AcceptCommand.ExecuteAsync(null);

        Assert.False(terms.MustAccept);
        Assert.Equal(1, nav.BackCount);
        var settings = await t.Settings.GetSettingsAsync();
        Assert.Equal(TermsOfUse.Version, settings.TermsAcceptedVersion);

        await startup.RunAsync();
        Assert.Single(nav.History); // plus de redirection vers les conditions
    }

    [Fact]
    public async Task Declining_terms_keeps_the_gate_closed()
    {
        await using var t = new TestDatabase();
        var dialogs = new FakeDialogs();
        var terms = new TermsViewModel(t.Settings, new FakeNavigation(), t.Clock, dialogs);
        await terms.OnAppearingAsync();

        await terms.DeclineCommand.ExecuteAsync(null);

        Assert.True(terms.MustAccept);
        Assert.Single(dialogs.Alerts);
        Assert.False(await TermsViewModel.AreAcceptedAsync(t.Settings));
    }

    [Fact]
    public async Task Startup_reminds_once_to_back_up_and_exports_on_request()
    {
        await using var t = new TestDatabase();
        await SeedAsync(t);
        await t.Settings.SaveAsync(await AcceptedSettingsAsync(t));
        var share = new FakeShare();
        var startup = new StartupCoordinator(t.Settings, t.Backup, new FakeNavigation(), share, new FakeDialogs { ConfirmResult = true });

        await startup.RunAsync();
        await startup.RunAsync();

        Assert.Single(share.Shared);
    }

    [Fact]
    public void Terms_mention_swiss_law_and_data_responsibility()
    {
        Assert.Contains("droit suisse", TermsOfUse.Text);
        Assert.Contains("seul responsable de la conservation de ses données", TermsOfUse.Text);
    }

    private static async Task<CompanySettings> AcceptedSettingsAsync(TestDatabase t)
    {
        var settings = await t.Settings.GetSettingsAsync();
        settings.TermsAcceptedVersion = TermsOfUse.Version;
        return settings;
    }
}
