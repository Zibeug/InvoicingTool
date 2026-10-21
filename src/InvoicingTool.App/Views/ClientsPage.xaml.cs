using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class ClientsPage : BasePage
{
	public ClientsPage(ClientsViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}
