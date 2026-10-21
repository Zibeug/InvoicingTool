using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

public sealed class ShareService : IShareService
{
	public async Task ShareHtmlAsync(string fileName, string html, string title)
	{
		var path = Path.Combine(FileSystem.CacheDirectory, fileName);
		await File.WriteAllTextAsync(path, html);
		await Share.Default.RequestAsync(new ShareFileRequest
		{
			Title = title,
			File = new ShareFile(path, "text/html"),
		});
	}
}
