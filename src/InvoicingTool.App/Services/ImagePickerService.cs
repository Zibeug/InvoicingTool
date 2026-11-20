using InvoicingTool.Presentation.Abstractions;
#if ANDROID || IOS
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;
#elif WINDOWS
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
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
#elif WINDOWS
		return await DownsizeOnWindowsAsync(stream);
#else
		using var output = new MemoryStream();
		await stream.CopyToAsync(output);
		return output.ToArray();
#endif
	}

#if WINDOWS
	/// <summary>Réduction et conversion en PNG avec les API d'image de Windows (transparence et orientation conservées).</summary>
	private static async Task<byte[]> DownsizeOnWindowsAsync(Stream stream)
	{
		// Le décodeur Windows a besoin d'un flux positionnable : copie en mémoire.
		var copy = new MemoryStream();
		await stream.CopyToAsync(copy);
		copy.Position = 0;
		using var source = copy.AsRandomAccessStream();

		var decoder = await BitmapDecoder.CreateAsync(source);
		var scale = Math.Min(1.0, MaxSize / Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
		var width = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelWidth * scale));
		var height = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelHeight * scale));
		var pixels = await decoder.GetPixelDataAsync(
			BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
			new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
			ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

		using var output = new InMemoryRandomAccessStream();
		var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
		encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels.DetachPixelData());
		await encoder.FlushAsync();

		var bytes = new byte[output.Size];
		using var reader = new DataReader(output.GetInputStreamAt(0));
		await reader.LoadAsync((uint)output.Size);
		reader.ReadBytes(bytes);
		return bytes;
	}
#endif
}
