using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Tests;

public sealed class FakeNavigation : INavigationService
{
    public List<(string Route, IDictionary<string, object>? Parameters)> History { get; } = [];
    public int BackCount { get; private set; }

    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        History.Add((route, parameters));
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        BackCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeDialogs : IDialogService
{
    public List<string> Alerts { get; } = [];
    public bool ConfirmResult { get; set; } = true;
    public Func<IReadOnlyList<string>, string?> Choose { get; set; } = options => options.FirstOrDefault();

    public Task AlertAsync(string title, string message)
    {
        Alerts.Add(message);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Oui", string cancel = "Non") =>
        Task.FromResult(ConfirmResult);

    public Task<string?> ChooseAsync(string title, IReadOnlyList<string> options, string cancel = "Annuler") =>
        Task.FromResult(Choose(options));
}

public sealed class FakeShare : IShareService
{
    public List<(string FileName, byte[] Content, string ContentType)> Shared { get; } = [];

    public Task ShareFileAsync(string fileName, byte[] content, string contentType, string title)
    {
        Shared.Add((fileName, content, contentType));
        return Task.CompletedTask;
    }

    public Task OpenFileAsync(string fileName, byte[] content, string contentType, string title) =>
        ShareFileAsync(fileName, content, contentType, title);
}

public sealed class FakeFilePicker : IFilePickerService
{
    public byte[]? Content { get; set; }

    public Task<Stream?> PickBackupFileAsync() =>
        Task.FromResult<Stream?>(Content is null ? null : new MemoryStream(Content));
}
