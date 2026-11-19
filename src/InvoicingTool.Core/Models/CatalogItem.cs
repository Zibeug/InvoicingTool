using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>Article réutilisable : produit ou service vendu régulièrement (ex. « Heure de conseil », « Coupe homme »).</summary>
public class CatalogItem : EntityBase
{
    [Indexed]
    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Unité : u, h, jour, forfait…</summary>
    public string Unit { get; set; } = "u";

    public decimal UnitPrice { get; set; }

    /// <summary>Taux de TVA en pourcentage (20, 10, 5.5, 0).</summary>
    public decimal VatRate { get; set; } = 10m;
}
