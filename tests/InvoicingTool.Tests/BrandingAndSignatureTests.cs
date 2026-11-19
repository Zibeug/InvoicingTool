using InvoicingTool.Core.Models;
using InvoicingTool.Core.Pdf;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.ViewModels;
using PdfSharp.Pdf.IO;

namespace InvoicingTool.Tests;

public class BrandingAndSignatureTests
{
    [Theory]
    [InlineData("CH93 0076 2011 6238 5295 7", true)]
    [InlineData("ch9300762011623852957", true)]
    [InlineData("FR76 3000 6000 0112 3456 7890 189", true)]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("CH93 0076 2011 6238 5295 8", false)] // une faute de frappe
    [InlineData("CH93", false)]
    [InlineData("", false)]
    public void Iban_checksum_is_verified(string iban, bool expected) => Assert.Equal(expected, Iban.IsValid(iban));

    [Fact]
    public void Iban_is_formatted_by_groups_of_four() =>
        Assert.Equal("CH93 0076 2011 6238 5295 7", Iban.Format("ch93-0076201162385 2957"));

    private static async Task<CommercialDocument> IssueQuoteAsync(TestDatabase t, CompanySettings? company = null)
    {
        await t.Settings.SaveAsync(company ?? new CompanySettings { CompanyName = "Atelier Nova" });
        var client = new Client { Name = "Studio Lumen" };
        await t.Clients.SaveAsync(client);
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await t.Service.SaveDraftAsync(quote, [new() { Description = "Atelier", Quantity = 2, Unit = "h", UnitPrice = 90m, VatRate = 20m }]);
        await t.Service.IssueAsync(quote);
        return quote;
    }

    [Fact]
    public async Task Signing_a_quote_accepts_it_and_prints_the_signature()
    {
        await using var t = new TestDatabase();
        var company = new CompanySettings { CompanyName = "Atelier Nova", Logo = TestImages.Logo(), AccentColor = "#C2185B" };
        var quote = await IssueQuoteAsync(t, company);

        await t.Service.SignQuoteAsync(quote, TestImages.Signature(), "Léa Martin");

        Assert.Equal(DocumentStatus.Accepted, (await t.Documents.GetAsync(quote.Id))!.Status);
        var signature = (await t.Documents.GetSignatureAsync(quote.Id))!;
        Assert.Equal("Léa Martin", signature.SignerName);

        var printable = PrintableDocument.Build(quote, await t.Service.GetLinesAsync(quote.Id), company, signature);
        Assert.Equal("Signé le 15/03/2026 à 10:00 par Léa Martin", printable.Signature!.Caption);

        var html = HtmlDocumentRenderer.Render(printable);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "data:image/png;base64,").Count); // logo + signature
        Assert.Contains("#C2185B", html);
        Assert.DoesNotContain("#2b5797", html);

        var pdf = PdfDocumentRenderer.Render(printable);
        using var parsed = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(1, parsed.PageCount);
        Assert.True(pdf.Length > 5000);
    }

    [Fact]
    public async Task Only_issued_unsigned_quotes_can_be_signed()
    {
        await using var t = new TestDatabase();
        var client = new Client { Name = "X" };
        await t.Clients.SaveAsync(client);
        var draft = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.SignQuoteAsync(draft, TestImages.Signature(), "X"));

        var quote = await IssueQuoteAsync(t);
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.SignQuoteAsync(quote, [], "X"));
        await t.Service.SignQuoteAsync(quote, TestImages.Signature(), "X");
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.SignQuoteAsync(quote, TestImages.Signature(), "X"));
    }

    [Fact]
    public async Task Signature_screen_requires_a_drawing_and_returns_after_signing()
    {
        await using var t = new TestDatabase();
        var quote = await IssueQuoteAsync(t);
        var nav = new FakeNavigation();
        var dialogs = new FakeDialogs();
        var vm = new SignatureViewModel(t.Documents, t.Service, nav, dialogs);
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = quote.Id });
        await vm.OnAppearingAsync();
        Assert.Equal("Studio Lumen", vm.SignerName);
        Assert.Contains("216,00", vm.Summary);

        await vm.SaveAsync(null);
        Assert.Single(dialogs.Alerts);
        Assert.Equal(0, nav.BackCount);

        await vm.SaveAsync(TestImages.Signature());
        Assert.Equal(1, nav.BackCount);
        Assert.NotNull(await t.Documents.GetSignatureAsync(quote.Id));
    }

    [Fact]
    public async Task Editor_offers_signature_until_the_quote_is_signed()
    {
        await using var t = new TestDatabase();
        var quote = await IssueQuoteAsync(t);
        var vm = new DocumentEditViewModel(t.Documents, t.Service, t.Settings, t.Catalog, new FakeNavigation(), new FakeDialogs());
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = quote.Id });

        await vm.OnAppearingAsync();
        Assert.True(vm.CanSign);
        Assert.False(vm.HasSignature);

        await t.Service.SignQuoteAsync(quote, TestImages.Signature(), "Paul");
        await vm.OnAppearingAsync();
        Assert.False(vm.CanSign);
        Assert.Equal("Signé par Paul le 15/03/2026 à 10:00", vm.SignatureLabel);
        Assert.Equal("Accepté", vm.StatusLabel);
    }

    [Fact]
    public async Task Numbering_can_start_from_a_chosen_number()
    {
        await using var t = new TestDatabase();
        await t.Service.SetNextNumberAsync(DocumentType.Invoice, 2026, 42);
        var client = new Client { Name = "X" };
        await t.Clients.SaveAsync(client);

        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "A", Quantity = 1, UnitPrice = 10m }]);
        await t.Service.IssueAsync(invoice);

        Assert.Equal("F-2026-0042", invoice.Number);
        Assert.Equal(43, await t.Service.GetNextNumberAsync(DocumentType.Invoice, 2026));
        Assert.Equal(1, await t.Service.GetNextNumberAsync(DocumentType.Quote, 2026));
    }

    [Fact]
    public async Task Numbering_cannot_go_back_to_an_already_used_number()
    {
        await using var t = new TestDatabase();
        await t.Service.SetNextNumberAsync(DocumentType.Invoice, 2026, 10);
        var client = new Client { Name = "X" };
        await t.Clients.SaveAsync(client);
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, [new() { Description = "A", Quantity = 1, UnitPrice = 10m }]);
        await t.Service.IssueAsync(invoice); // F-2026-0010

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.SetNextNumberAsync(DocumentType.Invoice, 2026, 10));
        Assert.Contains("au moins 11", error.Message);
        await t.Service.SetNextNumberAsync(DocumentType.Invoice, 2026, 100); // plus haut : autorisé
        Assert.Equal(100, await t.Service.GetNextNumberAsync(DocumentType.Invoice, 2026));
    }

    [Fact]
    public async Task Settings_save_logo_color_iban_and_starting_number()
    {
        await using var t = new TestDatabase();
        var images = new FakeImagePicker { Image = TestImages.Logo() };
        var vm = t.CreateSettingsViewModel(imagePicker: images);
        await vm.OnAppearingAsync();
        Assert.Equal("Prochain numéro : F-2026-0001", vm.NextInvoicePreview);

        await vm.PickLogoCommand.ExecuteAsync(null);
        vm.SelectColorCommand.Execute("#2E7D32");
        vm.Iban = "ch93 0076 2011 6238 5295 7";
        vm.NextInvoiceNumberText = "250";
        Assert.Equal("Prochain numéro : F-2026-0250", vm.NextInvoicePreview);
        await vm.SaveCommand.ExecuteAsync(null);

        var settings = await t.Settings.GetSettingsAsync();
        Assert.True(vm.HasLogo);
        Assert.Equal(TestImages.Logo(), settings.Logo);
        Assert.Equal("#2E7D32", settings.AccentColor);
        Assert.Equal("CH9300762011623852957", settings.Iban);
        Assert.Equal("CH93 0076 2011 6238 5295 7", vm.Iban);
        Assert.Equal(250, await t.Service.GetNextNumberAsync(DocumentType.Invoice, 2026));

        vm.RemoveLogoCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null((await t.Settings.GetSettingsAsync()).Logo);
    }

    [Theory]
    [InlineData("CH93 0076 2011 6238 5295 8", "IBAN")]
    [InlineData(null, "couleur")]
    public async Task Settings_reject_invalid_iban_or_color(string? iban, string expectedWord)
    {
        await using var t = new TestDatabase();
        var dialogs = new FakeDialogs();
        var vm = t.CreateSettingsViewModel(dialogs);
        await vm.OnAppearingAsync();
        vm.CompanyName = "Ne doit pas être enregistré";
        vm.Iban = iban;
        if (iban is null)
            vm.AccentColor = "bleu";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains(expectedWord, Assert.Single(dialogs.Alerts));
        Assert.Equal(string.Empty, (await t.Settings.GetSettingsAsync()).CompanyName);
    }

    [Fact]
    public async Task Signatures_and_logo_are_part_of_the_backup()
    {
        await using var source = new TestDatabase();
        var quote = await IssueQuoteAsync(source, new CompanySettings { CompanyName = "Atelier Nova", Logo = TestImages.Logo() });
        await source.Service.SignQuoteAsync(quote, TestImages.Signature(), "Paul");
        var file = await source.Backup.ExportAsync();

        await using var target = new TestDatabase();
        await target.Backup.RestoreAsync(BackupService.Read(new MemoryStream(file)));

        Assert.Equal(TestImages.Signature(), (await target.Documents.GetSignatureAsync(quote.Id))!.Image);
        Assert.Equal(TestImages.Logo(), (await target.Settings.GetSettingsAsync()).Logo);
    }

    [Fact]
    public async Task Invalid_accent_color_falls_back_to_default_on_documents()
    {
        await using var t = new TestDatabase();
        var quote = await IssueQuoteAsync(t);
        var company = new CompanySettings { AccentColor = "red;} body{display:none" };

        var printable = PrintableDocument.Build(quote, [], company);

        Assert.Equal(CompanySettings.DefaultAccentColor, printable.AccentColor);
    }
}
