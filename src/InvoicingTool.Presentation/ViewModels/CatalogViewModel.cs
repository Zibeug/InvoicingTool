using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Article affiché dans la liste : pastille, libellé, prix dans la devise de l'entreprise, unité en clair.</summary>
public sealed record CatalogItemRow(CatalogItem Item, string Initials, string PriceDisplay, string UnitDisplay, string? VatDisplay)
{
    public string Label => Item.Label;
    public string? Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
    public bool HasVat => VatDisplay is not null;
}

/// <summary>Catalogue des produits et services vendus régulièrement.</summary>
public partial class CatalogViewModel : ViewModelBase
{
    private readonly CatalogRepository _catalog;
    private readonly SettingsRepository _settings;
    private readonly INavigationService _navigation;
    private List<CatalogItem> _all = [];
    private MoneyFormatter _money = new(null, null);
    private bool _showVat;

    public CatalogViewModel(
        CatalogRepository catalog,
        SettingsRepository settings,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _catalog = catalog;
        _settings = settings;
        _navigation = navigation;
        Title = "Articles";
    }

    public ObservableCollection<CatalogItemRow> Items { get; } = [];

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private string _countText = string.Empty;

    /// <summary>Couleur choisie pour les documents, reprise sur les pastilles.</summary>
    [ObservableProperty] private string _accentColor = CompanySettings.DefaultAccentColor;

    [ObservableProperty] private bool _hasItems;

    public override async Task OnAppearingAsync()
    {
        var company = await _settings.GetSettingsAsync();
        _money = MoneyFormatter.For(company);
        _showVat = !company.VatExempt;
        AccentColor = ImageData.NormalizeColor(company.AccentColor, CompanySettings.DefaultAccentColor);
        _all = await _catalog.GetSortedAsync();
        Refresh();
    }

    partial void OnSearchTextChanged(string? value) => Refresh();

    private void Refresh()
    {
        var term = SearchText?.Trim();
        var visible = string.IsNullOrEmpty(term)
            ? _all
            : _all.Where(i => i.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                              (i.Description?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();

        Items.Clear();
        foreach (var item in visible)
        {
            Items.Add(new CatalogItemRow(
                item,
                Initials(item.Label),
                _money.Format(item.UnitPrice),
                UnitDisplay(item.Unit),
                _showVat ? $"TVA {_money.Number(item.VatRate)} %" : null));
        }

        HasItems = _all.Count > 0;
        CountText = _all.Count switch
        {
            0 => string.Empty,
            1 => "1 article",
            _ when visible.Count != _all.Count => $"{visible.Count} sur {_all.Count} articles",
            _ => $"{_all.Count} articles",
        };
    }

    /// <summary>Deux premières lettres significatives : « Heure de conseil » → « HC ».</summary>
    public static string Initials(string label)
    {
        var words = label.Split([' ', '-', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && char.IsLetterOrDigit(w[0]))
            .ToList();
        var initials = words.Count switch
        {
            0 => label.Trim().Length > 0 ? label.Trim()[..1] : "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => $"{words[0][0]}{words[1][0]}",
        };
        return initials.ToUpper(CultureInfo.GetCultureInfo("fr-FR"));
    }

    /// <summary>Unité lisible sous le prix : « par heure », « forfait »…</summary>
    public static string UnitDisplay(string unit) => unit switch
    {
        "h" => "par heure",
        "min" => "par minute",
        "jour" => "par jour",
        "semaine" => "par semaine",
        "mois" => "par mois",
        "nuit" => "par nuit",
        "séance" => "par séance",
        "u" or "" => "l'unité",
        "pièce" => "la pièce",
        "forfait" => "forfait",
        _ => $"par {unit}",
    };

    [RelayCommand]
    private Task AddAsync() => _navigation.GoToAsync(Routes.CatalogItemEdit);

    [RelayCommand]
    private Task OpenAsync(CatalogItemRow? row) => row is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.CatalogItemEdit, new Dictionary<string, object> { [Routes.IdParameter] = row.Item.Id });
}
