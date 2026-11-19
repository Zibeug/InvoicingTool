using InvoicingTool.Presentation.ViewModels;

namespace InvoicingTool.App.Views;

public partial class SignaturePage : BasePage
{
	private readonly SignatureViewModel _viewModel;

	public SignaturePage(SignatureViewModel viewModel) : base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
	}

	private void OnClearClicked(object? sender, EventArgs e) => Pad.Clear();

	/// <summary>Le dessin est converti en PNG (fond transparent) puis transmis au ViewModel.</summary>
	private async void OnValidateClicked(object? sender, EventArgs e)
	{
		byte[]? png = null;
		if (Pad.Lines.Count > 0)
		{
			await using var stream = await Pad.GetImageStream(Pad.Width, Pad.Height);
			using var memory = new MemoryStream();
			await stream.CopyToAsync(memory);
			png = memory.ToArray();
		}
		await _viewModel.SaveAsync(png);
	}
}
