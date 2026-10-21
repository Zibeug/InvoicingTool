using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

public partial class ClientsViewModel : ViewModelBase
{
    private readonly ClientRepository _clients;
    private readonly INavigationService _navigation;

    public ClientsViewModel(ClientRepository clients, INavigationService navigation, IDialogService dialogs) : base(dialogs)
    {
        _clients = clients;
        _navigation = navigation;
        Title = "Clients";
    }

    public ObservableCollection<Client> Clients { get; } = [];

    [ObservableProperty]
    private string? _searchText;

    public override Task OnAppearingAsync() => LoadAsync();

    partial void OnSearchTextChanged(string? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var result = await _clients.SearchAsync(SearchText);
        Clients.Clear();
        foreach (var client in result)
            Clients.Add(client);
    }

    [RelayCommand]
    private Task AddAsync() => _navigation.GoToAsync(Routes.ClientEdit);

    [RelayCommand]
    private Task OpenAsync(Client? client) => client is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.ClientEdit, new Dictionary<string, object> { [Routes.IdParameter] = client.Id });
}
