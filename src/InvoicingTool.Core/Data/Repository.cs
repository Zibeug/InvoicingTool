using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Data;

/// <summary>Accès générique aux entités, avec suppression logique.</summary>
public class Repository<T>(AppDatabase database, TimeProvider clock) where T : EntityBase, new()
{
    protected AppDatabase Database { get; } = database;
    protected TimeProvider Clock { get; } = clock;

    public async Task<List<T>> GetAllAsync()
    {
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        return await db.Table<T>().Where(e => !e.IsDeleted).ToListAsync().ConfigureAwait(false);
    }

    public async Task<T?> GetAsync(Guid id)
    {
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        return await db.Table<T>().Where(e => e.Id == id && !e.IsDeleted).FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task SaveAsync(T entity)
    {
        Touch(entity);
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(entity).ConfigureAwait(false);
    }

    /// <summary>Suppression logique : la ligne reste en base pour pouvoir propager la suppression lors d'une synchronisation.</summary>
    public async Task DeleteAsync(T entity)
    {
        entity.IsDeleted = true;
        await SaveAsync(entity).ConfigureAwait(false);
    }

    /// <summary>Entités modifiées localement et pas encore synchronisées (y compris les suppressions).</summary>
    public async Task<List<T>> GetPendingChangesAsync()
    {
        var db = await Database.GetConnectionAsync().ConfigureAwait(false);
        return await db.Table<T>().Where(e => e.IsDirty).ToListAsync().ConfigureAwait(false);
    }

    protected void Touch(T entity)
    {
        entity.UpdatedAt = Clock.GetUtcNow().UtcDateTime;
        entity.IsDirty = true;
    }
}
