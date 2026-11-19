using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Ligne comprise, telle qu'affichée avant l'ajout au document.</summary>
public sealed record DictatedLineRow(DictatedLine Line, string Title, string Detail, bool MissingPrice);

/// <summary>
/// Dictée de lignes : l'utilisateur parle (ou tape), le texte reste modifiable et les lignes comprises
/// s'affichent en direct ; un bouton les ajoute au devis ou à la facture.
/// </summary>
public partial class DictationViewModel : ViewModelBase, IParameterReceiver
{
    private readonly DocumentRepository _documents;
    private readonly DocumentService _service;
    private readonly SettingsRepository _settings;
    private readonly CatalogRepository _catalog;
    private readonly ISpeechRecognizer _speech;
    private readonly INavigationService _navigation;

    private Guid _documentId;
    private CommercialDocument? _document;
    private CompanySettings _company = new();
    private List<CatalogItem> _catalogItems = [];
    private MoneyFormatter _money = new(null, null);
    /// <summary>Texte déjà validé avant la phrase en cours de dictée.</summary>
    private string _textBeforeListening = string.Empty;

    public DictationViewModel(
        DocumentRepository documents,
        DocumentService service,
        SettingsRepository settings,
        CatalogRepository catalog,
        ISpeechRecognizer speech,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _documents = documents;
        _service = service;
        _settings = settings;
        _catalog = catalog;
        _speech = speech;
        _navigation = navigation;
        Title = "Dicter des lignes";
        _speech.TextUpdated += (_, partial) => Text = Join(_textBeforeListening, partial);
        _speech.Completed += (_, final) =>
        {
            if (!string.IsNullOrWhiteSpace(final))
                Text = Join(_textBeforeListening, final);
            IsListening = false;
        };
    }

    public ObservableCollection<DictatedLineRow> Lines { get; } = [];

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _example = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListenButtonText))]
    private bool _isListening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddButtonText))]
    private int _lineCount;

    public string ListenButtonText => IsListening ? "Terminer" : "Parler";

    public string AddButtonText => LineCount switch
    {
        0 => "Aucune ligne comprise",
        1 => "Ajouter 1 ligne",
        _ => $"Ajouter {LineCount} lignes",
    };

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(Routes.IdParameter, out var id))
            _documentId = (Guid)id;
    }

    public override async Task OnAppearingAsync()
    {
        _document = await _documents.GetAsync(_documentId);
        if (_document is null || !_document.IsEditable)
        {
            await _navigation.GoBackAsync();
            return;
        }
        _company = await _settings.GetSettingsAsync();
        _catalogItems = await _catalog.GetSortedAsync();
        _money = MoneyFormatter.For(_document);

        var currencyWord = _document.CurrencyCode switch
        {
            "CHF" => "francs",
            "USD" or "CAD" => "dollars",
            _ => "euros",
        };
        Example = $"Exemple : « 3 heures de conseil à 120 {currencyWord}, puis un forfait déplacement à 50 {currencyWord} ». " +
                  "Dites « puis » ou « ensuite » entre deux lignes. Les articles enregistrés sont reconnus avec leur prix.";
        UpdatePreview();
    }

    public override async Task OnDisappearingAsync()
    {
        if (IsListening)
            await _speech.StopAsync();
    }

    partial void OnTextChanged(string value) => UpdatePreview();

    [RelayCommand]
    private async Task ToggleListenAsync()
    {
        if (IsListening)
        {
            await _speech.StopAsync();
            IsListening = false;
            return;
        }

        _textBeforeListening = Text.Trim();
        var error = await _speech.StartAsync(CountryProfiles.Get(_document?.CountryCode).SpeechCulture);
        if (error is not null)
        {
            await Dialogs.AlertAsync("Dictée", error);
            return;
        }
        IsListening = true;
    }

    [RelayCommand]
    private void Clear() => Text = string.Empty;

    /// <summary>Ajoute les lignes comprises à la fin du document puis revient à l'éditeur.</summary>
    [RelayCommand]
    private Task AddAsync() => RunAsync(async () =>
    {
        if (_document is null || Lines.Count == 0)
            return;
        if (IsListening)
        {
            await _speech.StopAsync();
            IsListening = false;
        }

        var lines = await _service.GetLinesAsync(_document.Id);
        foreach (var row in Lines)
        {
            var dictated = row.Line;
            lines.Add(new DocumentLine
            {
                DocumentId = _document.Id,
                Description = dictated.Description,
                Quantity = dictated.Quantity,
                Unit = dictated.Unit,
                UnitPrice = dictated.UnitPrice ?? 0m,
                VatRate = _document.VatExempt ? 0m : dictated.VatRate ?? _company.DefaultVatRate,
            });
        }
        await _service.SaveDraftAsync(_document, lines);
        await _navigation.GoBackAsync();
    });

    private void UpdatePreview()
    {
        Lines.Clear();
        foreach (var line in DictationParser.Parse(Text, _catalogItems))
        {
            var title = line.Description.Length == 0 ? "(désignation à compléter)" : line.Description.Split('\n')[0];
            var detail = line.UnitPrice is { } price
                ? $"{DecimalParser.Format(line.Quantity)} {line.Unit} × {_money.Format(price)} = {_money.Format(DocumentCalculator.Round(line.Quantity * price))}"
                : $"{DecimalParser.Format(line.Quantity)} {line.Unit} — prix à compléter";
            Lines.Add(new DictatedLineRow(line, title, detail, line.UnitPrice is null));
        }
        LineCount = Lines.Count;
    }

    private static string Join(string before, string after) =>
        string.IsNullOrWhiteSpace(before) ? after.Trim() : $"{before}, {after.Trim()}";
}
