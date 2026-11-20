using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

/// <summary>Mode clair / sombre : préférence propre à l'appareil (non incluse dans la sauvegarde des données).</summary>
public sealed class ThemeService : IThemeService
{
	private const string PreferenceKey = "theme";

	public ThemeChoice Current =>
		Enum.TryParse<ThemeChoice>(Preferences.Default.Get(PreferenceKey, nameof(ThemeChoice.System)), out var choice)
			? choice
			: ThemeChoice.System;

	public void Apply(ThemeChoice choice)
	{
		Preferences.Default.Set(PreferenceKey, choice.ToString());
		ApplyToApplication(choice);
	}

	/// <summary>Au démarrage : réapplique le thème choisi.</summary>
	public void ApplySaved() => ApplyToApplication(Current);

	private static void ApplyToApplication(ThemeChoice choice)
	{
		if (Application.Current is not { } app)
			return;
		app.UserAppTheme = choice switch
		{
			ThemeChoice.Light => AppTheme.Light,
			ThemeChoice.Dark => AppTheme.Dark,
			_ => AppTheme.Unspecified,
		};
	}
}
