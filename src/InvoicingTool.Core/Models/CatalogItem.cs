using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>Prestation ou fourniture réutilisable (ex. « Peinture murs, 2 couches » au m²).</summary>
public class CatalogItem : EntityBase
{
    [Indexed]
    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Unité : m², h, u, forfait, ml…</summary>
    public string Unit { get; set; } = "u";

    public decimal UnitPrice { get; set; }

    /// <summary>Taux de TVA en pourcentage (20, 10, 5.5, 0).</summary>
    public decimal VatRate { get; set; } = 10m;
}
