using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Data;

public class DocumentRepository(AppDatabase database, TimeProvider clock) : Repository<CommercialDocument>(database, clock)
{
    public async Task<List<CommercialDocument>> GetByTypeAsync(DocumentType type, string? search = null)
    {
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        var docs = await db.Table<CommercialDocument>()
            .Where(d => d.Type == type && !d.IsDeleted)
            .ToListAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            docs = docs.Where(d =>
                d.ClientName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (d.Number?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (d.Title?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();
        }

        return docs.OrderByDescending(d => d.IssueDate).ThenByDescending(d => d.CreatedAt).ToList();
    }

    public async Task<List<CommercialDocument>> GetByClientAsync(Guid clientId)
    {
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        var docs = await db.Table<CommercialDocument>()
            .Where(d => d.ClientId == clientId && !d.IsDeleted)
            .ToListAsync().ConfigureAwait(false);
        return docs.OrderByDescending(d => d.IssueDate).ToList();
    }

    public async Task<List<DocumentLine>> GetLinesAsync(Guid documentId)
    {
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        var lines = await db.Table<DocumentLine>()
            .Where(l => l.DocumentId == documentId && !l.IsDeleted)
            .ToListAsync().ConfigureAwait(false);
        return lines.OrderBy(l => l.Position).ToList();
    }
}
