using InvoicingTool.Presentation.Abstractions;
#if ANDROID || IOS
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;
#endif

namespace InvoicingTool.App.Services;

public sealed class ImagePickerService : IImagePickerService
{
	/// <summary>Taille maximale du logo : suffisant pour l'impression, léger pour la base et les sauvegardes.</summary>
	private const float MaxSize = 600;

	public async Task<byte[]?> PickLogoAsync()
	{
		var result = await FilePicker.Default.PickAsync(new PickOptions
		{
			PickerTitle = "Choisir le logo",
			FileTypes = FilePickerFileType.Images,
		});
		if (result is null)
			return null;

		await using var stream = await result.OpenReadAsync();
#if ANDROID || IOS
		// Réduit l'image et la convertit en PNG (garde la transparence des logos).
		IImage image = PlatformImage.FromStream(stream);
		if (image.Width > MaxSize || image.Height > MaxSize)
			image = image.Downsize(MaxSize, disposeOriginal: true);
		using var output = new MemoryStream();
		await image.SaveAsync(output, ImageFormat.Png);
		image.Dispose();
		return output.ToArray();
#else
		using var output = new MemoryStream();
		await stream.CopyToAsync(output);
		return output.ToArray();
#endif
	}
}
