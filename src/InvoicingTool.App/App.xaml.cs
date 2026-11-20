using InvoicingTool.App.Services;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App;

public partial class App : Application
{
	private readonly StartupCoordinator _startup;
	private readonly BackgroundSync _sync;

	public App(StartupCoordinator startup, ThemeService theme, BackgroundSync sync)
	{
		_startup = startup;
		_sync = sync;
		InitializeComponent();
		theme.ApplyAtStartup();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell(_startup, _sync)) { Title = "Devis & Factures" };
		// Retour dans l'application : récupère les modifications faites sur les autres appareils.
		window.Resumed += async (_, _) => await _sync.TrySyncAsync();
#if WINDOWS
		// Sur ordinateur : fenêtre au format « portrait », adaptée à la mise en page pensée pour le téléphone.
		window.Width = 560;
		window.Height = 900;
		window.MinimumWidth = 420;
		window.MinimumHeight = 600;
#endif
		return window;
	}
}
