using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Sync;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>
/// Synchronisation entre appareils (facultative) : création de compte ou connexion à un serveur,
/// synchronisation manuelle, état et déconnexion.
/// </summary>
public partial class SyncViewModel : ViewModelBase
{
    private readonly SyncApiClient _api;
    private readonly SyncEngine _engine;
    private readonly SyncStateStore _states;
    private readonly TimeProvider _clock;

    public SyncViewModel(
        SyncApiClient api,
        SyncEngine engine,
        SyncStateStore states,
        IDeviceInfoService device,
        TimeProvider clock,
        IDialogService dialogs) : base(dialogs)
    {
        _api = api;
        _engine = engine;
        _states = states;
        _clock = clock;
        _deviceName = device.DeviceName;
        Title = "Synchronisation";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled;

    public bool IsDisabled => !IsEnabled;

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _deviceName;

    [ObservableProperty] private string _accountText = string.Empty;
    [ObservableProperty] private string _lastSyncText = string.Empty;
    [ObservableProperty] private string _seriesText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public override Task OnAppearingAsync() => RefreshAsync();

    [RelayCommand]
    private Task RegisterAsync() => RunAsync(async () =>
    {
        if (!await ValidateAsync())
            return;
        await _api.RegisterAsync(ServerUrl, Email, Password, DeviceName);
        await AfterSignInAsync();
    });

    [RelayCommand]
    private Task LoginAsync() => RunAsync(async () =>
    {
        if (!await ValidateAsync())
            return;
        await _api.LoginAsync(ServerUrl, Email, Password, DeviceName);
        await AfterSignInAsync();
    });

    [RelayCommand]
    private Task SyncNowAsync() => RunAsync(async () =>
    {
        try
        {
            var result = await _engine.SyncAsync();
            await RefreshAsync();
            await Dialogs.AlertAsync("Synchronisation", result.Sent + result.Received == 0
                ? "Tout est à jour."
                : $"{result.Sent} modification(s) envoyée(s), {result.Received} reçue(s).");
        }
        catch (SyncException)
        {
            await RefreshAsync(); // l'erreur s'affiche dans l'écran
        }
    });

    [RelayCommand]
    private Task LogoutAsync() => RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Synchronisation",
                "Désactiver la synchronisation sur cet appareil ? Vos données restent sur l'appareil et sur le serveur.",
                "Désactiver", "Annuler"))
            return;
        await _api.LogoutAsync();
        await RefreshAsync();
    });

    private async Task AfterSignInAsync()
    {
        Password = string.Empty;
        try
        {
            await _engine.SyncAsync();
        }
        catch (SyncException)
        {
            // Connecté, mais la première synchronisation a échoué : l'erreur s'affiche, on pourra réessayer.
        }
        await RefreshAsync();
    }

    private async Task<bool> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            await Dialogs.AlertAsync("Synchronisation", "Indiquez l'adresse du serveur, votre e-mail et votre mot de passe.");
            return false;
        }
        return true;
    }

    private async Task RefreshAsync()
    {
        var state = await _states.GetAsync();
        IsEnabled = state.IsEnabled;
        if (!string.IsNullOrWhiteSpace(state.ServerUrl) && string.IsNullOrWhiteSpace(ServerUrl))
            ServerUrl = state.ServerUrl;
        if (!string.IsNullOrWhiteSpace(state.Email) && string.IsNullOrWhiteSpace(Email))
            Email = state.Email;
        ErrorText = state.LastError;
        AccountText = state.IsEnabled ? $"Connecté : {state.Email} · {state.ServerUrl}" : "Synchronisation désactivée sur cet appareil.";
        SeriesText = state.SeriesLetter is null
            ? "Numérotation de cet appareil : série principale (ex. F-2026-0012)."
            : $"Numérotation de cet appareil : série {state.SeriesLetter} (ex. F-2026-{state.SeriesLetter}-0001), " +
              "pour qu'aucun numéro ne soit utilisé deux fois, même hors ligne.";
        LastSyncText = state.LastSyncAt is { } last
            ? $"Dernière synchronisation : {Ago(last)}"
            : "Pas encore synchronisé.";
    }

    private string Ago(DateTime utc)
    {
        var minutes = (int)(_clock.GetUtcNow().UtcDateTime - utc).TotalMinutes;
        return minutes switch
        {
            < 1 => "à l'instant",
            < 60 => $"il y a {minutes} min",
            _ => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
        };
    }
}
