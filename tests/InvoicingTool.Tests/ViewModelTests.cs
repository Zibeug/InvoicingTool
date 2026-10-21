using InvoicingTool.Core.Models;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.Tests;

public class ViewModelTests
{
    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData(" 1 250,40 € ", 1250.40)]
    [InlineData("5,5 %", 5.5)]
    [InlineData("", 0)]
    public void Decimal_parser_accepts_french_and_invariant_input(string text, decimal expected)
    {
        Assert.True(DecimalParser.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Decimal_parser_rejects_garbage() => Assert.False(DecimalParser.TryParse("abc", out _));

    private static async Task<(DocumentEditViewModel Vm, CommercialDocument Doc, FakeNavigation Nav, FakeDialogs Dialogs)>
        OpenNewQuoteAsync(TestDatabase t)
    {
        var client = new Client { Name = "M. Bernard" };
        await t.Clients.SaveAsync(client);
        var doc = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        var nav = new FakeNavigation();
        var dialogs = new FakeDialogs();
        var vm = new DocumentEditViewModel(t.Documents, t.Service, t.Settings, t.Catalog, nav, dialogs);
        vm.ApplyParameters(new Dictionary<string, object> { [Routes.IdParameter] = doc.Id });
        await vm.OnAppearingAsync();
        return (vm, doc, nav, dialogs);
    }

    [Fact]
    public async Task Editing_lines_updates_totals_live_and_autosaves_on_leave()
    {
        await using var t = new TestDatabase();
        var (vm, doc, _, _) = await OpenNewQuoteAsync(t);

        vm.AddLineCommand.Execute(null);
        var line = vm.Lines.Single();
        line.Description = "Peinture plafond";
        line.QuantityText = "20,5";
        line.UnitPriceText = "18";

        Assert.Equal("369,00 €", line.TotalDisplay);
        Assert.Equal("369,00 €", vm.TotalExclTaxDisplay);
        Assert.Equal("405,90 €", vm.TotalInclTaxDisplay); // TVA 10 % par défaut

        await vm.OnDisappearingAsync();

        var saved = await t.Documents.GetAsync(doc.Id);
        Assert.Equal(405.90m, saved!.TotalInclTax);
        Assert.Equal(20.5m, Assert.Single(await t.Service.GetLinesAsync(doc.Id)).Quantity);
    }

    [Fact]
    public async Task Invalid_number_blocks_save_with_message()
    {
        await using var t = new TestDatabase();
        var (vm, doc, nav, dialogs) = await OpenNewQuoteAsync(t);
        vm.AddLineCommand.Execute(null);
        vm.Lines[0].Description = "Ponçage";
        vm.Lines[0].UnitPriceText = "douze";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Alerts);
        Assert.Equal(0, nav.BackCount);
        Assert.Empty(await t.Service.GetLinesAsync(doc.Id));
    }

    [Fact]
    public async Task Adding_from_catalog_copies_price_unit_and_vat()
    {
        await using var t = new TestDatabase();
        await t.Catalog.SaveAsync(new CatalogItem { Label = "Lessivage", Unit = "m²", UnitPrice = 4.5m, VatRate = 20m });
        var (vm, _, _, _) = await OpenNewQuoteAsync(t);

        await vm.AddFromCatalogCommand.ExecuteAsync(null);

        var line = Assert.Single(vm.Lines);
        Assert.Equal("Lessivage", line.Description);
        Assert.Equal("m²", line.Unit);
        Assert.Equal(4.5m, line.Line.UnitPrice);
        Assert.Equal(20m, line.Line.VatRate);
    }

    [Fact]
    public async Task Issue_locks_document_and_shows_number()
    {
        await using var t = new TestDatabase();
        var (vm, _, _, _) = await OpenNewQuoteAsync(t);
        vm.AddLineCommand.Execute(null);
        vm.Lines[0].Description = "Peinture façade";
        vm.Lines[0].UnitPriceText = "1500";

        await vm.IssueCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditable);
        Assert.Equal("D-2026-0001", vm.Number);
        Assert.Equal("Envoyé", vm.StatusLabel);
        Assert.True(vm.CanConvertToInvoice);
    }

    [Fact]
    public async Task Convert_to_invoice_navigates_to_new_invoice()
    {
        await using var t = new TestDatabase();
        var (vm, _, nav, _) = await OpenNewQuoteAsync(t);
        vm.AddLineCommand.Execute(null);
        vm.Lines[0].Description = "Peinture façade";
        vm.Lines[0].UnitPriceText = "1500";
        await vm.IssueCommand.ExecuteAsync(null);

        await vm.ConvertToInvoiceCommand.ExecuteAsync(null);

        var (route, parameters) = nav.History.Last();
        Assert.Equal($"../{Routes.DocumentEdit}", route);
        var invoice = await t.Documents.GetAsync((Guid)parameters![Routes.IdParameter]);
        Assert.Equal(DocumentType.Invoice, invoice!.Type);
    }

    [Fact]
    public async Task Deleted_draft_is_not_recreated_on_leave()
    {
        await using var t = new TestDatabase();
        var (vm, doc, nav, _) = await OpenNewQuoteAsync(t);

        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.OnDisappearingAsync();

        Assert.Equal(1, nav.BackCount);
        Assert.Null(await t.Documents.GetAsync(doc.Id));
    }
}
