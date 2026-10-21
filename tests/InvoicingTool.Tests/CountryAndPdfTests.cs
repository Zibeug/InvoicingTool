using InvoicingTool.Core.Models;
using InvoicingTool.Core.Pdf;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.ViewModels;
using PdfSharp.Pdf.IO;

namespace InvoicingTool.Tests;

public class CountryAndPdfTests
{
    [Theory]
    [InlineData("EUR", "FR", "1 234,50 €")]
    [InlineData("CHF", "CH", "CHF 1'234.50")]
    [InlineData("EUR", "BE", "1 234,50 €")]
    [InlineData("EUR", "LU", "1.234,50 €")]
    [InlineData("USD", "XX", "1 234,50 $")]
    [InlineData("SEK", "FR", "1 234,50 SEK")] // devise inconnue : le code sert de symbole
    public void Money_is_formatted_per_currency_and_country(string currency, string country, string expected) =>
        Assert.Equal(expected, new MoneyFormatter(currency, country).Format(1234.5m));

    [Fact]
    public void Swiss_dates_use_dots() =>
        Assert.Equal("21.10.2026", new MoneyFormatter("CHF", "CH").Date(new DateTime(2026, 10, 21)));

    [Theory]
    [InlineData("8,1 ; 2,6 ; 3,8", new[] { 8.1, 2.6, 3.8 })]
    [InlineData("20;10;5.5;2.1", new[] { 20, 10, 5.5, 2.1 })]
    [InlineData("21 % / 6 %", new[] { 21.0, 6 })]
    [InlineData("abc;150;-1", new double[0])]
    public void Tax_rates_are_parsed_leniently(string text, double[] expected) =>
        Assert.Equal(expected.Select(e => (decimal)e), CompanySettings.ParseRates(text));

    [Fact]
    public void Applying_swiss_profile_sets_currency_rates_and_rounding()
    {
        var settings = new CompanySettings();

        settings.ApplyCountryDefaults(CountryProfiles.Switzerland);

        Assert.Equal("CH", settings.CountryCode);
        Assert.Equal("CHF", settings.CurrencyCode);
        Assert.Equal([8.1m, 3.8m, 2.6m], settings.TaxRates);
        Assert.Equal(8.1m, settings.DefaultVatRate);
        Assert.Equal(0.05m, settings.RoundingIncrement);
        Assert.Equal("Non assujetti à la TVA.", settings.VatExemptionMention);
    }

    private static async Task<(CommercialDocument Doc, CompanySettings Company)> IssueSwissInvoiceAsync(TestDatabase t)
    {
        var company = new CompanySettings { CompanyName = "Peinture Favre Sàrl", Siret = "CHE-123.456.789", VatNumber = "CHE-123.456.789 TVA" };
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        await t.Settings.SaveAsync(company);

        var client = new Client { Name = "Mme Rochat", Address = "Rue du Lac 4", PostalCode = "1003", City = "Lausanne" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice,
        [
            new() { Description = "Peinture façade", Quantity = 42, Unit = "m²", UnitPrice = 38.5m, VatRate = 8.1m },
            new() { Description = "Déplacement", Quantity = 1, Unit = "forfait", UnitPrice = 60m, VatRate = 8.1m },
        ]);
        await t.Service.IssueAsync(invoice);
        return (invoice, company);
    }

    [Fact]
    public async Task Swiss_invoice_uses_chf_rounding_and_swiss_labels()
    {
        await using var t = new TestDatabase();
        var (invoice, company) = await IssueSwissInvoiceAsync(t);

        // 42 × 38,50 + 60 = 1 677,00 HT ; TVA 8,1 % = 135,84 ; 1 812,84 → arrondi 1 812,85
        Assert.Equal("CHF", invoice.CurrencyCode);
        Assert.Equal(1812.85m, invoice.TotalInclTax);

        var printable = PrintableDocument.Build(invoice, await t.Service.GetLinesAsync(invoice.Id), company);
        Assert.Contains("IDE : CHE-123.456.789", printable.CompanyLines);
        Assert.Contains(printable.Totals, r => r.Label == "Arrondi" && r.Value == "CHF 0.01");
        Assert.Contains(printable.Totals, r => r.IsGrandTotal && r.Value == "CHF 1'812.85");
        Assert.Contains(printable.Totals, r => r.Label.StartsWith("TVA 8.1 %"));
    }

