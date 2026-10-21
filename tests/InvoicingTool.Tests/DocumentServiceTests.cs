using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;

namespace InvoicingTool.Tests;

public class DocumentServiceTests
{
    private static async Task<Client> AddClientAsync(TestDatabase t)
    {
        var client = new Client { Name = "Mme Leroy", Address = "3 rue des Lilas", PostalCode = "69003", City = "Lyon" };
        await t.Clients.SaveAsync(client);
        return client;
    }

    private static List<DocumentLine> SampleLines() =>
    [
        new() { Description = "Peinture murs séjour", Quantity = 35, Unit = "m²", UnitPrice = 24m, VatRate = 10m },
        new() { Description = "Fournitures", Quantity = 1, Unit = "forfait", UnitPrice = 150m, VatRate = 20m },
    ];

    [Fact]
    public async Task Draft_copies_client_and_defaults_due_date_from_settings()
    {
        await using var t = new TestDatabase();
        var client = await AddClientAsync(t);

        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);

        Assert.Equal(DocumentStatus.Draft, quote.Status);
        Assert.Null(quote.Number);
        Assert.Equal("Mme Leroy", quote.ClientName);
        Assert.Equal("3 rue des Lilas, 69003 Lyon", quote.ClientAddress);
        Assert.Equal(new DateTime(2026, 3, 15), quote.IssueDate);
        Assert.Equal(new DateTime(2026, 4, 14), quote.DueDate);
    }

    [Fact]
    public async Task Save_draft_stores_lines_in_order_and_totals()
    {
        await using var t = new TestDatabase();
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, await AddClientAsync(t));

        await t.Service.SaveDraftAsync(quote, SampleLines());

        var reloaded = await t.Documents.GetAsync(quote.Id);
        Assert.Equal(990m, reloaded!.TotalExclTax);   // 840 + 150
        Assert.Equal(114m, reloaded.TotalVat);        // 84 + 30
        Assert.Equal(1104m, reloaded.TotalInclTax);
        var lines = await t.Service.GetLinesAsync(quote.Id);
        Assert.Equal(["Peinture murs séjour", "Fournitures"], lines.Select(l => l.Description));
    }

    [Fact]
    public async Task Removed_lines_are_soft_deleted()
    {
        await using var t = new TestDatabase();
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, await AddClientAsync(t));
        var lines = SampleLines();
        await t.Service.SaveDraftAsync(quote, lines);

        await t.Service.SaveDraftAsync(quote, [lines[1]]);

        Assert.Equal("Fournitures", Assert.Single(await t.Service.GetLinesAsync(quote.Id)).Description);
        Assert.Equal(150m, (await t.Documents.GetAsync(quote.Id))!.TotalExclTax);
    }

    [Fact]
    public async Task Issuing_assigns_continuous_numbers_per_type_and_year()
    {
        await using var t = new TestDatabase();
        var client = await AddClientAsync(t);

        async Task<CommercialDocument> IssueNew(DocumentType type)
        {
            var doc = await t.Service.CreateDraftAsync(type, client);
            await t.Service.SaveDraftAsync(doc, SampleLines());
            await t.Service.IssueAsync(doc);
            return doc;
        }

        var q1 = await IssueNew(DocumentType.Quote);
        var q2 = await IssueNew(DocumentType.Quote);
        var f1 = await IssueNew(DocumentType.Invoice);

        Assert.Equal("D-2026-0001", q1.Number);
        Assert.Equal("D-2026-0002", q2.Number);
        Assert.Equal("F-2026-0001", f1.Number);
        Assert.Equal(DocumentStatus.Issued, f1.Status);

        t.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero));
        var f2027 = await IssueNew(DocumentType.Invoice);
        Assert.Equal("F-2027-0001", f2027.Number);
    }

    [Fact]
    public async Task Deleted_drafts_do_not_consume_numbers()
    {
        await using var t = new TestDatabase();
        var client = await AddClientAsync(t);
        var abandoned = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(abandoned, SampleLines());
        await t.Service.DeleteDraftAsync(abandoned);

        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await t.Service.SaveDraftAsync(invoice, SampleLines());
        await t.Service.IssueAsync(invoice);

        Assert.Equal("F-2026-0001", invoice.Number);
    }

    [Fact]
    public async Task Issued_document_can_no_longer_be_edited_or_deleted()
    {
        await using var t = new TestDatabase();
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, await AddClientAsync(t));
        await t.Service.SaveDraftAsync(invoice, SampleLines());
        await t.Service.IssueAsync(invoice);

        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.SaveDraftAsync(invoice, SampleLines()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.DeleteDraftAsync(invoice));
    }

    [Fact]
    public async Task Cannot_issue_empty_document()
    {
        await using var t = new TestDatabase();
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, await AddClientAsync(t));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.IssueAsync(quote));
        Assert.Null(quote.Number);
    }

    [Fact]
    public async Task Quote_converts_to_draft_invoice_with_copied_lines()
    {
        await using var t = new TestDatabase();
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, await AddClientAsync(t));
        quote.Title = "Rénovation séjour";
        await t.Service.SaveDraftAsync(quote, SampleLines());
        await t.Service.IssueAsync(quote);

        var invoice = await t.Service.CreateInvoiceFromQuoteAsync(quote);

        Assert.Equal(DocumentType.Invoice, invoice.Type);
        Assert.Equal(DocumentStatus.Draft, invoice.Status);
        Assert.Equal(quote.Id, invoice.SourceDocumentId);
        Assert.Equal("Rénovation séjour", invoice.Title);
        Assert.Equal("Selon devis n° D-2026-0001", invoice.Notes);
        Assert.Equal(1104m, invoice.TotalInclTax);
        Assert.Equal(2, (await t.Service.GetLinesAsync(invoice.Id)).Count);
        Assert.Equal(2, (await t.Service.GetLinesAsync(quote.Id)).Count);
        Assert.Equal(DocumentStatus.Accepted, (await t.Documents.GetAsync(quote.Id))!.Status);
    }

    [Fact]
    public async Task Draft_quote_cannot_be_invoiced()
    {
        await using var t = new TestDatabase();
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, await AddClientAsync(t));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.CreateInvoiceFromQuoteAsync(quote));
    }

    [Fact]
    public async Task Paying_an_invoice_records_payment_date()
    {
        await using var t = new TestDatabase();
        var invoice = await t.Service.CreateDraftAsync(DocumentType.Invoice, await AddClientAsync(t));
        await t.Service.SaveDraftAsync(invoice, SampleLines());
        await t.Service.IssueAsync(invoice);

        await t.Service.SetStatusAsync(invoice, DocumentStatus.Paid);

        var reloaded = await t.Documents.GetAsync(invoice.Id);
        Assert.Equal(DocumentStatus.Paid, reloaded!.Status);
        Assert.Equal(new DateTime(2026, 3, 15), reloaded.PaidDate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Service.SetStatusAsync(invoice, DocumentStatus.Accepted));
    }

    [Fact]
    public async Task Html_rendering_contains_number_totals_and_escapes_text()
    {
        await using var t = new TestDatabase();
        var client = await AddClientAsync(t);
        client.Name = "<script>Dupont</script>";
        var quote = await t.Service.CreateDraftAsync(DocumentType.Quote, client);
        await t.Service.SaveDraftAsync(quote, SampleLines());
        await t.Service.IssueAsync(quote);
        var company = new CompanySettings { CompanyName = "Peinture Durand", Siret = "123 456 789 00012" };

        var html = HtmlDocumentRenderer.Render(quote, await t.Service.GetLinesAsync(quote.Id), company);

        Assert.Contains("DEVIS n&#176; D-2026-0001", html);
        Assert.Contains("1&#160;104,00&#160;€", html); // espaces insécables entre milliers et devant la devise
        Assert.Contains("&lt;script&gt;Dupont&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Bon pour accord", html);
    }
}
