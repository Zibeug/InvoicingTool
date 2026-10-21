using InvoicingTool.Core.Data;
using InvoicingTool.Core.Services;
using Microsoft.Extensions.Time.Testing;

namespace InvoicingTool.Tests;

/// <summary>Base SQLite temporaire sur disque, supprimée à la fin du test.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}.db3");

    public TestDatabase()
    {
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));
        Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        Database = new AppDatabase(_path);
        Clients = new ClientRepository(Database, Clock);
        Documents = new DocumentRepository(Database, Clock);
        Settings = new SettingsRepository(Database, Clock);
        Catalog = new CatalogRepository(Database, Clock);
        Service = new DocumentService(Database, Documents, Settings, Clock);
    }

    public FakeTimeProvider Clock { get; }
    public AppDatabase Database { get; }
    public ClientRepository Clients { get; }
    public DocumentRepository Documents { get; }
    public SettingsRepository Settings { get; }
    public CatalogRepository Catalog { get; }
    public DocumentService Service { get; }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
