using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

public sealed class DialogService : IDialogService
{
	private static Page CurrentPage => Shell.Current.CurrentPage ?? Shell.Current;

	public Task AlertAsync(string title, string message) =>
		MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayAlertAsync(title, message, "OK"));

	public Task<bool> ConfirmAsync(string title, string message, string accept = "Oui", string cancel = "Non") =>
		MainThread.InvokeOnMainThreadAsync(() => CurrentPage.DisplayAlertAsync(title, message, accept, cancel));

	public async Task<string?> ChooseAsync(string title, IReadOnlyList<string> options, string cancel = "Annuler")
	{
		var choice = await MainThread.InvokeOnMainThreadAsync(
			() => CurrentPage.DisplayActionSheetAsync(title, cancel, null, options.ToArray()));
		return choice == cancel ? null : choice;
	}
}
