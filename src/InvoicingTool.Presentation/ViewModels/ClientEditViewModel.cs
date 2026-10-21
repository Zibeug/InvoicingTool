using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Fiche client : coordonnées, historique des devis / factures et création rapide d'un document.</summary>
public partial class ClientEditViewModel : ViewModelBase, IParameterReceiver
{
    private readonly ClientRepository _clients;
    private readonly DocumentRepository _documents;
    private readonly DocumentService _documentService;
    private readonly INavigationService _navigation;
    private Client _client = new();

    public ClientEditViewModel(
        ClientRepository clients,
        DocumentRepository documents,
        DocumentService documentService,
        INavigationService navigation,
        IDialogService dialogs) : base(dialogs)
    {
        _clients = clients;
        _documents = documents;
        _documentService = documentService;
        _navigation = navigation;
        Title = "Nouveau client";
    }

    private Guid? _clientId;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _contactName;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _postalCode;
    [ObservableProperty] private string? _city;
    [ObservableProperty] private string? _companyId;
    [ObservableProperty] private string? _notes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    private bool _isExisting;

    public bool IsNew => !IsExisting;

    public ObservableCollection<CommercialDocument> Documents { get; } = [];

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        _clientId = parameters.TryGetValue(Routes.IdParameter, out var id) ? (Guid)id : null;
    }

    public override async Task OnAppearingAsync()
    {
        if (_clientId is not { } id)
            return;

        _client = await _clients.GetAsync(id) ?? new Client();
        IsExisting = true;
        Title = _client.Name;
        Name = _client.Name;
        ContactName = _client.ContactName;
        Phone = _client.Phone;
        Email = _client.Email;
        Address = _client.Address;
        PostalCode = _client.PostalCode;
        City = _client.City;
        CompanyId = _client.CompanyId;
        Notes = _client.Notes;

        Documents.Clear();
        foreach (var doc in await _documents.GetByClientAsync(id))
            Documents.Add(doc);
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (await SaveClientAsync())
            await _navigation.GoBackAsync();
    });

    [RelayCommand]
    private Task NewQuoteAsync() => RunAsync(() => CreateDocumentAsync(DocumentType.Quote));

    [RelayCommand]
    private Task NewInvoiceAsync() => RunAsync(() => CreateDocumentAsync(DocumentType.Invoice));

    [RelayCommand]
    private Task OpenDocumentAsync(CommercialDocument? document) => document is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.DocumentEdit, new Dictionary<string, object> { [Routes.IdParameter] = document.Id });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (!IsExisting)
            return;
        if (Documents.Count > 0)
        {
            await Dialogs.AlertAsync("Suppression impossible", "Ce client possède des devis ou des factures.");
            return;
        }
        if (!await Dialogs.ConfirmAsync("Supprimer", $"Supprimer le client « {_client.Name} » ?"))
            return;
        await _clients.DeleteAsync(_client);
        await _navigation.GoBackAsync();
    });

    private async Task CreateDocumentAsync(DocumentType type)
    {
        if (!await SaveClientAsync())
            return;
        var document = await _documentService.CreateDraftAsync(type, _client);
        await _navigation.GoToAsync(Routes.DocumentEdit, new Dictionary<string, object> { [Routes.IdParameter] = document.Id });
    }

    private async Task<bool> SaveClientAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Dialogs.AlertAsync("Client", "Le nom est obligatoire.");
            return false;
        }

        _client.Name = Name.Trim();
        _client.ContactName = ContactName;
        _client.Phone = Phone;
        _client.Email = Email;
        _client.Address = Address;
        _client.PostalCode = PostalCode;
        _client.City = City;
        _client.CompanyId = CompanyId;
        _client.Notes = Notes;
        await _clients.SaveAsync(_client);

        _clientId = _client.Id;
        IsExisting = true;
        Title = _client.Name;
        return true;
    }
}
