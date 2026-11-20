using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

/// <summary>
/// Mode clair / sombre propre à l'application : le thème est imposé à l'application et ne suit jamais
/// celui de Windows ou d'Android. Le réglage est enregistré dans les paramètres de l'application ;
/// une copie locale permet seulement de l'appliquer dès l'ouverture, sans attendre la base de données.
/// </summary>
public sealed class ThemeService : IThemeService
{
	private const string StartupCacheKey = "theme";

	public ThemeChoice Current { get; private set; } =
		Enum.TryParse<ThemeChoice>(Preferences.Default.Get(StartupCacheKey, nameof(ThemeChoice.Light)), out var cached)
			? cached
			: ThemeChoice.Light;

	public void Apply(ThemeChoice choice)
	{
		Current = choice;
		Preferences.Default.Set(StartupCacheKey, choice.ToString());
		ApplyToApplication();
	}

	/// <summary>À l'ouverture : thème de la dernière utilisation, avant même la lecture des paramètres.</summary>
	public void ApplyAtStartup() => ApplyToApplication();

	private void ApplyToApplication()
	{
		if (Application.Current is { } app)
			app.UserAppTheme = Current == ThemeChoice.Dark ? AppTheme.Dark : AppTheme.Light;
	}
}