    [Fact]
    public async Task Issued_document_keeps_its_currency_when_settings_change()
    {
        await using var t = new TestDatabase();
        var (invoice, company) = await IssueSwissInvoiceAsync(t);

        company.ApplyCountryDefaults(CountryProfiles.France);
        await t.Settings.SaveAsync(company);

        var reloaded = (await t.Documents.GetAsync(invoice.Id))!;
        Assert.Equal("CHF", reloaded.CurrencyCode);
        Assert.Equal("CHF 1'812.85", reloaded.TotalInclTaxDisplay);
    }

    [Fact]
    public async Task Vat_exempt_document_prints_country_mention_without_vat_rows()
    {
        await using var t = new TestDatabase();
        var company = new CompanySettings { CompanyName = "Déco Martin", VatExempt = true };
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "M. Petit" };
        await t.Clients.SaveAsync(client);
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await t.Service.SaveDraftAsync(quote, [new() { Description = "Papier peint", Quantity = 10, UnitPrice = 30m, VatRate = 20m }]);

        var printable = PrintableDocument.Build(quote, await t.Service.GetLinesAsync(quote.Id), company);

        Assert.False(printable.ShowTax);
        Assert.Equal("TVA non applicable, art. 293 B du CGI.", printable.TaxExemptionMention);
        Assert.Equal(300m, quote.TotalInclTax);
        Assert.DoesNotContain(printable.Totals, r => r.Label.StartsWith("TVA"));
    }

    [Fact]
    public async Task Pdf_is_a_valid_single_page_document()
    {
        await using var t = new TestDatabase();
        var (invoice, company) = await IssueSwissInvoiceAsync(t);

        var pdf = PdfDocumentRenderer.Render(invoice, await t.Service.GetLinesAsync(invoice.Id), company);

        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
        using var stream = new MemoryStream(pdf);
        using var parsed = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(1, parsed.PageCount);
        Assert.Equal("FACTURE n° F-2026-0001", parsed.Info.Title);
    }

    [Fact]
    public async Task Long_quote_flows_onto_several_pdf_pages()
    {
        await using var t = new TestDatabase();
        var client = new Client { Name = "Syndic Les Tilleuls" };
        await t.Clients.SaveAsync(client);
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        var lines = Enumerable.Range(1, 80)
            .Select(i => new DocumentLine { Description = $"Appartement {i} : peinture murs et plafonds", Quantity = 45, Unit = "m²", UnitPrice = 24m, VatRate = 10m })
            .ToList();
        await t.Service.SaveDraftAsync(quote, lines);

        var pdf = PdfDocumentRenderer.Render(quote, await t.Service.GetLinesAsync(quote.Id), await t.Settings.GetSettingsAsync());

        using var parsed = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.True(parsed.PageCount > 1);
    }

    [Fact]
    public async Task Choosing_switzerland_in_settings_proposes_swiss_defaults()
    {
        await using var t = new TestDatabase();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var vm = new SettingsViewModel(t.Settings, dialogs);
        await vm.OnAppearingAsync();

        await vm.ProposeCountryDefaultsAsync(CountryProfiles.Switzerland);

        Assert.Equal("CHF", vm.SelectedCurrency.Code);
        Assert.Equal("8,1 ; 3,8 ; 2,6", vm.TaxRatesText);
        Assert.True(vm.RoundToFiveCents);
    }

    [Fact]
    public async Task Draft_editor_follows_current_currency()
    {
        await using var t = new TestDatabase();
        var company = new CompanySettings();
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "M. Muller" };
        await t.Clients.SaveAsync(client);
        var doc = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        var vm = new DocumentEditViewModel(t.Documents, t.Service, t.Settings, t.Catalog, new FakeNavigation(), new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object> { [Presentation.Routes.IdParameter] = doc.Id });
        await vm.OnAppearingAsync();

        vm.AddLineCommand.Execute(null);
        vm.Lines[0].UnitPriceText = "100.03";

        Assert.Equal("8,1", vm.Lines[0].VatRateText);
        Assert.Equal("CHF 108.15", vm.TotalInclTaxDisplay); // 108,13 arrondi à 0,05
        Assert.True(vm.HasRounding);
    }
}
