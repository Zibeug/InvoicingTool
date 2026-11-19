using InvoicingTool.Core.Models;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.Tests;

public class DictationViewModelTests
{
    private static async Task<(DictationViewModel Vm, CommercialDocument Doc, FakeSpeech Speech, FakeNavigation Nav, FakeDialogs Dialogs)>
        OpenAsync(TestDatabase t, CompanySettings? company = null)
    {
        await t.Settings.SaveAsync(company ?? new CompanySettings());
        var client = new Client { Name = "Studio Lumen" };
        await t.Clients.SaveAsync(client);
        var doc = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await t.Service.SaveDraftAsync(doc, [new() { Description = "Ligne existante", Quantity = 1, UnitPrice = 10m, VatRate = 20m }]);

        var speech = new FakeSpeech();
        var nav = new FakeNavigation();
        var dialogs = new FakeDialogs();
        var vm = new DictationViewModel(t.Documents, t.Service, t.Settings, t.Catalog, speech, nav, dialogs);
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = doc.Id });
        await vm.OnAppearingAsync();
        return (vm, doc, speech, nav, dialogs);
    }

    [Fact]
    public async Task Dictated_lines_are_previewed_live_and_appended_to_the_document()
    {
        await using var t = new TestDatabase();
        var (vm, doc, speech, nav, _) = await OpenAsync(t);

        await vm.ToggleListenCommand.ExecuteAsync(null);
        Assert.True(vm.IsListening);
        Assert.Equal("Terminer", vm.ListenButtonText);
        speech.Say("3 heures de conseil à 120 euros puis un forfait déplacement à 50 euros");

        Assert.False(vm.IsListening);
        Assert.Equal(2, vm.LineCount);
        Assert.Equal("Ajouter 2 lignes", vm.AddButtonText);
        Assert.Equal("Conseil", vm.Lines[0].Title);
        Assert.Equal("3 h × 120,00 € = 360,00 €", vm.Lines[0].Detail);

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(1, nav.BackCount);
        var lines = await t.Service.GetLinesAsync(doc.Id);
        Assert.Equal(["Ligne existante", "Conseil", "Déplacement"], lines.Select(l => l.Description));
        Assert.Equal(20m, lines[1].VatRate); // taux par défaut de l'entreprise
        Assert.Equal(420m, (await t.Documents.GetAsync(doc.Id))!.TotalExclTax);
    }

    [Fact]
    public async Task Successive_dictations_add_up_and_text_stays_editable()
    {
        await using var t = new TestDatabase();
        var (vm, _, speech, _, _) = await OpenAsync(t);

        await vm.ToggleListenCommand.ExecuteAsync(null);
        speech.Say("2 heures de cours à 50 euros");
        await vm.ToggleListenCommand.ExecuteAsync(null);
        speech.Say("1 manuel à 30 euros");
        Assert.Equal("2 heures de cours à 50 euros, 1 manuel à 30 euros", vm.Text);
        Assert.Equal(2, vm.LineCount);

        // Correction à la main d'un mot mal compris
        vm.Text = vm.Text.Replace("manuel", "classeur");
        Assert.Equal("Classeur", vm.Lines[1].Title);
    }

    [Fact]
    public async Task Missing_price_is_flagged_and_added_at_zero()
    {
        await using var t = new TestDatabase();
        var (vm, doc, _, _, _) = await OpenAsync(t);

        vm.Text = "2 heures de rangement";

        Assert.True(Assert.Single(vm.Lines).MissingPrice);
        Assert.Contains("prix à compléter", vm.Lines[0].Detail);
        await vm.AddCommand.ExecuteAsync(null);
        Assert.Equal(0m, (await t.Service.GetLinesAsync(doc.Id))[1].UnitPrice);
    }

    [Fact]
    public async Task Speech_uses_the_country_language_and_reports_microphone_problems()
    {
        await using var t = new TestDatabase();
        var company = new CompanySettings();
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        var (vm, _, speech, _, dialogs) = await OpenAsync(t, company);
        speech.StartError = "L'accès au micro a été refusé.";

        await vm.ToggleListenCommand.ExecuteAsync(null);

        Assert.Equal("fr-CH", speech.LastCulture);
        Assert.False(vm.IsListening);
        Assert.Equal("L'accès au micro a été refusé.", Assert.Single(dialogs.Alerts));
        Assert.Contains("francs", vm.Example);
    }

    [Fact]
    public async Task Vat_exempt_company_gets_lines_without_vat_and_catalog_prices()
    {
        await using var t = new TestDatabase();
        await t.Catalog.SaveAsync(new CatalogItem { Label = "Coupe homme", Unit = "u", UnitPrice = 35m, VatRate = 8.1m });
        var (vm, doc, _, _, _) = await OpenAsync(t, new CompanySettings { VatExempt = true });

        vm.Text = "deux coupes homme";
        await vm.AddCommand.ExecuteAsync(null);

        var added = (await t.Service.GetLinesAsync(doc.Id))[1];
        Assert.Equal(("Coupe homme", 2m, 35m, 0m), (added.Description, added.Quantity, added.UnitPrice, added.VatRate));
    }

    [Fact]
    public async Task Editor_saves_pending_edits_before_opening_dictation()
    {
        await using var t = new TestDatabase();
        var client = new Client { Name = "X" };
        await t.Clients.SaveAsync(client);
        var doc = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        var nav = new FakeNavigation();
        var editor = new DocumentEditViewModel(t.Documents, t.Service, t.Settings, t.Catalog, nav, new FakeDialogs());
        editor.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = doc.Id });
        await editor.OnAppearingAsync();

        editor.AddLineCommand.Execute(null);
        editor.Lines[0].Description = "Saisie non enregistrée";
        await editor.DictateCommand.ExecuteAsync(null);

        Assert.Equal(Routes.Dictation, nav.History.Single().Route);
        Assert.Equal("Saisie non enregistrée", Assert.Single(await t.Service.GetLinesAsync(doc.Id)).Description);
    }
}
