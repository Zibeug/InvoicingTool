using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Édition d'un devis ou d'une facture, utilisable directement chez le client.</summary>
public partial class DocumentEditViewModel : ViewModelBase, IParameterReceiver
{
    private readonly DocumentRepository _documents;
    private readonly DocumentService _service;
    private readonly SettingsRepository _settings;
    private readonly CatalogRepository _catalog;
    private readonly INavigationService _navigation;

    private Guid _documentId;
    private CommercialDocument _document = new();
    private CompanySettings _company = new();
    private MoneyFormatter _money = new(null, null);
    private bool _loaded;
    private bool _deleted;

    public DocumentEditViewModel(
        DocumentRepository documents,
        DocumentService service,
        SettingsRepository settings,
        CatalogRepository catalog,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _documents = documents;
        _service = service;
        _settings = settings;
        _catalog = catalog;
        _navigation = navigation;
    }

    public ObservableCollection<LineItemViewModel> Lines { get; } = [];

    [ObservableProperty] private string? _subject;
    [ObservableProperty] private string? _siteAddress;
    [ObservableProperty] private DateTime _issueDate = DateTime.Today;
    [ObservableProperty] private DateTime _dueDate = DateTime.Today;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _depositText = "0";

    [ObservableProperty] private string _clientName = string.Empty;
    [ObservableProperty] private string _number = string.Empty;
    [ObservableProperty] private string _statusLabel = string.Empty;
    [ObservableProperty] private bool _isEditable;
    [ObservableProperty] private bool _isQuote;
    [ObservableProperty] private bool _canConvertToInvoice;
    [ObservableProperty] private bool _canChangeStatus;
    [ObservableProperty] private bool _showVat = true;

    [ObservableProperty] private string _totalExclTaxDisplay = string.Empty;
    [ObservableProperty] private string _totalVatDisplay = string.Empty;
    [ObservableProperty] private string _totalInclTaxDisplay = string.Empty;
    [ObservableProperty] private string _roundingDisplay = string.Empty;
    [ObservableProperty] private bool _hasRounding;
    [ObservableProperty] private string _amountDueDisplay = string.Empty;

    [ObservableProperty] private string _taxName = "TVA";
    [ObservableProperty] private string? _taxRateHint;
    [ObservableProperty] private string _depositLabel = "Acompte versé";

