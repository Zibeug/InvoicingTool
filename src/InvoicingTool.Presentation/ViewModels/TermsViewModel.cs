using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Conditions générales d'utilisation : à accepter au premier lancement, consultables ensuite.</summary>
public partial class TermsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settings;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _clock;

    public TermsViewModel(SettingsRepository settings, INavigationService navigation, TimeProvider clock, IDialogService dialogs)
        : base(dialogs)
    {
        _settings = settings;
        _navigation = navigation;
        _clock = clock;
        Title = TermsOfUse.Title;
    }

    public string Text => TermsOfUse.Text;

    public string VersionLabel => $"Version {TermsOfUse.Version}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MustAccept))]
    private bool _isAccepted = true;

    /// <summary>Tant que les conditions ne sont pas acceptées, la page ne peut pas être fermée.</summary>
    public bool MustAccept => !IsAccepted;

    public static async Task<bool> AreAcceptedAsync(SettingsRepository settings) =>
        (await settings.GetSettingsAsync()).TermsAcceptedVersion == TermsOfUse.Version;

    public override async Task OnAppearingAsync() => IsAccepted = await AreAcceptedAsync(_settings);

    [RelayCommand]
    private Task AcceptAsync() => RunAsync(async () =>
    {
        var company = await _settings.GetSettingsAsync();
        company.TermsAcceptedVersion = TermsOfUse.Version;
        company.TermsAcceptedAt = _clock.GetUtcNow().UtcDateTime;
        await _settings.SaveAsync(company);
        IsAccepted = true;
        await _navigation.GoBackAsync();
    });

    [RelayCommand]
    private Task CloseAsync() => _navigation.GoBackAsync();

    [RelayCommand]
    private Task DeclineAsync() => Dialogs.AlertAsync(TermsOfUse.Title,
        "L'application ne peut pas être utilisée sans accepter les conditions d'utilisation.");
}
