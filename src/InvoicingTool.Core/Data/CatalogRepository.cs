using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Data;

public class CatalogRepository(AppDatabase database, TimeProvider clock) : Repository<CatalogItem>(database, clock)
{
    public async Task<List<CatalogItem>> GetSortedAsync()
    {
        var all = await GetAllAsync().ConfigureAwait(false);
        return all.OrderBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
