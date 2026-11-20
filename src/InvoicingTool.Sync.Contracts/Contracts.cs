using System.Text.Json;

namespace InvoicingTool.Sync.Contracts;

/// <summary>Types d'éléments synchronisés. Le serveur n'accepte que ceux-ci.</summary>
public static class SyncEntityTypes
{
    public const string Client = "client";
    public const string CatalogItem = "catalogItem";
    public const string Document = "document";
    public const string DocumentLine = "documentLine";
    public const string Signature = "signature";
    public const string Reminder = "reminder";
    public const string Settings = "settings";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Client, CatalogItem, Document, DocumentLine, Signature, Reminder, Settings };
}

public static class SyncLimits
{
    /// <summary>Nombre maximal d'éléments par envoi / par réception.</summary>
    public const int MaxBatchSize = 500;

    /// <summary>Taille maximale d'un élément (logo, signature compris).</summary>
    public const int MaxPayloadBytes = 2 * 1024 * 1024;

    public const int MinPasswordLength = 8;
}

/// <param name="DeviceId">Identifiant d'un appareil déjà connu du compte (reconnexion), sinon null.</param>
public sealed record RegisterRequest(string Email, string Password, string DeviceName);

public sealed record LoginRequest(string Email, string Password, string DeviceName, Guid? DeviceId = null);

public sealed record RefreshRequest(string RefreshToken);

/// <param name="SeriesLetter">
/// Lettre de la série de numérotation de cet appareil (null pour le premier appareil du compte) :
/// les factures émises sur des appareils différents ne peuvent jamais avoir le même numéro.
/// </param>
public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    Guid AccountId,
    Guid DeviceId,
    string? SeriesLetter);

/// <summary>Un élément modifié (création, modification ou suppression logique), au format JSON de l'application.</summary>
/// <param name="Version">Attribuée par le serveur (0 à l'envoi).</param>
public sealed record SyncChange(
    string EntityType,
    Guid EntityId,
    DateTime UpdatedAt,
    bool IsDeleted,
    JsonElement Payload,
    long Version = 0);

public sealed record PushRequest(IReadOnlyList<SyncChange> Changes);

/// <param name="Version">Version du compte après cet envoi.</param>
public sealed record PushResponse(int Accepted, long Version);

/// <param name="Version">Version à redemander ensuite (« since »).</param>
/// <param name="HasMore">D'autres modifications attendent : rappeler immédiatement.</param>
public sealed record PullResponse(IReadOnlyList<SyncChange> Changes, long Version, bool HasMore);
