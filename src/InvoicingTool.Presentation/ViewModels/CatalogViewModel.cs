using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Catalogue des prestations et fournitures habituelles de l'artisan.</summary>
public partial class CatalogViewModel : ViewModelBase
{
    private readonly CatalogRepository _catalog;
    private readonly INavigationService _navigation;

    public CatalogViewModel(CatalogRepository catalog, INavigationService navigation, IDialogService dialogs) : base(dialogs)
    {
        _catalog = catalog;
        _navigation = navigation;
        Title = "Prestations";
    }

    public ObservableCollection<CatalogItem> Items { get; } = [];

    public override async Task OnAppearingAsync()
    {
        var items = await _catalog.GetSortedAsync();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
    }

    [RelayCommand]
    private Task AddAsync() => _navigation.GoToAsync(Routes.CatalogItemEdit);

    [RelayCommand]
    private Task OpenAsync(CatalogItem? item) => item is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.CatalogItemEdit, new Dictionary<string, object> { [Routes.IdParameter] = item.Id });
}
