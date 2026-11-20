using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

public sealed class FilePickerService : IFilePickerService
{
	private static readonly FilePickerFileType BackupFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
	{
		[DevicePlatform.Android] = ["application/json", "application/octet-stream", "text/plain"],
		[DevicePlatform.iOS] = ["public.json", "public.text"],
		[DevicePlatform.WinUI] = [".json"],
	});

	public async Task<Stream?> PickBackupFileAsync()
	{
		var result = await FilePicker.Default.PickAsync(new PickOptions
		{
			PickerTitle = "Choisir un fichier de sauvegarde",
			FileTypes = BackupFileType,
		});
		return result is null ? null : await result.OpenReadAsync();
	}
}
