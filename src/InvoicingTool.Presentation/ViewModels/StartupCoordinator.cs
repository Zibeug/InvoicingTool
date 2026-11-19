using InvoicingTool.Core.Data;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>
/// Vérifications au démarrage : acceptation des conditions d'utilisation, puis rappel de sauvegarde
/// si des documents existent et qu'aucune sauvegarde n'a été faite depuis <see cref="BackupReminderAge"/>.
/// </summary>
public sealed class StartupCoordinator(
    SettingsRepository settings,
    BackupService backup,
    INavigationService navigation,
    IShareService share,
    IDialogService dialogs)
{
    public static readonly TimeSpan BackupReminderAge = TimeSpan.FromDays(30);

    private bool _backupReminderShown;

    public async Task RunAsync()
    {
        if (!await TermsViewModel.AreAcceptedAsync(settings))
        {
            await navigation.GoToAsync(Routes.Terms);
            return;
        }

        // Un seul rappel par lancement de l'application.
        if (_backupReminderShown || !await backup.IsBackupOverdueAsync(BackupReminderAge))
            return;
        _backupReminderShown = true;

        if (await dialogs.ConfirmAsync("Sauvegarde",
                "Vos données ne sont enregistrées que sur ce téléphone et n'ont pas été sauvegardées depuis plus de 30 jours. " +
                "Exporter une sauvegarde maintenant ?", "Exporter", "Plus tard"))
        {
            var content = await backup.ExportAsync();
            await share.ShareFileAsync(backup.SuggestedFileName(), content, SettingsViewModel.BackupContentType, "Sauvegarde des données");
        }
    }
}
