using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

public partial class CatalogItemEditViewModel : ViewModelBase, IParameterReceiver
{
    private readonly CatalogRepository _catalog;
    private readonly SettingsRepository _settings;
    private readonly INavigationService _navigation;
    private CatalogItem _item = new();
    private Guid? _itemId;

    public CatalogItemEditViewModel(
        CatalogRepository catalog,
        SettingsRepository settings,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _catalog = catalog;
        _settings = settings;
        _navigation = navigation;
        Title = "Nouvel article";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTitle), nameof(PreviewInitials))]
    private string _label = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewUnit))]
    private string _unit = "u";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewPrice))]
    private string _unitPriceText = "0";

    [ObservableProperty] private string _accentColor = CompanySettings.DefaultAccentColor;

    private MoneyFormatter _money = new(null, null);

    // Aperçu de la carte telle qu'elle apparaîtra dans la liste des articles.
    public string PreviewTitle => string.IsNullOrWhiteSpace(Label) ? "Nouvel article" : Label.Trim();
    public string PreviewInitials => CatalogViewModel.Initials(PreviewTitle);
    public string PreviewPrice => DecimalParser.TryParse(UnitPriceText, out var price) ? _money.Format(price) : "Prix invalide";
    public string PreviewUnit => CatalogViewModel.UnitDisplay(Unit ?? "u");
    [ObservableProperty] private string _vatRateText = "10";
    [ObservableProperty] private bool _isExisting;
    [ObservableProperty] private string _priceLabel = "Prix unitaire HT";
    [ObservableProperty] private string? _taxRateHint;
    [ObservableProperty] private bool _showVat = true;

    /// <summary>Unités proposées ; la saisie libre reste possible.</summary>
    public IReadOnlyList<string> CommonUnits { get; } = ["u", "pièce", "h", "jour", "mois", "forfait", "km", "kg", "m²"];

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        _itemId = parameters.TryGetValue(Routes.IdParameter, out var id) ? (Guid)id : null;
    }

    public override async Task OnAppearingAsync()
    {
        var company = await _settings.GetSettingsAsync();
        _money = MoneyFormatter.For(company);
        AccentColor = ImageData.NormalizeColor(company.AccentColor, CompanySettings.DefaultAccentColor);
        OnPropertyChanged(nameof(PreviewPrice));
        ShowVat = !company.VatExempt;
        PriceLabel = $"{(ShowVat ? "Prix unitaire HT" : "Prix unitaire")} ({MoneyFormatter.For(company).CurrencySymbol})";
        TaxRateHint = ShowVat ? company.Country.TaxRateHint : null;

        if (_itemId is not { } id)
        {
            VatRateText = DecimalParser.Format(company.DefaultVatRate);
            return;
        }

        _item = await _catalog.GetAsync(id) ?? new CatalogItem();
        IsExisting = true;
        Title = _item.Label;
        Label = _item.Label;
        Description = _item.Description;
        Unit = _item.Unit;
        UnitPriceText = DecimalParser.Format(_item.UnitPrice);
        VatRateText = DecimalParser.Format(_item.VatRate);
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            await Dialogs.AlertAsync("Article", "Le libellé est obligatoire.");
            return;
        }
        if (!DecimalParser.TryParse(UnitPriceText, out var price) || !DecimalParser.TryParse(VatRateText, out var vat))
        {
            await Dialogs.AlertAsync("Article", ShowVat ? "Prix ou taux de TVA invalide." : "Prix invalide.");
            return;
        }

        _item.Label = Label.Trim();
        _item.Description = Description;
        _item.Unit = string.IsNullOrWhiteSpace(Unit) ? "u" : Unit.Trim();
        _item.UnitPrice = price;
        _item.VatRate = ShowVat ? vat : 0m;
        await _catalog.SaveAsync(_item);
        await _navigation.GoBackAsync();
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (!IsExisting || !await Dialogs.ConfirmAsync("Supprimer", $"Supprimer « {_item.Label} » ?"))
            return;
        await _catalog.DeleteAsync(_item);
        await _navigation.GoBackAsync();
    });
}
