namespace InvoicingTool.Presentation.Abstractions;

public enum ThemeChoice
{
    /// <summary>Suit le réglage du téléphone ou de Windows.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Thème de l'application, mémorisé sur l'appareil.</summary>
public interface IThemeService
{
    ThemeChoice Current { get; }

    /// <summary>Applique le thème immédiatement et le mémorise.</summary>
    void Apply(ThemeChoice choice);
}
