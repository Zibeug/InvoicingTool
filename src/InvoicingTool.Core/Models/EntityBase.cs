using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>
/// Base de toutes les entités persistées.
/// Les champs Id (GUID), UpdatedAt et IsDeleted préparent la future synchronisation :
/// un identifiant global évite les collisions entre appareils, la date de modification
/// permet de détecter les changements et la suppression logique permet de propager les suppressions.
/// </summary>
public abstract class EntityBase
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public bool IsDeleted { get; set; }

    /// <summary>Vrai tant que la modification locale n'a pas été envoyée à un serveur.</summary>
    [Indexed]
    public bool IsDirty { get; set; } = true;
}
