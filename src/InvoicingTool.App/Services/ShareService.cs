using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

public sealed class ShareService : IShareService
{
	public async Task ShareFileAsync(string fileName, byte[] content, string contentType, string title)
	{
		var path = await WriteAsync(fileName, content);
		await Share.Default.RequestAsync(new ShareFileRequest
		{
			Title = title,
			File = new ShareFile(path, contentType),
		});
	}

	public async Task OpenFileAsync(string fileName, byte[] content, string contentType, string title)
	{
		var path = await WriteAsync(fileName, content);
		await Launcher.Default.OpenAsync(new OpenFileRequest(title, new ReadOnlyFile(path, contentType)));
	}

	/// <summary>Fichiers écrits dans le cache : régénérés à chaque export, nettoyés par le système si besoin.</summary>
	private static async Task<string> WriteAsync(string fileName, byte[] content)
	{
		var directory = Path.Combine(FileSystem.CacheDirectory, "exports");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, fileName);
		await File.WriteAllBytesAsync(path, content);
		return path;
	}
}
