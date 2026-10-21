using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Data;

public class ClientRepository(AppDatabase database, TimeProvider clock) : Repository<Client>(database, clock)
{
    public async Task<List<Client>> SearchAsync(string? text)
    {
        var all = await GetAllAsync().ConfigureAwait(false);
        IEnumerable<Client> result = all;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = text.Trim();
            result = all.Where(c =>
                Contains(c.Name, term) || Contains(c.ContactName, term) || Contains(c.City, term) ||
                Contains(c.Phone, term) || Contains(c.Email, term));
        }
        return result.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;
}
