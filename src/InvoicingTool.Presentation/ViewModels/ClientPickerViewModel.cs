using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Choix du client avant la création d'un devis ou d'une facture.</summary>
public partial class ClientPickerViewModel : ViewModelBase, IParameterReceiver
{
    private readonly ClientRepository _clients;
    private readonly DocumentService _documentService;
    private readonly INavigationService _navigation;
    private DocumentType _documentType;

    public ClientPickerViewModel(
        ClientRepository clients,
        DocumentService documentService,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _clients = clients;
        _documentService = documentService;
        _navigation = navigation;
        Title = "Choisir un client";
    }

    public ObservableCollection<Client> Clients { get; } = [];

    [ObservableProperty]
    private string? _searchText;

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(Routes.DocumentTypeParameter, out var type))
            _documentType = (DocumentType)type;
        Title = _documentType == DocumentType.Quote ? "Nouveau devis : client" : "Nouvelle facture : client";
    }

    public override Task OnAppearingAsync() => LoadAsync();

    partial void OnSearchTextChanged(string? value) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        var result = await _clients.SearchAsync(SearchText);
        Clients.Clear();
        foreach (var client in result)
            Clients.Add(client);
    }

    [RelayCommand]
    private Task SelectAsync(Client? client) => client is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var document = await _documentService.CreateDraftAsync(_documentType, client);
        // Remplace la page de sélection par l'éditeur du document.
        await _navigation.GoToAsync($"../{Routes.DocumentEdit}",
            new Dictionary<string, object> { [Routes.IdParameter] = document.Id });
    });

    [RelayCommand]
    private Task NewClientAsync() => _navigation.GoToAsync($"../{Routes.ClientEdit}");
}
