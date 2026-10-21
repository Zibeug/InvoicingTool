using System.Globalization;
using InvoicingTool.App.Services;
using InvoicingTool.App.Views;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation;
using InvoicingTool.Presentation.Abstractions;
using InvoicingTool.Presentation.ViewModels;
using Microsoft.Extensions.Logging;

namespace InvoicingTool.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Application en français : formats de dates et de montants cohérents quel que soit le téléphone.
		var culture = CultureInfo.GetCultureInfo("fr-FR");
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		var services = builder.Services;

		// Données locales (hors ligne)
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton(_ => new AppDatabase(Path.Combine(FileSystem.AppDataDirectory, "invoicing.db3")));
		services.AddSingleton<ClientRepository>();
		services.AddSingleton<DocumentRepository>();
		services.AddSingleton<CatalogRepository>();
		services.AddSingleton<SettingsRepository>();
		services.AddSingleton<DocumentService>();

		// Services d'interface
		services.AddSingleton<INavigationService, ShellNavigationService>();
		services.AddSingleton<IDialogService, DialogService>();
		services.AddSingleton<IShareService, ShareService>();

		// Écrans
		services.AddTransient<ClientsViewModel>().AddTransient<ClientsPage>();
		services.AddTransient<ClientEditViewModel>().AddTransient<ClientEditPage>();
		services.AddTransient<ClientPickerViewModel>().AddTransient<ClientPickerPage>();
		services.AddTransient<QuotesViewModel>().AddTransient<QuotesPage>();
		services.AddTransient<InvoicesViewModel>().AddTransient<InvoicesPage>();
		services.AddTransient<DocumentEditViewModel>().AddTransient<DocumentEditPage>();
		services.AddTransient<DocumentPreviewViewModel>().AddTransient<DocumentPreviewPage>();
		services.AddTransient<CatalogViewModel>().AddTransient<CatalogPage>();
		services.AddTransient<CatalogItemEditViewModel>().AddTransient<CatalogItemEditPage>();
		services.AddTransient<SettingsViewModel>().AddTransient<SettingsPage>();

		Routing.RegisterRoute(Routes.ClientEdit, typeof(ClientEditPage));
		Routing.RegisterRoute(Routes.ClientPicker, typeof(ClientPickerPage));
		Routing.RegisterRoute(Routes.DocumentEdit, typeof(DocumentEditPage));
		Routing.RegisterRoute(Routes.DocumentPreview, typeof(DocumentPreviewPage));
		Routing.RegisterRoute(Routes.CatalogItemEdit, typeof(CatalogItemEditPage));

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
