namespace InvoicingTool.Presentation.Abstractions;

public interface IImagePickerService
{
    /// <summary>Choix d'une image dans la galerie, réduite à une taille raisonnable (PNG) ; null si annulé.</summary>
    Task<byte[]?> PickLogoAsync();
}
