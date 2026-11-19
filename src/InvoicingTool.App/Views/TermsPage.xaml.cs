using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class TermsPage : BasePage
{
	private readonly TermsViewModel _viewModel;

	public TermsPage(TermsViewModel viewModel) : base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
	}

	// Le bouton retour d'Android ne permet pas de contourner l'acceptation.
	protected override bool OnBackButtonPressed() => _viewModel.MustAccept || base.OnBackButtonPressed();
}
