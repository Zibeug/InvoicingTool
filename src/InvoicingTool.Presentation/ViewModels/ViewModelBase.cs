using CommunityToolkit.Mvvm.ComponentModel;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

public abstract partial class ViewModelBase(IDialogService dialogs) : ObservableObject
{
    protected IDialogService Dialogs { get; } = dialogs;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    /// <summary>Appelé à chaque affichage de la page (rafraîchissement des listes).</summary>
    public virtual Task OnAppearingAsync() => Task.CompletedTask;

    /// <summary>Appelé quand la page est quittée (sauvegarde automatique des saisies).</summary>
    public virtual Task OnDisappearingAsync() => Task.CompletedTask;

    /// <summary>Exécute une action en affichant les erreurs métier à l'utilisateur au lieu de planter l'application.</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (InvalidOperationException ex)
        {
            await Dialogs.AlertAsync("Action impossible", ex.Message);
        }
        catch (Exception ex)
        {
            await Dialogs.AlertAsync("Erreur", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
