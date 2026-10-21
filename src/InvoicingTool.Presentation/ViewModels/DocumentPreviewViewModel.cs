using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Aperçu du document tel que le client le verra, avec partage.</summary>
public partial class DocumentPreviewViewModel(
    DocumentRepository documents,
    DocumentService service,
    SettingsRepository settings,
    IShareService share,
    IDialogService dialogs) : ViewModelBase(dialogs), IParameterReceiver
{
    private Guid _documentId;
    private string _fileName = "document.html";

    [ObservableProperty]
    private string _html = string.Empty;

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(Routes.IdParameter, out var id))
            _documentId = (Guid)id;
    }

    public override async Task OnAppearingAsync()
    {
        var document = await documents.GetAsync(_documentId);
        if (document is null)
            return;
        var lines = await service.GetLinesAsync(document.Id);
        var company = await settings.GetSettingsAsync();

        Title = $"{DisplayText.Type(document.Type)} {document.DisplayNumber}";
        _fileName = $"{DisplayText.Type(document.Type)}_{document.Number ?? "brouillon"}_{Sanitize(document.ClientName)}.html";
        Html = HtmlDocumentRenderer.Render(document, lines, company);
    }

    [RelayCommand]
    private Task ShareAsync() => RunAsync(() => share.ShareHtmlAsync(_fileName, Html, Title));

    private static string Sanitize(string text)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat([' ', '/', '\\']).ToHashSet();
        return new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
