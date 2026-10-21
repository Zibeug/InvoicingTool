using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

/// <summary>Liste commune aux devis et aux factures.</summary>
public partial class DocumentsPage : BasePage
{
	protected DocumentsPage(DocumentsViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
	}
}

public sealed class QuotesPage(QuotesViewModel viewModel) : DocumentsPage(viewModel);

public sealed class InvoicesPage(InvoicesViewModel viewModel) : DocumentsPage(viewModel);
