using InvoicingTool.Presentation.Abstractions;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

/// <summary>
/// Page reliée à son ViewModel : transmet les paramètres de navigation Shell
/// et les événements d'affichage / de sortie.
/// </summary>
public abstract class BasePage : ContentPage, IQueryAttributable
{
	protected BasePage(ViewModelBase viewModel)
	{
		ViewModel = viewModel;
		BindingContext = viewModel;
		this.SetBinding(TitleProperty, static (ViewModelBase vm) => vm.Title);
	}

	protected ViewModelBase ViewModel { get; }

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (ViewModel is IParameterReceiver receiver)
			receiver.ApplyParameters(query);
	}

	/// <summary>Recharge l'écran (ex. après une synchronisation en arrière-plan qui a reçu des modifications).</summary>
	public Task RefreshAsync() => ViewModel.OnAppearingAsync();

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			await ViewModel.OnAppearingAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erreur", ex.Message, "OK");
		}
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();
		try
		{
			await ViewModel.OnDisappearingAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erreur", ex.Message, "OK");
		}
	}
}
