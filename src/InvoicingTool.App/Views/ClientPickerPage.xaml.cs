using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class ClientPickerPage : BasePage
{
	public ClientPickerPage(ClientPickerViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}
