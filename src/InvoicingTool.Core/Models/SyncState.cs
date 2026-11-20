using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>
/// État de la synchronisation de CET appareil : propre à l'appareil, jamais synchronisé ni sauvegardé.
/// Les jetons de connexion sont gardés à part, dans le stockage sécurisé du système.
/// </summary>
public class SyncState
{
    public const int SingletonId = 1;

    [PrimaryKey]
    public int Id { get; set; } = SingletonId;

    /// <summary>Vrai quand l'appareil est connecté à un compte de synchronisation.</summary>
    public bool IsEnabled { get; set; }

    public string? ServerUrl { get; set; }
    public string? Email { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }

    /// <summary>Lettre de la série de numérotation de cet appareil (null = série principale).</summary>
    public string? SeriesLetter { get; set; }

    /// <summary>Dernière version reçue du serveur : la prochaine réception demande « ce qui a changé depuis ».</summary>
    public long LastPulledVersion { get; set; }

    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
}
