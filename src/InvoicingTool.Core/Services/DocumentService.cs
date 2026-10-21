using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using SQLite;

namespace InvoicingTool.Core.Services;

/// <summary>
/// Règles métier des devis et factures : création, enregistrement, émission (numérotation),
/// changements de statut et transformation d'un devis en facture.
/// </summary>
public class DocumentService(
    AppDatabase database,
    DocumentRepository documents,
    SettingsRepository settings,
    TimeProvider clock)
{
    public Task<List<DocumentLine>> GetLinesAsync(Guid documentId) => documents.GetLinesAsync(documentId);

    public async Task<CommercialDocument> CreateDraftAsync(DocumentType type, Client client)
    {
        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        var today = Today();
        var document = new CommercialDocument
        {
            Type = type,
            Status = DocumentStatus.Draft,
            IssueDate = today,
            DueDate = today.AddDays(type == DocumentType.Quote ? company.QuoteValidityDays : company.PaymentTermDays),
        };
        ApplyClient(document, client);
        await documents.SaveAsync(document).ConfigureAwait(false);
        return document;
    }

    public static void ApplyClient(CommercialDocument document, Client client)
    {
        document.ClientId = client.Id;
        document.ClientName = client.Name;
        document.ClientAddress = client.FullAddress;
        document.ClientCompanyId = client.CompanyId;
    }

    /// <summary>Enregistre un brouillon et ses lignes, en recalculant les totaux.</summary>
    public async Task SaveDraftAsync(CommercialDocument document, IReadOnlyList<DocumentLine> lines)
    {
        EnsureEditable(document);
        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        DocumentCalculator.ApplyTotals(document, DocumentCalculator.Compute(lines, company.VatExempt));

        var now = clock.GetUtcNow().UtcDateTime;
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.RunInTransactionAsync(conn =>
        {
            Stamp(document, now);
            conn.InsertOrReplace(document);

            var keptIds = new HashSet<Guid>();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                line.DocumentId = document.Id;
                line.Position = i;
                Stamp(line, now);
                conn.InsertOrReplace(line);
                keptIds.Add(line.Id);
            }

            // Les lignes retirées sont supprimées logiquement (pour la future synchronisation).
            var existing = conn.Table<DocumentLine>().Where(l => l.DocumentId == document.Id && !l.IsDeleted).ToList();
            foreach (var removed in existing.Where(l => !keptIds.Contains(l.Id)))
            {
                removed.IsDeleted = true;
                Stamp(removed, now);
                conn.Update(removed);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Émet le document : il reçoit son numéro définitif (numérotation continue, sans trou) et n'est plus modifiable.
    /// </summary>
    public async Task IssueAsync(CommercialDocument document)
    {
        EnsureEditable(document);
        var lines = await documents.GetLinesAsync(document.Id).ConfigureAwait(false);
        if (lines.Count == 0)
            throw new InvalidOperationException("Impossible d'émettre un document sans ligne.");

        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        DocumentCalculator.ApplyTotals(document, DocumentCalculator.Compute(lines, company.VatExempt));

        var today = Today();
        if (document.Type == DocumentType.Invoice)
        {
            // La date d'une facture est sa date d'émission.
            document.IssueDate = today;
            document.DueDate = today.AddDays(company.PaymentTermDays);
        }

        var prefix = document.Type == DocumentType.Quote ? company.QuotePrefix : company.InvoicePrefix;
        var now = clock.GetUtcNow().UtcDateTime;
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.RunInTransactionAsync(conn =>
        {
            var next = NextSequenceValue(conn, document.Type, document.IssueDate.Year);
            document.Number = $"{prefix}-{document.IssueDate.Year}-{next:D4}";
            document.Status = DocumentStatus.Issued;
            Stamp(document, now);
            conn.InsertOrReplace(document);
        }).ConfigureAwait(false);
    }

    public async Task SetStatusAsync(CommercialDocument document, DocumentStatus status)
    {
        if (!CanChangeStatus(document, status))
            throw new InvalidOperationException($"Passage de {document.Status} à {status} non autorisé.");

        document.Status = status;
        document.PaidDate = status == DocumentStatus.Paid ? Today() : null;
        await documents.SaveAsync(document).ConfigureAwait(false);
    }

    public static bool CanChangeStatus(CommercialDocument document, DocumentStatus target) =>
        (document.Type, document.Status, target) switch
        {
            (DocumentType.Quote, DocumentStatus.Issued, DocumentStatus.Accepted or DocumentStatus.Refused or DocumentStatus.Cancelled) => true,
            (DocumentType.Quote, DocumentStatus.Accepted or DocumentStatus.Refused, DocumentStatus.Issued) => true,
            (DocumentType.Invoice, DocumentStatus.Issued, DocumentStatus.Paid or DocumentStatus.Cancelled) => true,
            (DocumentType.Invoice, DocumentStatus.Paid, DocumentStatus.Issued) => true,
            _ => false,
        };

    /// <summary>Crée une facture brouillon à partir d'un devis émis ; le devis passe en « accepté ».</summary>
    public async Task<CommercialDocument> CreateInvoiceFromQuoteAsync(CommercialDocument quote)
    {
        if (quote.Type != DocumentType.Quote)
            throw new InvalidOperationException("Le document source doit être un devis.");
        if (quote.Status is not (DocumentStatus.Issued or DocumentStatus.Accepted))
            throw new InvalidOperationException("Seul un devis émis ou accepté peut être facturé.");

        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        var quoteLines = await documents.GetLinesAsync(quote.Id).ConfigureAwait(false);
        var today = Today();

        var invoice = new CommercialDocument
        {
            Type = DocumentType.Invoice,
            Status = DocumentStatus.Draft,
            ClientId = quote.ClientId,
            ClientName = quote.ClientName,
            ClientAddress = quote.ClientAddress,
            ClientCompanyId = quote.ClientCompanyId,
            Title = quote.Title,
            SiteAddress = quote.SiteAddress,
            Notes = quote.Number is null ? quote.Notes : $"Selon devis n° {quote.Number}",
            IssueDate = today,
            DueDate = today.AddDays(company.PaymentTermDays),
            SourceDocumentId = quote.Id,
        };
        var lines = quoteLines.Select(l => l.Clone(invoice.Id)).ToList();

        if (quote.Status == DocumentStatus.Issued)
        {
            quote.Status = DocumentStatus.Accepted;
            await documents.SaveAsync(quote).ConfigureAwait(false);
        }
        await SaveDraftAsync(invoice, lines).ConfigureAwait(false);
        return invoice;
    }

    /// <summary>Copie n'importe quel document en un nouveau brouillon du même type.</summary>
    public async Task<CommercialDocument> DuplicateAsync(CommercialDocument source)
    {
        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        var sourceLines = await documents.GetLinesAsync(source.Id).ConfigureAwait(false);
        var today = Today();
        var copy = new CommercialDocument
        {
            Type = source.Type,
            ClientId = source.ClientId,
            ClientName = source.ClientName,
            ClientAddress = source.ClientAddress,
            ClientCompanyId = source.ClientCompanyId,
            Title = source.Title,
            SiteAddress = source.SiteAddress,
            Notes = source.Notes,
            IssueDate = today,
            DueDate = today.AddDays(source.Type == DocumentType.Quote ? company.QuoteValidityDays : company.PaymentTermDays),
        };
        await SaveDraftAsync(copy, sourceLines.Select(l => l.Clone(copy.Id)).ToList()).ConfigureAwait(false);
        return copy;
    }

    /// <summary>Seul un brouillon peut être supprimé. Un document émis doit être annulé.</summary>
    public async Task DeleteDraftAsync(CommercialDocument document)
    {
        EnsureEditable(document);
        await documents.DeleteAsync(document).ConfigureAwait(false);
    }

    private static int NextSequenceValue(SQLiteConnection conn, DocumentType type, int year)
    {
        var key = $"{type}-{year}";
        var sequence = conn.Find<NumberSequence>(key) ?? new NumberSequence { Key = key };
        sequence.LastValue++;
        conn.InsertOrReplace(sequence);
        return sequence.LastValue;
    }

    private static void EnsureEditable(CommercialDocument document)
    {
        if (!document.IsEditable)
            throw new InvalidOperationException("Un document émis ne peut plus être modifié.");
    }

    private static void Stamp(EntityBase entity, DateTime now)
    {
        entity.UpdatedAt = now;
        entity.IsDirty = true;
    }

    private DateTime Today() => clock.GetLocalNow().Date;
}
