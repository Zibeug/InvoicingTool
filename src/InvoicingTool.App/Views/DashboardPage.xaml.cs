using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class DashboardPage : BasePage
{
	public DashboardPage(DashboardViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}
