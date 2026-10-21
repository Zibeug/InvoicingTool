using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>Compteur de numérotation par type de document et par année (numérotation continue).</summary>
public class NumberSequence
{
    /// <summary>Clé du compteur, ex. « Invoice-2026 ».</summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public int LastValue { get; set; }
}
