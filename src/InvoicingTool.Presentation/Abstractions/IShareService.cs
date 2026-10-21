namespace InvoicingTool.Presentation.Abstractions;

public interface IShareService
{
    /// <summary>Écrit le document dans un fichier et ouvre la feuille de partage du téléphone (e-mail, SMS, WhatsApp…).</summary>
    Task ShareHtmlAsync(string fileName, string html, string title);
}
