using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Liste des devis ou des factures.</summary>
public abstract partial class DocumentsViewModel : ViewModelBase
{
    private readonly DocumentRepository _documents;
    private readonly INavigationService _navigation;

    protected DocumentsViewModel(
        DocumentType type,
        DocumentRepository documents,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        Type = type;
        _documents = documents;
        _navigation = navigation;
        Title = type == DocumentType.Quote ? "Devis" : "Factures";
    }

    public DocumentType Type { get; }

    public ObservableCollection<CommercialDocument> Documents { get; } = [];

    [ObservableProperty]
    private string? _searchText;

    /// <summary>Montant TTC total des documents affichés en attente (devis à signer / factures impayées).</summary>
    [ObservableProperty]
    private decimal _pendingAmount;

    public override Task OnAppearingAsync() => LoadAsync();

    partial void OnSearchTextChanged(string? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var result = await _documents.GetByTypeAsync(Type, SearchText);
        Documents.Clear();
        foreach (var doc in result)
            Documents.Add(doc);
        PendingAmount = result.Where(d => d.Status == DocumentStatus.Issued).Sum(d => d.AmountDue);
    }

    [RelayCommand]
    private Task AddAsync() => _navigation.GoToAsync(Routes.ClientPicker,
        new Dictionary<string, object> { [Routes.DocumentTypeParameter] = Type });

    [RelayCommand]
    private Task OpenAsync(CommercialDocument? document) => document is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.DocumentEdit, new Dictionary<string, object> { [Routes.IdParameter] = document.Id });
}

public sealed class QuotesViewModel(DocumentRepository documents, INavigationService navigation, IDialogService dialogs)
    : DocumentsViewModel(DocumentType.Quote, documents, navigation, dialogs);

public sealed class InvoicesViewModel(DocumentRepository documents, INavigationService navigation, IDialogService dialogs)
    : DocumentsViewModel(DocumentType.Invoice, documents, navigation, dialogs);
