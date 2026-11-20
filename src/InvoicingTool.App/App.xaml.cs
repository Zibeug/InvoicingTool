using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App;

public partial class App : Application
{
	private readonly StartupCoordinator _startup;

	public App(StartupCoordinator startup)
	{
		_startup = startup;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell(_startup)) { Title = "Devis & Factures" };
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
