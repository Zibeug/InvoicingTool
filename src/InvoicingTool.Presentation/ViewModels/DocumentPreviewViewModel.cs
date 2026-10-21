using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Pdf;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Aperçu du document tel que le client le verra, avec export et partage en PDF.</summary>
public partial class DocumentPreviewViewModel(
    DocumentRepository documents,
    DocumentService service,
    SettingsRepository settings,
    IShareService share,
    IDialogService dialogs) : ViewModelBase(dialogs), IParameterReceiver
{
    public const string PdfContentType = "application/pdf";

    private Guid _documentId;
    private PrintableDocument? _printable;

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

        _printable = PrintableDocument.Build(document, lines, company);
        Title = $"{DisplayText.Type(document.Type)} {document.DisplayNumber}";
        Html = HtmlDocumentRenderer.Render(_printable);
    }

    public string PdfFileName => $"{_printable?.FileNameWithoutExtension ?? "document"}.pdf";

    /// <summary>Génère le PDF (hors du thread d'interface : quelques dizaines de millisecondes).</summary>
    public Task<byte[]> CreatePdfAsync()
    {
        var printable = _printable ?? throw new InvalidOperationException("Document introuvable.");
        return Task.Run(() => PdfDocumentRenderer.Render(printable));
    }

    [RelayCommand]
    private Task SharePdfAsync() => RunAsync(async () =>
        await share.ShareFileAsync(PdfFileName, await CreatePdfAsync(), PdfContentType, Title));

    [RelayCommand]
    private Task OpenPdfAsync() => RunAsync(async () =>
        await share.OpenFileAsync(PdfFileName, await CreatePdfAsync(), PdfContentType, Title));
}
