namespace InvoicingTool.Presentation.Abstractions;

/// <summary>Thème propre à l'application, indépendant du réglage de Windows ou d'Android.</summary>
public enum ThemeChoice
{
    Light,
    Dark,
}

/// <summary>Applique le thème choisi dans l'application.</summary>
public interface IThemeService
{
    ThemeChoice Current { get; }

    /// <summary>Applique le thème immédiatement (le réglage enregistré fait foi : <c>CompanySettings.AppTheme</c>).</summary>
    void Apply(ThemeChoice choice);
}
