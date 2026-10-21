using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class ClientEditPage : BasePage
{
	public ClientEditPage(ClientEditViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}
