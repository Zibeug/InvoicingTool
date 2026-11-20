using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App;

public partial class AppShell : Shell
{
	private readonly StartupCoordinator _startup;
	private readonly BackgroundSync _sync;

	public AppShell(StartupCoordinator startup, BackgroundSync sync)
	{
		_startup = startup;
		_sync = sync;
		InitializeComponent();
		Loaded += OnLoaded;
		// Modifications reçues d'un autre appareil : l'écran affiché est rechargé.
		_sync.DataChanged += (_, _) => MainThread.BeginInvokeOnMainThread(async () =>
		{
			if (CurrentPage is Views.BasePage page)
				await page.RefreshAsync();
		});
	}

	/// <summary>Au démarrage : conditions d'utilisation à accepter, puis rappel de sauvegarde.</summary>
	private async void OnLoaded(object? sender, EventArgs e)
	{
		Loaded -= OnLoaded;
		try
		{
			await _startup.RunAsync();
			await _sync.TrySyncAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erreur", ex.Message, "OK");
		}
	}
}
