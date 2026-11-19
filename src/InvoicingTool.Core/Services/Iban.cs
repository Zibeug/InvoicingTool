using System.Text;

namespace InvoicingTool.Core.Services;

/// <summary>Vérification (clé ISO 13616, modulo 97) et mise en forme des IBAN.</summary>
public static class Iban
{
    /// <summary>Supprime espaces et tirets, met en majuscules.</summary>
    public static string Normalize(string? text) =>
        new((text ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValid(string? text)
    {
        var iban = Normalize(text);
        if (iban.Length is < 15 or > 34 || !char.IsLetter(iban[0]) || !char.IsLetter(iban[1]) ||
            !char.IsDigit(iban[2]) || !char.IsDigit(iban[3]) || !iban.All(char.IsAsciiLetterOrDigit))
            return false;

        // Les 4 premiers caractères passent à la fin, les lettres deviennent des nombres (A = 10…), reste modulo 97 = 1.
        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var value = char.IsDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }
        return remainder == 1;
    }

    /// <summary>Format d'affichage par groupes de 4 : « CH93 0076 2011 6238 5295 7 ».</summary>
    public static string Format(string? text)
    {
        var iban = Normalize(text);
        var sb = new StringBuilder();
        for (var i = 0; i < iban.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                sb.Append(' ');
            sb.Append(iban[i]);
        }
        return sb.ToString();
    }
}
