using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class DocumentEditPage : BasePage
{
	public DocumentEditPage(DocumentEditViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}
