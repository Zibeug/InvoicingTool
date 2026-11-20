using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Sync;

public class SyncStateStore(AppDatabase database)
{
    public async Task<SyncState> GetAsync()
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        return await db.FindAsync<SyncState>(SyncState.SingletonId).ConfigureAwait(false) ?? new SyncState();
    }

    public async Task SaveAsync(SyncState state)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(state).ConfigureAwait(false);
    }

    /// <summary>Série de numérotation de cet appareil (null hors synchronisation ou pour le premier appareil).</summary>
    public async Task<string?> GetSeriesLetterAsync()
    {
        var state = await GetAsync().ConfigureAwait(false);
        return state.IsEnabled ? state.SeriesLetter : null;
    }
}
