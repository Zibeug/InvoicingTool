using System.Text.Json;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Sync.Contracts;
using SQLite;

namespace InvoicingTool.Core.Sync;

public sealed record SyncResult(int Sent, int Received);

/// <summary>
/// Synchronisation des données avec le serveur : envoi des modifications locales (éléments marqués
/// « pas encore synchronisés »), puis réception de celles des autres appareils.
/// Les numéros de séquence (numérotation) restent propres à chaque appareil : chacun a sa série.
/// </summary>
public class SyncEngine(AppDatabase database, SyncApiClient api, SyncStateStore states, TimeProvider clock)
{
    private static readonly SemaphoreSlim Running = new(1, 1);

    /// <summary>Types synchronisés : nom côté serveur ↔ table locale.</summary>
    private static readonly IReadOnlyList<(string Name, Type Type)> EntityTypes =
    [
        (SyncEntityTypes.Settings, typeof(CompanySettings)),
        (SyncEntityTypes.Client, typeof(Client)),
        (SyncEntityTypes.CatalogItem, typeof(CatalogItem)),
        (SyncEntityTypes.Document, typeof(CommercialDocument)),
        (SyncEntityTypes.DocumentLine, typeof(DocumentLine)),
        (SyncEntityTypes.Signature, typeof(DocumentSignature)),
        (SyncEntityTypes.Reminder, typeof(PaymentReminder)),
    ];

    public async Task<SyncResult> SyncAsync()
    {
        // Une seule synchronisation à la fois (démarrage, retour dans l'app, bouton…).
        await Running.WaitAsync().ConfigureAwait(false);
        try
        {
            var state = await states.GetAsync().ConfigureAwait(false);
            if (!state.IsEnabled)
                throw new SyncException("La synchronisation n'est pas activée sur cet appareil.", requiresLogin: true);

            try
            {
                var received = 0;
                // Premier passage d'un appareil qui rejoint le compte : il reçoit d'abord les données du compte,
                // pour que ses paramètres encore vides n'écrasent pas ceux des autres appareils.
                if (state.LastSyncAt is null)
                    received += await PullAsync(state, firstSync: true).ConfigureAwait(false);
                var sent = await PushAsync().ConfigureAwait(false);
                received += await PullAsync(state, firstSync: false).ConfigureAwait(false);

                state.LastSyncAt = clock.GetUtcNow().UtcDateTime;
                state.LastError = null;
                await states.SaveAsync(state).ConfigureAwait(false);
                return new SyncResult(sent, received);
            }
            catch (SyncException ex)
            {
                state.LastError = ex.Message;
                if (ex.RequiresLogin)
                    state.IsEnabled = false;
                await states.SaveAsync(state).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            Running.Release();
        }
    }

    private async Task<int> PushAsync()
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var pending = new List<(SyncChange Change, string Table)>();
        foreach (var (name, type) in EntityTypes)
        {
            var table = TableName(type);
            var items = await db.QueryAsync(new TableMapping(type), $"SELECT * FROM \"{table}\" WHERE IsDirty = 1").ConfigureAwait(false);
            foreach (EntityBase entity in items)
            {
                var payload = JsonSerializer.SerializeToElement(entity, type, DataJson.Options);
                pending.Add((new SyncChange(name, entity.Id, DateTime.SpecifyKind(entity.UpdatedAt, DateTimeKind.Utc), entity.IsDeleted, payload), table));
            }
        }

        foreach (var batch in pending.Chunk(SyncLimits.MaxBatchSize))
        {
            await api.PushAsync(batch.Select(p => p.Change).ToList()).ConfigureAwait(false);
            // N'efface le marqueur que si l'élément n'a pas été modifié entre-temps (sinon il repartira au prochain envoi).
            await db.RunInTransactionAsync(conn =>
            {
                foreach (var (change, table) in batch)
                    conn.Execute($"UPDATE \"{table}\" SET IsDirty = 0 WHERE Id = ? AND UpdatedAt = ?", change.EntityId, change.UpdatedAt);
            }).ConfigureAwait(false);
        }
        return pending.Count;
    }

    private async Task<int> PullAsync(SyncState state, bool firstSync)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var received = 0;
        while (true)
        {
            var response = await api.PullAsync(state.LastPulledVersion).ConfigureAwait(false);
            await db.RunInTransactionAsync(conn =>
            {
                foreach (var change in response.Changes)
                    Apply(conn, change, firstSync);
            }).ConfigureAwait(false);
            received += response.Changes.Count;
            state.LastPulledVersion = response.Version;
            await states.SaveAsync(state).ConfigureAwait(false);
            if (!response.HasMore)
                return received;
        }
    }

    private static void Apply(SQLiteConnection conn, SyncChange change, bool firstSync)
    {
        var type = EntityTypes.FirstOrDefault(t => t.Name == change.EntityType).Type;
        if (type is null)
            return; // type apparu dans une version plus récente de l'application

        if (JsonSerializer.Deserialize(change.Payload, type, DataJson.Options) is not EntityBase remote)
            return;
        var local = conn.Find(change.EntityId, new TableMapping(type)) as EntityBase;

        if (remote is CompanySettings remoteSettings)
        {
            // Paramètres propres à l'appareil : acceptation des conditions et date de dernière sauvegarde.
            var localSettings = local as CompanySettings;
            remoteSettings.TermsAcceptedVersion = localSettings?.TermsAcceptedVersion;
            remoteSettings.TermsAcceptedAt = localSettings?.TermsAcceptedAt;
            remoteSettings.LastBackupAt = localSettings?.LastBackupAt;
            // Appareil qui rejoint le compte : les paramètres du compte priment sur les siens.
            if (firstSync && local is not null)
                local.IsDirty = false;
        }

        // Une modification locale pas encore envoyée est plus récente : elle partira au prochain envoi.
        if (local is { IsDirty: true })
            return;

        remote.IsDirty = false;
        conn.InsertOrReplace(remote, type);
    }

    private static string TableName(Type type) => new TableMapping(type).TableName;
}
