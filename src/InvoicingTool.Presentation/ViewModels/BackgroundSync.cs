using InvoicingTool.Core.Sync;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>
/// Synchronisation automatique (ouverture de l'application, retour dans l'application) : silencieuse,
/// l'éventuelle erreur est conservée et affichée dans l'écran Synchronisation.
/// </summary>
public sealed class BackgroundSync(SyncEngine engine, SyncStateStore states)
{
    /// <summary>Déclenché après une synchronisation réussie qui a reçu des modifications.</summary>
    public event EventHandler? DataChanged;

    public async Task TrySyncAsync()
    {
        try
        {
            if (!(await states.GetAsync()).IsEnabled)
                return;
            var result = await engine.SyncAsync();
            if (result.Received > 0)
                DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (SyncException)
        {
            // Hors ligne ou session expirée : l'erreur est enregistrée dans l'état de synchronisation.
        }
    }
}
