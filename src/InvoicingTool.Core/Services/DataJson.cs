using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvoicingTool.Core.Services;

/// <summary>Format JSON des données de l'application, commun à la sauvegarde et à la synchronisation.</summary>
public static class DataJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        // Les propriétés calculées (totaux formatés, adresse complète…) ne sont pas transmises.
        IgnoreReadOnlyProperties = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
