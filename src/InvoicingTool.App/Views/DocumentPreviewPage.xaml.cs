using System.ComponentModel;
using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class DocumentPreviewPage : BasePage
{
	public DocumentPreviewPage(DocumentPreviewViewModel viewModel) : base(viewModel)
	{
		InitializeComponent();
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(DocumentPreviewViewModel.Html) && sender is DocumentPreviewViewModel vm)
			Browser.Source = new HtmlWebViewSource { Html = vm.Html };
	}
}
