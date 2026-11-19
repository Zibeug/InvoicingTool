namespace InvoicingTool.Presentation.Abstractions;

/// <summary>Reconnaissance vocale du téléphone (de préférence sans connexion internet).</summary>
public interface ISpeechRecognizer
{
    /// <summary>Texte reconnu pendant que l'utilisateur parle (phrase en cours, provisoire).</summary>
    event EventHandler<string>? TextUpdated;

    /// <summary>Fin de l'écoute : texte final (null si rien n'a été compris ou en cas d'erreur).</summary>
    event EventHandler<string?>? Completed;

    /// <summary>Demande l'accès au micro puis démarre l'écoute ; renvoie un message d'erreur si impossible.</summary>
    Task<string?> StartAsync(string culture);

    Task StopAsync();
}
