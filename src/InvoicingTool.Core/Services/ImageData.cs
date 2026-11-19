namespace InvoicingTool.Core.Services;

/// <summary>Détection du format d'une image (logo, signature) à partir de ses premiers octets.</summary>
public static class ImageData
{
    public static string? MimeType(byte[]? image) => image switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        _ => null,
    };

    public static bool IsSupported(byte[]? image) => MimeType(image) is not null;

    public static string ToDataUri(byte[] image) => $"data:{MimeType(image)};base64,{Convert.ToBase64String(image)}";

    /// <summary>Couleur « #RRGGBB » valide, sinon la couleur par défaut.</summary>
    public static string NormalizeColor(string? hex, string fallback)
    {
        var value = (hex ?? string.Empty).Trim().TrimStart('#');
        return value.Length == 6 && value.All(Uri.IsHexDigit) ? "#" + value.ToUpperInvariant() : fallback;
    }
}
