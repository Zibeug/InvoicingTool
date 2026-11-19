using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>Signature du client sur un devis, faite au doigt sur l'écran du téléphone.</summary>
/// <remarks>Table séparée : les listes de documents ne chargent pas les images.</remarks>
public class DocumentSignature : EntityBase
{
    [Indexed]
    public Guid DocumentId { get; set; }

    /// <summary>Image PNG de la signature.</summary>
    public byte[] Image { get; set; } = [];

    public string SignerName { get; set; } = string.Empty;

    public DateTime SignedAt { get; set; }
}
