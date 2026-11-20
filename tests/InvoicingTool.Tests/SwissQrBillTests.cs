using Codecrete.SwissQRBill.Generator;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Pdf;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.ViewModels;
using PdfSharp.Pdf.IO;

namespace InvoicingTool.Tests;

public class SwissQrBillTests
{
    private static CompanySettings SwissCompany()
    {
        var company = new CompanySettings
        {
            CompanyName = "Léa Morel Graphisme", Address = "Rue de Lausanne 12", PostalCode = "1201", City = "Genève",
            Iban = "CH93 0076 2011 6238 5295 7",
        };
        company.ApplyCountryDefaults(CountryProfiles.Switzerland);
        return company;
    }

    private static async Task<CommercialDocument> IssueInvoiceAsync(TestDatabase t, CompanySettings company, Client? client = null)
    {
        await t.Settings.SaveAsync(company);
        client ??= new Client { Name = "Café du Marché Sàrl", Address = "Place du Molard 3", PostalCode = "1204", City = "Genève", CountryCode = "CH" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "Création du logo", Quantity = 1, Unit = "forfait", UnitPrice = 950m, VatRate = 8.1m }]);
        await t.Service.IssueAsync(invoice);
        return invoice;
    }

    [Theory]
    [InlineData("Rue du Lac 4", "Rue du Lac", "4")]
    [InlineData("Chemin des Vignes 12b", "Chemin des Vignes", "12b")]
    [InlineData("12bis avenue de la Gare", "avenue de la Gare", "12bis")]
    [InlineData("4, rue Neuve", "rue Neuve", "4")]
    [InlineData("Place du Marché", "Place du Marché", null)]
    [InlineData("Route 66 Nord", "Route 66 Nord", null)]
    public void Street_and_house_number_are_separated(string line, string street, string? number) =>
        Assert.Equal((street, number), SwissQrBill.SplitStreet(line));

    [Theory]
    [InlineData("F-2026-0012", "F20260012")]
    [InlineData("FACT/2026/12", "FACT202612")]
    public void Reference_source_keeps_letters_and_digits(string number, string expected) =>
        Assert.Equal(expected, SwissQrBill.CreditorReferenceSource(number));

    [Fact]
    public async Task Invoice_in_chf_gets_a_valid_qr_bill_with_rf_reference()
    {
        await using var t = new TestDatabase();
        var company = SwissCompany();
        var invoice = await IssueInvoiceAsync(t, company);

        var bill = SwissQrBill.Create(invoice, company);

        Assert.NotNull(bill);
        Assert.True(QRBill.Validate(bill).IsValid);
        var lines = SwissQrBill.QrCodeText(bill).Replace("\r\n", "\n").Split('\n');
        Assert.Equal(["SPC", "0200", "1", "CH9300762011623852957", "S", "Léa Morel Graphisme", "Rue de Lausanne", "12", "1201", "Genève", "CH"], lines[..11]);
        Assert.Equal(["1026.95", "CHF"], lines[18..20]); // 950 + TVA 8,1 % = 1 026,95
        Assert.Equal(["S", "Café du Marché Sàrl", "Place du Molard", "3", "1204", "Genève", "CH"], lines[20..27]);
        Assert.Equal("SCOR", lines[27]);
        Assert.True(Payments.IsValidIso11649Reference(lines[28]));
        Assert.EndsWith("F20260001", lines[28]);
        Assert.Equal("Facture F-2026-0001", lines[29]);
    }

    [Fact]
    public async Task Incomplete_client_address_leaves_the_payer_box_empty()
    {
        await using var t = new TestDatabase();
        var company = SwissCompany();
        var invoice = await IssueInvoiceAsync(t, company, new Client { Name = "M. Dupont" });

        var bill = SwissQrBill.Create(invoice, company)!;

        Assert.Null(bill.Debtor);
    }

    [Fact]
    public async Task Qr_bill_only_applies_where_it_should()
    {
        await using var t = new TestDatabase();
        var company = SwissCompany();
        var invoice = await IssueInvoiceAsync(t, company);
        Assert.NotNull(SwissQrBill.Create(invoice, company));

        company.QrBillEnabled = false;
        Assert.Null(SwissQrBill.Create(invoice, company)); // désactivée
        company.QrBillEnabled = true;

        var eur = new CommercialDocument { Type = DocumentType.Invoice, Status = DocumentStatus.Issued, CurrencyCode = "EUR", TotalInclTax = 10m, ClientName = "X" };
        Assert.Null(SwissQrBill.Create(eur, company)); // autre devise

        var quote = new CommercialDocument { Type = DocumentType.Quote, Status = DocumentStatus.Issued, CurrencyCode = "CHF", TotalInclTax = 10m, ClientName = "X" };
        Assert.Null(SwissQrBill.Create(quote, company)); // devis

        await t.Service.SetStatusAsync(invoice, DocumentStatus.Paid);
        Assert.Null(SwissQrBill.Create(invoice, company)); // déjà payée
    }

    [Theory]
    [InlineData("CH44 3199 9123 0008 8901 2", "QR-IBAN")]
    [InlineData("FR76 3000 6000 0112 3456 7890 189", "suisse (CH)")]
    [InlineData("", "IBAN")]
    public void Company_iban_must_be_a_normal_swiss_iban(string iban, string expected)
    {
        var company = SwissCompany();
        company.Iban = iban;
        Assert.Contains(SwissQrBill.CompanyIssues(company), i => i.Contains(expected));
    }

    [Fact]
    public void Complete_swiss_company_has_no_issue() => Assert.Empty(SwissQrBill.CompanyIssues(SwissCompany()));

    [Fact]
    public async Task Pdf_and_preview_contain_the_payment_part()
    {
        await using var t = new TestDatabase();
        var company = SwissCompany();
        var invoice = await IssueInvoiceAsync(t, company);
        var lines = await t.Service.GetLinesAsync(invoice.Id);

        var printable = PrintableDocument.Build(invoice, lines, company);
        Assert.NotNull(printable.QrBill);
        Assert.Contains("data:image/svg+xml;base64,", HtmlDocumentRenderer.Render(printable));

        var pdf = PdfDocumentRenderer.Render(printable);
        using var parsed = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(1, parsed.PageCount); // la section paiement tient en bas de la page
    }

    [Fact]
    public async Task Payment_part_moves_to_a_new_page_only_when_the_last_page_is_full()
    {
        await using var t = new TestDatabase();
        var company = SwissCompany();
        await t.Settings.SaveAsync(company);
        var client = new Client { Name = "Studio Lumen", PostalCode = "1003", City = "Lausanne" };
        await t.Clients.SaveAsync(client);

        var extraPages = new List<int>();
        for (var count = 1; count <= 30; count++)
        {
            var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
            await t.Service.SaveDraftAsync(invoice, Enumerable.Range(1, count)
                .Select(i => new DocumentLine { Description = $"Séance {i}", Quantity = 1, UnitPrice = 40m, VatRate = 8.1m }).ToList());
            await t.Service.IssueAsync(invoice);
            var lines = await t.Service.GetLinesAsync(invoice.Id);

            company.QrBillEnabled = false;
            var withoutQr = PageCount(PdfDocumentRenderer.Render(invoice, lines, company));
            company.QrBillEnabled = true;
            var withQr = PageCount(PdfDocumentRenderer.Render(invoice, lines, company));
            extraPages.Add(withQr - withoutQr);
        }

        Assert.All(extraPages, extra => Assert.InRange(extra, 0, 1));
        Assert.Contains(0, extraPages); // place libre : en bas de la dernière page
        Assert.Contains(1, extraPages); // page pleine : page supplémentaire
    }

    private static int PageCount(byte[] pdf) =>
        PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import).PageCount;

    [Fact]
    public async Task Invoice_from_quote_keeps_the_structured_client_address()
    {
        await using var t = new TestDatabase();
        await t.Settings.SaveAsync(SwissCompany());
        var client = new Client { Name = "Studio Lumen", Address = "Rue Centrale 8", PostalCode = "1003", City = "Lausanne", CountryCode = "CH" };
        await t.Clients.SaveAsync(client);
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await t.Service.SaveDraftAsync(quote, [new() { Description = "A", Quantity = 1, UnitPrice = 10m }]);
        await t.Service.IssueAsync(quote);

        var invoice = await t.Service.CreateInvoiceFromQuoteAsync(quote);

        Assert.Equal(("Rue Centrale 8", "1003", "Lausanne", "CH"),
            (invoice.ClientStreet, invoice.ClientPostalCode, invoice.ClientCity, invoice.ClientCountryCode));
    }

    [Fact]
    public async Task Settings_show_live_qr_bill_status()
    {
        await using var t = new TestDatabase();
        var company = SwissCompany();
        company.Iban = null;
        await t.Settings.SaveAsync(company);
        var vm = t.CreateSettingsViewModel();
        await vm.OnAppearingAsync();

        Assert.True(vm.IsSwitzerland);
        Assert.False(vm.IsQrBillReady);
        Assert.Contains("Indiquez votre IBAN", vm.QrBillStatus);

        vm.Iban = "CH93 0076 2011 6238 5295 7";
        Assert.True(vm.IsQrBillReady);
        Assert.StartsWith("Prête", vm.QrBillStatus);

        vm.QrBillEnabled = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False((await t.Settings.GetSettingsAsync()).QrBillEnabled);
    }

    [Fact]
    public async Task Client_country_defaults_to_company_country_and_is_saved()
    {
        await using var t = new TestDatabase();
        await t.Settings.SaveAsync(SwissCompany());
        var vm = new ClientEditViewModel(t.Clients, t.Documents, t.Service, new FakeNavigation(), t.Settings, new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object>());
        await vm.OnAppearingAsync();
        Assert.Equal("CH", vm.SelectedCountry.Code);

        vm.Name = "Bäckerei Huber";
        vm.SelectedCountry = CountryOption.Get("DE");
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("DE", Assert.Single(await t.Clients.GetAllAsync()).CountryCode);
    }
}