    public string DueDateLabel => IsQuote ? "Valable jusqu'au" : "Échéance";

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(Routes.IdParameter, out var id))
            _documentId = (Guid)id;
    }

    public override async Task OnAppearingAsync()
    {
        _company = await _settings.GetSettingsAsync();
        var document = await _documents.GetAsync(_documentId);
        if (document is null)
        {
            await _navigation.GoBackAsync();
            return;
        }
        _document = document;
        // Un brouillon suit le pays, la devise et le régime de TVA actuels ; un document émis garde les siens.
        if (document.IsEditable)
            document.ApplyTaxSettings(_company);
        _money = MoneyFormatter.For(document);

        Subject = document.Title;
        SiteAddress = document.SiteAddress;
        IssueDate = document.IssueDate;
        DueDate = document.DueDate ?? document.IssueDate;
        Notes = document.Notes;
        DepositText = DecimalParser.Format(document.DepositAmount);

        foreach (var line in Lines)
            line.AmountChanged -= OnLineAmountChanged;
        Lines.Clear();
        foreach (var line in await _service.GetLinesAsync(document.Id))
            AddLineViewModel(line);

        RefreshState();
        _loaded = true;
    }

    public override async Task OnDisappearingAsync()
    {
        // Sauvegarde automatique : rien n'est perdu si l'artisan quitte la page ou est interrompu.
        if (_loaded && !_deleted && _document.IsEditable)
            await SaveDraftAsync();
    }

    partial void OnDepositTextChanged(string value) => RefreshTotals();

    [RelayCommand]
    private void AddLine()
    {
        var line = new DocumentLine
        {
            DocumentId = _document.Id,
            Quantity = 1,
            Unit = "u",
            VatRate = _document.VatExempt ? 0m : _company.DefaultVatRate,
        };
        AddLineViewModel(line);
        RefreshTotals();
    }

    [RelayCommand]
    private Task AddFromCatalogAsync() => RunAsync(async () =>
    {
        var items = await _catalog.GetSortedAsync();
        if (items.Count == 0)
        {
            await Dialogs.AlertAsync("Prestations", "Aucune prestation enregistrée. Ajoutez-en dans l'onglet « Prestations ».");
            return;
        }

        var labels = items.Select(i => $"{i.Label} — {_money.Format(i.UnitPrice)}/{i.Unit}").ToList();
        var choice = await Dialogs.ChooseAsync("Ajouter une prestation", labels);
        var index = choice is null ? -1 : labels.IndexOf(choice);
        if (index < 0)
            return;

        var item = items[index];
        AddLineViewModel(new DocumentLine
        {
            DocumentId = _document.Id,
            Description = string.IsNullOrWhiteSpace(item.Description) ? item.Label : $"{item.Label}\n{item.Description}",
            Quantity = 1,
            Unit = item.Unit,
            UnitPrice = item.UnitPrice,
            VatRate = _document.VatExempt ? 0m : item.VatRate,
        });
        RefreshTotals();
    });

    [RelayCommand]
    private void RemoveLine(LineItemViewModel? line)
    {
        if (line is null || !IsEditable)
            return;
        line.AmountChanged -= OnLineAmountChanged;
        Lines.Remove(line);
        RefreshTotals();
    }

    [RelayCommand]
    private void MoveLineUp(LineItemViewModel? line)
    {
        if (line is null || !IsEditable)
            return;
        var index = Lines.IndexOf(line);
        if (index > 0)
            Lines.Move(index, index - 1);
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (await SaveDraftAsync())
            await _navigation.GoBackAsync();
    });

    [RelayCommand]
    private Task IssueAsync() => RunAsync(async () =>
    {
        if (!await SaveDraftAsync())
            return;
        var kind = IsQuote ? "ce devis" : "cette facture";
        if (!await Dialogs.ConfirmAsync("Émettre",
                $"Émettre {kind} ? Un numéro définitif sera attribué et le document ne sera plus modifiable."))
            return;
        await _service.IssueAsync(_document);
        RefreshState();
    });

    [RelayCommand]
    private Task PreviewAsync() => RunAsync(async () =>
    {
        if (_document.IsEditable && !await SaveDraftAsync())
            return;
        await _navigation.GoToAsync(Routes.DocumentPreview,
            new Dictionary<string, object> { [Routes.IdParameter] = _document.Id });
    });

    [RelayCommand]
    private Task ChangeStatusAsync() => RunAsync(async () =>
    {
        var targets = Enum.GetValues<DocumentStatus>()
            .Where(s => DocumentService.CanChangeStatus(_document, s))
            .ToList();
        if (targets.Count == 0)
            return;

        var labels = targets.Select(s => DisplayText.Status(_document.Type, s)).ToList();
        var choice = await Dialogs.ChooseAsync("Changer le statut", labels);
        var index = choice is null ? -1 : labels.IndexOf(choice);
        if (index < 0)
            return;

        await _service.SetStatusAsync(_document, targets[index]);
        RefreshState();
    });

    [RelayCommand]
    private Task ConvertToInvoiceAsync() => RunAsync(async () =>
    {
        var invoice = await _service.CreateInvoiceFromQuoteAsync(_document);
        await _navigation.GoToAsync($"../{Routes.DocumentEdit}",
            new Dictionary<string, object> { [Routes.IdParameter] = invoice.Id });
    });

    [RelayCommand]
    private Task DuplicateAsync() => RunAsync(async () =>
    {
        if (_document.IsEditable && !await SaveDraftAsync())
            return;
        var copy = await _service.DuplicateAsync(_document);
        await _navigation.GoToAsync($"../{Routes.DocumentEdit}",
            new Dictionary<string, object> { [Routes.IdParameter] = copy.Id });
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Supprimer", "Supprimer ce brouillon ?"))
            return;
        await _service.DeleteDraftAsync(_document);
        _deleted = true;
        await _navigation.GoBackAsync();
    });

    /// <summary>Enregistre le brouillon. Retourne false si la saisie est invalide.</summary>
    private async Task<bool> SaveDraftAsync()
    {
        if (!_document.IsEditable)
            return true;

        if (Lines.Any(l => l.HasErrors) || !DecimalParser.TryParse(DepositText, out var deposit))
        {
            await Dialogs.AlertAsync("Saisie invalide", "Vérifiez les quantités, prix et taux saisis.");
            return false;
        }
        if (DueDate < IssueDate)
        {
            await Dialogs.AlertAsync("Saisie invalide", "La date d'échéance / de validité est antérieure à la date du document.");
            return false;
        }

        _document.Title = Subject;
        _document.SiteAddress = SiteAddress;
        _document.IssueDate = IssueDate.Date;
        _document.DueDate = DueDate.Date;
        _document.Notes = Notes;
        _document.DepositAmount = deposit;

        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Line.Description) || l.Line.UnitPrice != 0)
            .Select(l => l.Line)
            .ToList();
        await _service.SaveDraftAsync(_document, lines);
        return true;
    }

    private void AddLineViewModel(DocumentLine line)
    {
        var vm = new LineItemViewModel(line, _money);
        vm.AmountChanged += OnLineAmountChanged;
        Lines.Add(vm);
    }

    private void OnLineAmountChanged(object? sender, EventArgs e) => RefreshTotals();

    private void RefreshState()
    {
        IsQuote = _document.Type == DocumentType.Quote;
        Title = $"{DisplayText.Type(_document.Type)} {_document.DisplayNumber}";
        Number = _document.DisplayNumber;
        ClientName = _document.ClientName;
        StatusLabel = DisplayText.Status(_document.Type, _document.Status);
        IsEditable = _document.IsEditable;
        CanConvertToInvoice = IsQuote && _document.Status is DocumentStatus.Issued or DocumentStatus.Accepted;
        CanChangeStatus = Enum.GetValues<DocumentStatus>().Any(s => DocumentService.CanChangeStatus(_document, s));
        var profile = CountryProfiles.Get(_document.CountryCode);
        ShowVat = !_document.VatExempt;
        TaxName = profile.TaxName;
        TaxRateHint = ShowVat && IsEditable ? profile.TaxRateHint : null;
        DepositLabel = $"Acompte versé ({_money.CurrencySymbol})";
        IssueDate = _document.IssueDate;
        DueDate = _document.DueDate ?? _document.IssueDate;
        OnPropertyChanged(nameof(DueDateLabel));
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        var totals = DocumentCalculator.Compute(_document, Lines.Select(l => l.Line));
        TotalExclTaxDisplay = _money.Format(totals.TotalExclTax);
        TotalVatDisplay = _money.Format(totals.TotalVat);
        TotalInclTaxDisplay = _money.Format(totals.TotalInclTax);
        HasRounding = totals.RoundingAdjustment != 0;
        RoundingDisplay = _money.Format(totals.RoundingAdjustment);
        AmountDueDisplay = _money.Format(totals.TotalInclTax - DecimalParser.ParseOrZero(DepositText));
    }
}
