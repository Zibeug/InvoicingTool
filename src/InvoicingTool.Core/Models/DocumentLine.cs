using SQLite;

namespace InvoicingTool.Core.Models;

public class DocumentLine : EntityBase
{
    [Indexed]
    public Guid DocumentId { get; set; }

    public int Position { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public string Unit { get; set; } = "u";

    /// <summary>Prix unitaire hors taxes.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Taux de TVA en pourcentage.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Remise sur la ligne, en pourcentage.</summary>
    public decimal DiscountPercent { get; set; }

    [Ignore]
    public decimal TotalExclTax => Services.DocumentCalculator.LineTotalExclTax(this);

    public DocumentLine Clone(Guid newDocumentId) => new()
    {
        DocumentId = newDocumentId,
        Position = Position,
        Description = Description,
        Quantity = Quantity,
        Unit = Unit,
        UnitPrice = UnitPrice,
        VatRate = VatRate,
        DiscountPercent = DiscountPercent,
    };
}
