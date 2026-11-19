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
		return new Window(new AppShell(_startup));
	}
}
