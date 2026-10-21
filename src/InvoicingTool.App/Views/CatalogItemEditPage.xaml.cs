using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class CatalogItemEditPage : BasePage
{
	public CatalogItemEditPage(CatalogItemEditViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}
