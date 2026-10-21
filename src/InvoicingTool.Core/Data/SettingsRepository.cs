using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Data;

public class SettingsRepository(AppDatabase database, TimeProvider clock) : Repository<CompanySettings>(database, clock)
{
    public async Task<CompanySettings> GetSettingsAsync() =>
        await GetAsync(CompanySettings.SingletonId).ConfigureAwait(false) ?? new CompanySettings();
}
