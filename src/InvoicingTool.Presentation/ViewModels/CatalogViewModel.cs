using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Prestation affichée dans la liste, avec son prix formaté dans la devise de l'entreprise.</summary>
public sealed record CatalogItemRow(CatalogItem Item, string PriceDisplay)
{
    public string Label => Item.Label;
    public string? Description => Item.Description;
}

/// <summary>Catalogue des prestations et fournitures habituelles de l'artisan.</summary>
public partial class CatalogViewModel : ViewModelBase
{
    private readonly CatalogRepository _catalog;
    private readonly SettingsRepository _settings;
    private readonly INavigationService _navigation;

    public CatalogViewModel(
        CatalogRepository catalog,
        SettingsRepository settings,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _catalog = catalog;
        _settings = settings;
        _navigation = navigation;
        Title = "Prestations";
    }

    public ObservableCollection<CatalogItemRow> Items { get; } = [];

    public override async Task OnAppearingAsync()
    {
        var money = MoneyFormatter.For(await _settings.GetSettingsAsync());
        var items = await _catalog.GetSortedAsync();
        Items.Clear();
        foreach (var item in items)
            Items.Add(new CatalogItemRow(item, $"{money.Format(item.UnitPrice)} / {item.Unit}"));
    }

    [RelayCommand]
    private Task AddAsync() => _navigation.GoToAsync(Routes.CatalogItemEdit);

    [RelayCommand]
    private Task OpenAsync(CatalogItemRow? row) => row is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.CatalogItemEdit, new Dictionary<string, object> { [Routes.IdParameter] = row.Item.Id });
}
