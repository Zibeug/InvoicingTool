using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App;

public partial class AppShell : Shell
{
	private readonly StartupCoordinator _startup;

	public AppShell(StartupCoordinator startup)
	{
		_startup = startup;
		InitializeComponent();
		Loaded += OnLoaded;
	}

	/// <summary>Au démarrage : conditions d'utilisation à accepter, puis rappel de sauvegarde.</summary>
	private async void OnLoaded(object? sender, EventArgs e)
	{
		Loaded -= OnLoaded;
		try
		{
			await _startup.RunAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erreur", ex.Message, "OK");
		}
	}
}
