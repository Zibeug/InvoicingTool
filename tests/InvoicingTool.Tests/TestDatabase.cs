using InvoicingTool.Core.Data;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;
using InvoicingTool.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace InvoicingTool.Tests;

/// <summary>Base SQLite temporaire sur disque, supprimée à la fin du test.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"invoicing-test-{Guid.NewGuid():N}.db3");

    public TestDatabase(DateTimeOffset? start = null)
    {
        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));
        Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        Database = new AppDatabase(_path);
        Clients = new ClientRepository(Database, Clock);
        Documents = new DocumentRepository(Database, Clock);
        Settings = new SettingsRepository(Database, Clock);
        Catalog = new CatalogRepository(Database, Clock);
        Service = new DocumentService(Database, Documents, Settings, Clock);
        Backup = new BackupService(Database, Settings, Clock);
        Reminders = new ReminderService(Database, Documents, Settings, Clock);
        Dashboard = new DashboardService(Documents, Settings, Clock);
        Export = new AccountingExportService(Documents, Settings, Clock);
    }

    public FakeTimeProvider Clock { get; }
    public AppDatabase Database { get; }
    public ClientRepository Clients { get; }
    public DocumentRepository Documents { get; }
    public SettingsRepository Settings { get; }
    public CatalogRepository Catalog { get; }
    public DocumentService Service { get; }
    public BackupService Backup { get; }
    public ReminderService Reminders { get; }
    public DashboardService Dashboard { get; }
    public AccountingExportService Export { get; }

    public SettingsViewModel CreateSettingsViewModel(
        IDialogService? dialogs = null, IShareService? share = null,
        IFilePickerService? filePicker = null, IImagePickerService? imagePicker = null) =>
        new(Settings, Backup, share ?? new FakeShare(), filePicker ?? new FakeFilePicker(),
            imagePicker ?? new FakeImagePicker(), Service, Export, new FakeNavigation(), Clock, dialogs ?? new FakeDialogs());

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
