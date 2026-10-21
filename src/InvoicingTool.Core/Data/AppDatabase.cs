using InvoicingTool.Core.Models;
using SQLite;

namespace InvoicingTool.Core.Data;

/// <summary>
/// Base SQLite locale stockée sur le téléphone. Tout fonctionne hors ligne ;
/// une synchronisation pourra plus tard s'appuyer sur les champs UpdatedAt / IsDirty / IsDeleted.
/// </summary>
public sealed class AppDatabase : IAsyncDisposable
{
    private const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;

    private readonly SQLiteAsyncConnection _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public AppDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        _connection = new SQLiteAsyncConnection(databasePath, Flags);
    }

    public string DatabasePath { get; }

    /// <summary>Retourne la connexion après avoir créé / mis à jour le schéma si nécessaire.</summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_initialized)
            return _connection;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_initialized)
            {
                await _connection.EnableWriteAheadLoggingAsync().ConfigureAwait(false);
                // CreateTables ajoute automatiquement les colonnes manquantes (migration simple).
                await _connection.CreateTablesAsync(CreateFlags.None,
                    typeof(Client),
                    typeof(CatalogItem),
                    typeof(CommercialDocument),
                    typeof(DocumentLine),
                    typeof(CompanySettings),
                    typeof(NumberSequence)).ConfigureAwait(false);
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }
}
