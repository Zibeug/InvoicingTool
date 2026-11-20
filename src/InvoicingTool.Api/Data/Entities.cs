namespace InvoicingTool.Api.Data;

/// <summary>Compte utilisateur (une entreprise).</summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    /// <summary>E-mail en minuscules, unique.</summary>
    public required string NormalizedEmail { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Dernier numéro de version attribué aux données du compte (augmente à chaque envoi).</summary>
    public long SyncVersion { get; set; }

    public List<Device> Devices { get; set; } = [];
}

/// <summary>Appareil (téléphone, PC…) connecté au compte.</summary>
public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public required string Name { get; set; }

    /// <summary>Série de numérotation (null pour le premier appareil, puis B, C…).</summary>
    public string? SeriesLetter { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

/// <summary>Jeton de renouvellement : seule son empreinte SHA-256 est stockée.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid DeviceId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Dernier état connu d'un élément de l'application (client, facture…), stocké tel quel en JSON :
/// le serveur ne dépend pas de la structure des données de l'application.
/// </summary>
public class SyncRecord
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>Version attribuée par le serveur : sert à demander « ce qui a changé depuis ».</summary>
    public long Version { get; set; }

    /// <summary>Date de modification sur l'appareil (informative).</summary>
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Guid DeviceId { get; set; }
    public required string Payload { get; set; }
}
