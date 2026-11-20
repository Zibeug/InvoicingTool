using InvoicingTool.Presentation.Abstractions;
#if WINDOWS
using CommunityToolkit.Maui.Storage;
#endif

namespace InvoicingTool.App.Services;

public sealed class ShareService : IShareService
{
	public async Task ShareFileAsync(string fileName, byte[] content, string contentType, string title)
	{
#if WINDOWS
		// Sur ordinateur : boîte « Enregistrer sous » plutôt que la feuille de partage des téléphones.
		using var stream = new MemoryStream(content);
		var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
		if (!result.IsSuccessful && result.Exception is not null and not OperationCanceledException)
			throw new InvalidOperationException($"Impossible d'enregistrer le fichier : {result.Exception.Message}");
		return;
#else
		var path = await WriteAsync(fileName, content);
		await Share.Default.RequestAsync(new ShareFileRequest
		{
			Title = title,
			File = new ShareFile(path, contentType),
		});
#endif
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
