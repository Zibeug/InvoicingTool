using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.Tests;

public class CatalogViewModelTests
{
    [Theory]
    [InlineData("Heure de conseil", "HC")]
    [InlineData("Coupe homme", "CH")]
    [InlineData("Massage", "MA")]
    [InlineData("Forfait d'entretien annuel", "FE")]
    [InlineData("A", "A")]
    public void Initials_use_significant_words(string label, string expected) =>
        Assert.Equal(expected, CatalogViewModel.Initials(label));

    [Theory]
    [InlineData("h", "par heure")]
    [InlineData("forfait", "forfait")]
    [InlineData("u", "l'unité")]
    [InlineData("m²", "par m²")]
    public void Units_are_spelled_out(string unit, string expected) =>
        Assert.Equal(expected, CatalogViewModel.UnitDisplay(unit));

    [Fact]
    public async Task List_shows_price_unit_vat_and_filters_by_search()
    {
        await using var t = new TestDatabase();
        var company = new CompanySettings { AccentColor = "#C2185B" };
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        await t.Settings.SaveAsync(company);
        await t.Catalog.SaveAsync(new CatalogItem { Label = "Heure de conseil", Unit = "h", UnitPrice = 120m, VatRate = 8.1m });
        await t.Catalog.SaveAsync(new CatalogItem { Label = "Forfait déplacement", Unit = "forfait", UnitPrice = 50m, VatRate = 8.1m });
        var vm = new CatalogViewModel(t.Catalog, t.Settings, new FakeNavigation(), new FakeDialogs());

        await vm.OnAppearingAsync();

        Assert.Equal("2 articles", vm.CountText);
        Assert.Equal("#C2185B", vm.AccentColor);
        var forfait = vm.Items[0];
        Assert.Equal(("FD", "CHF 50.00", "forfait", "TVA 8.1 %"), (forfait.Initials, forfait.PriceDisplay, forfait.UnitDisplay, forfait.VatDisplay));

        vm.SearchText = "conseil";
        Assert.Equal("Heure de conseil", Assert.Single(vm.Items).Label);
        Assert.Equal("1 sur 2 articles", vm.CountText);
    }

    [Fact]
    public async Task Vat_badge_is_hidden_for_vat_exempt_company()
    {
        await using var t = new TestDatabase();
        await t.Settings.SaveAsync(new CompanySettings { VatExempt = true });
        await t.Catalog.SaveAsync(new CatalogItem { Label = "Cours", Unit = "h", UnitPrice = 40m });
        var vm = new CatalogViewModel(t.Catalog, t.Settings, new FakeNavigation(), new FakeDialogs());

        await vm.OnAppearingAsync();

        Assert.False(Assert.Single(vm.Items).HasVat);
    }

    [Fact]
    public async Task Edit_screen_previews_the_card_live()
    {
        await using var t = new TestDatabase();
        var vm = new CatalogItemEditViewModel(t.Catalog, t.Settings, new FakeNavigation(), new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object>());
        await vm.OnAppearingAsync();
        Assert.Equal("Nouvel article", vm.PreviewTitle);

        vm.Label = "Séance de coaching";
        vm.UnitPriceText = "90";
        vm.Unit = "séance";

        Assert.Equal(("SC", "90,00 €", "par séance"), (vm.PreviewInitials, vm.PreviewPrice, vm.PreviewUnit));
    }
}
