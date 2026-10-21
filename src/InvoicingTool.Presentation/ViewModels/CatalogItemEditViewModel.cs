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
        Title = "Nouvelle prestation";
    }

    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string _unit = "m²";
    [ObservableProperty] private string _unitPriceText = "0";
    [ObservableProperty] private string _vatRateText = "10";
    [ObservableProperty] private bool _isExisting;
    [ObservableProperty] private string _priceLabel = "Prix unitaire HT";
    [ObservableProperty] private string? _taxRateHint;

    /// <summary>Unités proposées ; la saisie libre reste possible.</summary>
    public IReadOnlyList<string> CommonUnits { get; } = ["m²", "ml", "h", "j", "u", "forfait", "L", "kg"];

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        _itemId = parameters.TryGetValue(Routes.IdParameter, out var id) ? (Guid)id : null;
    }

    public override async Task OnAppearingAsync()
    {
        var company = await _settings.GetSettingsAsync();
        PriceLabel = $"Prix unitaire HT ({MoneyFormatter.For(company).CurrencySymbol})";
        TaxRateHint = company.VatExempt ? null : company.Country.TaxRateHint;

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
            await Dialogs.AlertAsync("Prestation", "Le libellé est obligatoire.");
            return;
        }
        if (!DecimalParser.TryParse(UnitPriceText, out var price) || !DecimalParser.TryParse(VatRateText, out var vat))
        {
            await Dialogs.AlertAsync("Prestation", "Prix ou taux de TVA invalide.");
            return;
        }

        _item.Label = Label.Trim();
        _item.Description = Description;
        _item.Unit = string.IsNullOrWhiteSpace(Unit) ? "u" : Unit.Trim();
        _item.UnitPrice = price;
        _item.VatRate = vat;
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
