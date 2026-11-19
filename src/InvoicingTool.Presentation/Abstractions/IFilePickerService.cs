namespace InvoicingTool.Presentation.Abstractions;

public interface IFilePickerService
{
    /// <summary>Laisse l'utilisateur choisir un fichier de sauvegarde ; null s'il annule.</summary>
    Task<Stream?> PickBackupFileAsync();
}
