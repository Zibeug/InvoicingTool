using System.Globalization;
using CommunityToolkit.Maui.Media;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

/// <summary>
/// Dictée avec la reconnaissance vocale du téléphone (CommunityToolkit.Maui).
/// La reconnaissance hors ligne est essayée d'abord (utilisable sans réseau) ; si le téléphone
/// ne la propose pas, la reconnaissance standard du système prend le relais.
/// </summary>
public sealed class SpeechRecognizer : ISpeechRecognizer
{
	private ISpeechToText? _active;

	public event EventHandler<string>? TextUpdated;
	public event EventHandler<string?>? Completed;

	public async Task<string?> StartAsync(string culture)
	{
		foreach (var engine in new[] { OfflineSpeechToText.Default, SpeechToText.Default })
		{
			try
			{
				if (!await engine.RequestPermissions(CancellationToken.None))
					return "L'accès au micro a été refusé. Autorisez-le dans les réglages du téléphone, " +
					       "ou utilisez le micro du clavier pour dicter dans la zone de texte.";

				Attach(engine);
				await engine.StartListenAsync(new SpeechToTextOptions
				{
					Culture = CultureInfo.GetCultureInfo(culture),
					ShouldReportPartialResults = true,
				}, CancellationToken.None);
				return null;
			}
			catch (Exception)
			{
				Detach();
				// Moteur indisponible sur ce téléphone : on essaie le suivant.
			}
		}
		return "La reconnaissance vocale n'est pas disponible sur ce téléphone. " +
		       "Utilisez le micro du clavier pour dicter dans la zone de texte.";
	}

	public async Task StopAsync()
	{
		if (_active is { } engine)
			await engine.StopListenAsync(CancellationToken.None);
	}

	private void Attach(ISpeechToText engine)
	{
		Detach();
		_active = engine;
		engine.RecognitionResultUpdated += OnUpdated;
		engine.RecognitionResultCompleted += OnCompleted;
	}

	private void Detach()
	{
		if (_active is null)
			return;
		_active.RecognitionResultUpdated -= OnUpdated;
		_active.RecognitionResultCompleted -= OnCompleted;
		_active = null;
	}

	// Les résultats arrivent hors du fil d'interface : on y revient avant de mettre l'écran à jour.
	private void OnUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(() => TextUpdated?.Invoke(this, e.RecognitionResult));

	private void OnCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(() =>
		{
			Completed?.Invoke(this, e.RecognitionResult.IsSuccessful ? e.RecognitionResult.Text : null);
			Detach();
		});
}
