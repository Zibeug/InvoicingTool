using Microsoft.UI.Xaml;

namespace InvoicingTool.App.WinUI;

/// <summary>Point d'entrée Windows (WinUI) : démarre l'application MAUI commune.</summary>
public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

