namespace InvoicingTool.Presentation.Abstractions;

public interface IShareService
{
    /// <summary>Écrit le fichier puis ouvre la feuille de partage du téléphone (e-mail, SMS, WhatsApp…).</summary>
    Task ShareFileAsync(string fileName, byte[] content, string contentType, string title);

    /// <summary>Écrit le fichier puis l'ouvre dans l'application par défaut (lecteur PDF, impression…).</summary>
    Task OpenFileAsync(string fileName, byte[] content, string contentType, string title);
}
