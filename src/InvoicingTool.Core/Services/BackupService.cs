using System.Text.Json;
using System.Text.Json.Serialization;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>Contenu d'un fichier de sauvegarde : toutes les tables de la base locale.</summary>
public sealed class BackupFile
{
    public const string FormatName = "InvoicingTool.Backup";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; }

    public CompanySettings? Settings { get; set; }
    public List<Client> Clients { get; set; } = [];
    public List<CatalogItem> Catalog { get; set; } = [];
    public List<CommercialDocument> Documents { get; set; } = [];
    public List<DocumentLine> Lines { get; set; } = [];
    public List<DocumentSignature> Signatures { get; set; } = [];
    public List<PaymentReminder> Reminders { get; set; } = [];
    public List<NumberSequence> Sequences { get; set; } = [];
}

public sealed record BackupSummary(int Clients, int Documents, int CatalogItems, DateTime ExportedAt);

/// <summary>
/// Sauvegarde complète des données dans un fichier JSON (lisible, indépendant de l'application)
/// et restauration à partir de ce fichier, par exemple sur un nouveau téléphone.
/// </summary>
public class BackupService(AppDatabase database, SettingsRepository settings, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(DataJson.Options) { WriteIndented = true };

    public string SuggestedFileName() =>
        $"sauvegarde-devis-factures-{clock.GetLocalNow():yyyy-MM-dd-HHmm}.json";

    /// <summary>Exporte toutes les données (y compris les éléments supprimés, utiles à une future synchronisation).</summary>
    public async Task<byte[]> ExportAsync()
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var backup = new BackupFile
        {
            ExportedAt = now,
            Settings = await settings.GetAsync(CompanySettings.SingletonId).ConfigureAwait(false),
            Clients = await db.Table<Client>().ToListAsync().ConfigureAwait(false),
            Catalog = await db.Table<CatalogItem>().ToListAsync().ConfigureAwait(false),
            Documents = await db.Table<CommercialDocument>().ToListAsync().ConfigureAwait(false),
            Lines = await db.Table<DocumentLine>().ToListAsync().ConfigureAwait(false),
            Signatures = await db.Table<DocumentSignature>().ToListAsync().ConfigureAwait(false),
            Reminders = await db.Table<PaymentReminder>().ToListAsync().ConfigureAwait(false),
            Sequences = await db.Table<NumberSequence>().ToListAsync().ConfigureAwait(false),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, JsonOptions);

        // Mémorise la date pour pouvoir rappeler à l'utilisateur de sauvegarder régulièrement.
        var current = await settings.GetSettingsAsync().ConfigureAwait(false);
        current.LastBackupAt = now;
        await settings.SaveAsync(current).ConfigureAwait(false);
        return bytes;
    }

    /// <summary>Lit et vérifie un fichier de sauvegarde sans rien modifier.</summary>
    public static BackupFile Read(Stream content)
    {
        BackupFile? backup;
        try
        {
            backup = JsonSerializer.Deserialize<BackupFile>(content, JsonOptions);
        }
        catch (JsonException)
        {
            backup = null;
        }

        if (backup is null || backup.Format != BackupFile.FormatName)
            throw new InvalidOperationException("Ce fichier n'est pas une sauvegarde de l'application.");
        if (backup.Version > BackupFile.CurrentVersion)
            throw new InvalidOperationException("Cette sauvegarde vient d'une version plus récente de l'application. Mettez l'application à jour.");
        return backup;
    }

    public static BackupSummary Summarize(BackupFile backup) => new(
        backup.Clients.Count(c => !c.IsDeleted),
        backup.Documents.Count(d => !d.IsDeleted),
        backup.Catalog.Count(i => !i.IsDeleted),
        backup.ExportedAt);

    /// <summary>
    /// Remplace toutes les données du téléphone par celles de la sauvegarde, en une seule transaction :
    /// en cas d'erreur, rien n'est modifié. L'acceptation des conditions d'utilisation de ce téléphone est conservée.
    /// </summary>
    public async Task RestoreAsync(BackupFile backup)
    {
        var current = await settings.GetSettingsAsync().ConfigureAwait(false);
        var restoredSettings = backup.Settings ?? new CompanySettings();
        restoredSettings.Id = CompanySettings.SingletonId;
        restoredSettings.TermsAcceptedVersion = current.TermsAcceptedVersion;
        restoredSettings.TermsAcceptedAt = current.TermsAcceptedAt;
        restoredSettings.LastBackupAt = backup.ExportedAt;

        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<DocumentSignature>();
            conn.DeleteAll<PaymentReminder>();
            conn.DeleteAll<DocumentLine>();
            conn.DeleteAll<CommercialDocument>();
            conn.DeleteAll<Client>();
            conn.DeleteAll<CatalogItem>();
            conn.DeleteAll<NumberSequence>();
            conn.DeleteAll<CompanySettings>();

            conn.InsertAll(backup.Clients);
            conn.InsertAll(backup.Catalog);
            conn.InsertAll(backup.Documents);
            conn.InsertAll(backup.Lines);
            conn.InsertAll(backup.Signatures);
            conn.InsertAll(backup.Reminders);
            conn.InsertAll(backup.Sequences);
            conn.Insert(restoredSettings);
            // Synchronisation : l'appareil redemandera tout l'historique au serveur.
            conn.Execute("UPDATE SyncState SET LastPulledVersion = 0, LastSyncAt = NULL");
        }).ConfigureAwait(false);
    }

    /// <summary>Vrai s'il y a des documents et aucune sauvegarde depuis <paramref name="maxAge"/>.</summary>
    public async Task<bool> IsBackupOverdueAsync(TimeSpan maxAge)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var hasData = await db.Table<CommercialDocument>().Where(d => !d.IsDeleted).CountAsync().ConfigureAwait(false) > 0;
        if (!hasData)
            return false;
        var last = (await settings.GetSettingsAsync().ConfigureAwait(false)).LastBackupAt;
        return last is null || clock.GetUtcNow().UtcDateTime - last.Value > maxAge;
    }
}
