using System.Text.RegularExpressions;
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
    TimeProvider clock,
    Sync.SyncStateStore? syncState = null)
{
    /// <summary>
    /// Série de numérotation de cet appareil : null hors synchronisation et pour le premier appareil du compte,
    /// sinon une lettre (« B », « C »…) qui rend les numéros uniques entre appareils, même hors ligne.
    /// </summary>
    public async Task<string?> GetSeriesAsync() =>
        syncState is null ? null : await syncState.GetSeriesLetterAsync().ConfigureAwait(false);

    /// <summary>« F-2026-0012 », ou « F-2026-B-0012 » sur un appareil de la série B.</summary>
    public static string FormatNumber(string prefix, int year, string? series, int value) =>
        series is null ? $"{prefix}-{year}-{value:D4}" : $"{prefix}-{year}-{series}-{value:D4}";

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
        document.ApplyTaxSettings(company);
        await documents.SaveAsync(document).ConfigureAwait(false);
        return document;
    }

    public static void ApplyClient(CommercialDocument document, Client client)
    {
        document.ClientId = client.Id;
        document.ClientName = client.Name;
        document.ClientAddress = client.FullAddress;
        document.ClientCompanyId = client.CompanyId;
        document.ClientStreet = client.Address;
        document.ClientPostalCode = client.PostalCode;
        document.ClientCity = client.City;
        document.ClientCountryCode = client.CountryCode;
    }

    /// <summary>Enregistre un brouillon et ses lignes, en recalculant les totaux.</summary>
    public async Task SaveDraftAsync(CommercialDocument document, IReadOnlyList<DocumentLine> lines)
    {
        EnsureEditable(document);
        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        // Un brouillon suit les paramètres courants (pays, devise, TVA) ; ils sont figés à l'émission.
        document.ApplyTaxSettings(company);
        DocumentCalculator.ApplyTotals(document, DocumentCalculator.Compute(document, lines));

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
        // Un brouillon suit les paramètres courants (pays, devise, TVA) ; ils sont figés à l'émission.
        document.ApplyTaxSettings(company);
        DocumentCalculator.ApplyTotals(document, DocumentCalculator.Compute(document, lines));

        var today = Today();
        if (document.Type == DocumentType.Invoice)
        {
            // La date d'une facture est sa date d'émission.
            document.IssueDate = today;
            document.DueDate = today.AddDays(company.PaymentTermDays);
        }

        var prefix = document.Type == DocumentType.Quote ? company.QuotePrefix : company.InvoicePrefix;
        var series = await GetSeriesAsync().ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.RunInTransactionAsync(conn =>
        {
            var next = NextSequenceValue(conn, document.Type, document.IssueDate.Year, series);
            document.Number = FormatNumber(prefix, document.IssueDate.Year, series, next);
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

    /// <summary>Enregistre le paiement d'une facture émise, à la date où il a été reçu.</summary>
    public async Task MarkPaidAsync(CommercialDocument invoice, DateTime paidDate)
    {
        if (invoice.Type != DocumentType.Invoice || invoice.Status != DocumentStatus.Issued)
            throw new InvalidOperationException(invoice.Status == DocumentStatus.Draft
                ? "Émettez d'abord la facture : un brouillon ne peut pas être payé."
                : "Seule une facture émise et impayée peut être marquée comme payée.");
        var date = paidDate.Date;
        if (date > Today())
            throw new InvalidOperationException("La date de paiement ne peut pas être dans le futur.");
        if (date < invoice.IssueDate.Date)
            throw new InvalidOperationException("La date de paiement ne peut pas précéder la date de la facture.");

        invoice.Status = DocumentStatus.Paid;
        invoice.PaidDate = date;
        await documents.SaveAsync(invoice).ConfigureAwait(false);
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
            ClientStreet = quote.ClientStreet,
            ClientPostalCode = quote.ClientPostalCode,
            ClientCity = quote.ClientCity,
            ClientCountryCode = quote.ClientCountryCode,
            Title = quote.Title,
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
            ClientStreet = source.ClientStreet,
            ClientPostalCode = source.ClientPostalCode,
            ClientCity = source.ClientCity,
            ClientCountryCode = source.ClientCountryCode,
            Title = source.Title,
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

    /// <summary>Enregistre la signature du client sur un devis émis ; le devis passe en « accepté ».</summary>
    public async Task SignQuoteAsync(CommercialDocument quote, byte[] signaturePng, string signerName)
    {
        if (quote.Type != DocumentType.Quote)
            throw new InvalidOperationException("Seul un devis peut être signé.");
        if (quote.Status is not (DocumentStatus.Issued or DocumentStatus.Accepted))
            throw new InvalidOperationException("Émettez le devis avant de le faire signer.");
        if (!ImageData.IsSupported(signaturePng))
            throw new InvalidOperationException("La signature est vide.");
        if (await documents.GetSignatureAsync(quote.Id).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("Ce devis est déjà signé.");

        var now = clock.GetUtcNow().UtcDateTime;
        var signature = new DocumentSignature
        {
            DocumentId = quote.Id,
            Image = signaturePng,
            SignerName = signerName.Trim(),
            SignedAt = now,
        };
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.RunInTransactionAsync(conn =>
        {
            Stamp(signature, now);
            conn.Insert(signature);
            quote.Status = DocumentStatus.Accepted;
            Stamp(quote, now);
            conn.InsertOrReplace(quote);
        }).ConfigureAwait(false);
    }

    /// <summary>Prochain numéro qui sera attribué (ex. 1 pour « F-2026-0001 »).</summary>
    public async Task<int> GetNextNumberAsync(DocumentType type, int year)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var series = await GetSeriesAsync().ConfigureAwait(false);
        var sequence = await db.FindAsync<NumberSequence>(SequenceKey(type, year, series)).ConfigureAwait(false);
        return (sequence?.LastValue ?? 0) + 1;
    }

    /// <summary>
    /// Choisit le numéro de départ (par exemple pour continuer la numérotation d'un ancien logiciel).
    /// Refusé s'il créerait un doublon avec un document déjà émis cette année.
    /// </summary>
    public async Task SetNextNumberAsync(DocumentType type, int year, int next)
    {
        if (next < 1)
            throw new InvalidOperationException("Le numéro doit être supérieur ou égal à 1.");

        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var series = await GetSeriesAsync().ConfigureAwait(false);
        var issued = await db.Table<CommercialDocument>()
            .Where(d => d.Type == type && d.Number != null)
            .ToListAsync().ConfigureAwait(false);
        var highest = issued
            .Where(d => d.IssueDate.Year == year && SeriesOf(d.Number!) == series)
            .Select(d => SequenceNumber(d.Number!))
            .DefaultIfEmpty(0)
            .Max();
        if (next <= highest)
        {
            var kind = type == DocumentType.Quote ? "devis" : "facture";
            throw new InvalidOperationException(
                $"Le numéro {next} est déjà utilisé : la dernière {kind} émise en {year} porte le n° {highest}. Choisissez au moins {highest + 1}.");
        }

        await db.InsertOrReplaceAsync(new NumberSequence { Key = SequenceKey(type, year, series), LastValue = next - 1 }).ConfigureAwait(false);
    }

    private static int SequenceNumber(string number)
    {
        var match = Regex.Match(number, @"(\d+)$");
        return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
    }

    /// <summary>Série d'un numéro : « F-2026-B-0012 » → « B » ; « F-2026-0012 » → null.</summary>
    public static string? SeriesOf(string number)
    {
        var match = Regex.Match(number, @"-\d{4}-(?<series>[A-Z]{1,2}\d*)-\d+$");
        return match.Success ? match.Groups["series"].Value : null;
    }

    private static string SequenceKey(DocumentType type, int year, string? series) =>
        series is null ? $"{type}-{year}" : $"{type}-{year}-{series}";

    private static int NextSequenceValue(SQLiteConnection conn, DocumentType type, int year, string? series)
    {
        var key = SequenceKey(type, year, series);
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
