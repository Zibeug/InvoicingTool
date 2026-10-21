using System.Globalization;

namespace InvoicingTool.Presentation;

/// <summary>Saisie de nombres tolérante : accepte « 12,5 » comme « 12.5 » quel que soit le clavier.</summary>
public static class DecimalParser
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var normalized = text.Trim()
            .Replace("€", "").Replace("%", "")
            .Replace(" ", "").Replace(" ", "").Replace(" ", "")
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static decimal ParseOrZero(string? text) => TryParse(text, out var value) ? value : 0m;

    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));
}
