using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>Ligne comprise dans une dictée. <see cref="UnitPrice"/> est null si aucun prix n'a été dit ni trouvé au catalogue.</summary>
public sealed record DictatedLine(
    string Description, decimal Quantity, string Unit, decimal? UnitPrice, decimal? VatRate = null, CatalogItem? CatalogItem = null);

/// <summary>
/// Transforme une phrase dictée en français en lignes de devis / facture.
/// Exemple : « 3 heures de conseil à 120 francs, puis un forfait déplacement à 50 francs ».
/// Comprend les nombres en chiffres ou en lettres (y compris septante, huitante, nonante),
/// les unités courantes, le prix unitaire (« à 120 francs l'heure ») ou total (« pour 300 euros »)
/// et reconnaît les articles du catalogue.
/// </summary>
public static class DictationParser
{
    private static readonly Regex SegmentSeparators = new(
        @"\b(?:et\s+puis|puis|ensuite|nouvelle\s+ligne|ligne\s+suivante|et\s+aussi)\b|[;\n]|,(?=\s)|\.(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> SmallNumbers = new()
    {
        ["zéro"] = 0, ["zero"] = 0, ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5,
        ["six"] = 6, ["sept"] = 7, ["huit"] = 8, ["neuf"] = 9, ["dix"] = 10, ["onze"] = 11, ["douze"] = 12,
        ["treize"] = 13, ["quatorze"] = 14, ["quinze"] = 15, ["seize"] = 16,
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["vingt"] = 20, ["vingts"] = 20, ["trente"] = 30, ["quarante"] = 40, ["cinquante"] = 50, ["soixante"] = 60,
        ["septante"] = 70, ["huitante"] = 80, ["octante"] = 80, ["nonante"] = 90,
    };

    /// <summary>Mot (minuscule) → unité imprimée.</summary>
    private static readonly Dictionary<string, string> Units = new()
    {
        ["heure"] = "h", ["heures"] = "h", ["h"] = "h",
        ["minute"] = "min", ["minutes"] = "min", ["min"] = "min",
        ["jour"] = "jour", ["jours"] = "jour", ["journée"] = "jour", ["journées"] = "jour", ["journee"] = "jour", ["journees"] = "jour",
        ["semaine"] = "semaine", ["semaines"] = "semaine",
        ["mois"] = "mois",
        ["nuit"] = "nuit", ["nuits"] = "nuit",
        ["séance"] = "séance", ["séances"] = "séance", ["seance"] = "séance", ["seances"] = "séance",
        ["m²"] = "m²", ["m2"] = "m²",
        ["mètre"] = "m", ["mètres"] = "m", ["metre"] = "m", ["metres"] = "m", ["m"] = "m",
        ["kilomètre"] = "km", ["kilomètres"] = "km", ["kilometre"] = "km", ["kilometres"] = "km", ["km"] = "km",
        ["kilo"] = "kg", ["kilos"] = "kg", ["kilogramme"] = "kg", ["kilogrammes"] = "kg", ["kg"] = "kg",
        ["litre"] = "L", ["litres"] = "L",
        ["pièce"] = "pièce", ["pièces"] = "pièce", ["piece"] = "pièce", ["pieces"] = "pièce",
        ["unité"] = "u", ["unités"] = "u", ["unite"] = "u", ["unites"] = "u",
        ["forfait"] = "forfait", ["forfaits"] = "forfait",
    };

    private static readonly HashSet<string> CurrencyWords =
        ["euro", "euros", "€", "franc", "francs", "chf", "fr", "fr.", "dollar", "dollars", "$", "balles"];

    private static readonly HashSet<string> TaxWords = ["ht", "ttc", "hors", "taxe", "taxes", "net"];

    private static readonly HashSet<string> LeadingFillers =
        ["de", "d'", "des", "du", "la", "le", "les", "l'", "pour", "et", "avec", "x", "fois"];

    private static readonly HashSet<string> TrailingFillers =
        ["à", "a", "et", "pour", "de", "d'", "au", "le", "la", "les", "du", "des", "prix", "soit", "chacun", "chacune"];

    private static readonly HashSet<string> StopWords =
        ["de", "des", "du", "la", "le", "les", "et", "pour", "avec", "un", "une", "en", "sur", "aux", "au"];

    public static IReadOnlyList<DictatedLine> Parse(string? text, IReadOnlyList<CatalogItem>? catalog = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = new List<DictatedLine>();
        foreach (var segment in SegmentSeparators.Split(Prepare(text)))
        {
            var line = ParseSegment(segment, catalog ?? []);
            if (line is not null)
                lines.Add(line);
        }
        return lines;
    }

    /// <summary>Uniformise symboles et écritures (€, m², apostrophes, « mètres carrés »…).</summary>
    private static string Prepare(string text)
    {
        var s = text.Replace('’', '\'').Replace(' ', ' ').Replace(' ', ' ');
        s = Regex.Replace(s, @"(\d)\s*€", "$1 euros");
        s = s.Replace("€", " euros ");
        s = Regex.Replace(s, @"(\d)\s*(?:chf|CHF)\b", "$1 francs");
        s = Regex.Replace(s, @"\bm[èe]tres?\s+carr[ée]s?\b|\bm2\b|\bm²", "m²", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bm[èe]tres?\s+lin[ée]aires?\b", "ml", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(\d)'(\d{3})", "$1$2"); // 1'200 → 1200
        return s;
    }

    private sealed class Token(string text)
    {
        public string Text { get; } = text;
        public string Key { get; } = text.ToLowerInvariant().Trim(',', ':', '!', '?', '"', '«', '»', '(', ')');
        public bool Used { get; set; }
    }

    private static DictatedLine? ParseSegment(string segment, IReadOnlyList<CatalogItem> catalog)
    {
        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => new Token(t))
            .Where(t => t.Key.Length > 0)
            .ToList();
        if (tokens.Count == 0)
            return null;

        // 1. Prix
        var (price, isTotal, priceUnit) = ExtractPrice(tokens);

        // 2. Quantité et unité
        decimal? quantity = null;
        string? unit = null;
        ExtractQuantity(tokens, ref quantity, ref unit);

        unit ??= priceUnit;
        if (unit is null && tokens.FirstOrDefault(t => !t.Used && t.Key is "forfait" or "forfaits") is { } forfait)
        {
            unit = "forfait";
            forfait.Used = true;
        }
        var qty = quantity is > 0 ? quantity.Value : 1m;
        var explicitUnit = unit;
        unit ??= "u";

        // 3. Désignation : ce qui reste, sans les mots de liaison en début et en fin
        var description = Description(tokens);

        decimal? unitPrice = price is null ? null : isTotal ? DocumentCalculator.Round(price.Value / qty) : price;

        // 4. Article du catalogue
        var item = MatchCatalog(description, catalog);
        if (item is not null)
        {
            description = string.IsNullOrWhiteSpace(item.Description) ? item.Label : $"{item.Label}\n{item.Description}";
            unitPrice ??= item.UnitPrice;
            if (explicitUnit is null)
                unit = item.Unit;
        }

        if (description.Length == 0 && unitPrice is null)
            return null;
        return new DictatedLine(description, qty, unit, unitPrice, item?.VatRate, item);
    }

    private static (decimal? Price, bool IsTotal, string? PerUnit) ExtractPrice(List<Token> tokens)
    {
        // Priorité à un nombre suivi d'une devise, sinon « à 120 » / « pour 120 ».
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Used || !TryReadNumber(tokens, i, allowArticle: false, out var value, out var length))
                    continue;
                var after = i + length;
                var hasCurrency = after < tokens.Count && CurrencyWords.Contains(tokens[after].Key);
                var marker = i > 0 ? tokens[i - 1].Key : null;
                var isMarked = marker is "à" or "a" or "pour" or "prix" or "soit" or "total";
                if (pass == 0 && !hasCurrency)
                    continue;
                if (pass == 1 && (!isMarked || (after < tokens.Count && IsUnitWord(tokens, after))))
                    continue;

                for (var k = i; k < after; k++)
                    tokens[k].Used = true;
                var isTotal = marker is "pour" or "soit" or "total";
                if (isMarked)
                    tokens[i - 1].Used = true;
                if (i > 2 && tokens[i - 1].Key == "de" && tokens[i - 2].Key == "prix" && tokens[i - 3].Key == "au")
                    tokens[i - 1].Used = tokens[i - 2].Used = tokens[i - 3].Used = true;
                else if (i > 1 && tokens[i - 1].Key == "prix" && tokens[i - 2].Key == "au")
                    tokens[i - 2].Used = true;

                if (hasCurrency)
                {
                    tokens[after].Used = true;
                    after++;
                    // « 12 euros 50 » → 12,50
                    if (value == decimal.Truncate(value) && after < tokens.Count &&
                        TryReadNumber(tokens, after, allowArticle: false, out var cents, out var centsLength) &&
                        cents is > 0 and < 100 && cents == decimal.Truncate(cents) &&
                        (after + centsLength >= tokens.Count || !IsUnitWord(tokens, after + centsLength)))
                    {
                        value += cents / 100m;
                        for (var k = after; k < after + centsLength; k++)
                            tokens[k].Used = true;
                        after += centsLength;
                        if (after < tokens.Count && tokens[after].Key is "centime" or "centimes")
                            tokens[after++].Used = true;
                    }
                }

                // « HT », « hors taxes », « au total », « en tout », « l'heure », « le m² », « chacun »…
                string? perUnit = null;
                while (after < tokens.Count)
                {
                    var key = tokens[after].Key;
                    if (TaxWords.Contains(key) || key is "chacun" or "chacune")
                    {
                        tokens[after++].Used = true;
                    }
                    else if (key is "pièce" or "piece" or "l'unité" or "l'unite")
                    {
                        perUnit = key.StartsWith("l'") ? "u" : "pièce";
                        tokens[after++].Used = true;
                    }
                    else if (key is "au" or "en" or "le" && after + 1 < tokens.Count && tokens[after + 1].Key is "total" or "tout")
                    {
                        tokens[after].Used = tokens[after + 1].Used = true;
                        after += 2;
                        isTotal = true;
                    }
                    else if (key.StartsWith("l'") && Units.TryGetValue(key[2..], out var elided))
                    {
                        perUnit = elided;
                        tokens[after++].Used = true;
                    }
                    else if (key is "le" or "la" or "par" or "du" or "de" or "/" && after + 1 < tokens.Count &&
                             (Units.TryGetValue(tokens[after + 1].Key, out var u) ||
                              (tokens[after + 1].Key.StartsWith("l'") && Units.TryGetValue(tokens[after + 1].Key[2..], out u))))
                    {
                        perUnit = u;
                        tokens[after].Used = tokens[after + 1].Used = true;
                        after += 2;
                    }
                    else
                    {
                        break;
                    }
                }
                return (value, isTotal, perUnit);
            }
        }
        return (null, false, null);
    }

    private static void ExtractQuantity(List<Token> tokens, ref decimal? quantity, ref string? unit)
    {
        // « une demi-heure », « une demi-journée »
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].Used && tokens[i].Key.StartsWith("demi-") && Units.TryGetValue(tokens[i].Key[5..], out var halfUnit))
            {
                quantity = 0.5m;
                unit = halfUnit;
                tokens[i].Used = true;
                if (i > 0 && tokens[i - 1].Key is "une" or "un")
                    tokens[i - 1].Used = true;
                return;
            }
        }

        var firstFree = tokens.FindIndex(t => !t.Used && !LeadingFillers.Contains(t.Key));
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Used)
                continue;

            if (!TryReadNumber(tokens, i, allowArticle: i == firstFree, out var value, out var length, allowArticleBeforeUnit: true))
                continue;
            var after = i + length;
            var hasUnit = after < tokens.Count && IsUnitWord(tokens, after);
            // Un nombre au milieu de la désignation (« peinture 2 couches ») reste dans le texte.
            if (!hasUnit && i != firstFree)
                continue;

            for (var k = i; k < after; k++)
                tokens[k].Used = true;
            quantity = value;
            if (after < tokens.Count && tokens[after].Key is "x" or "fois")
                tokens[after++].Used = true;
            if (hasUnit)
            {
                unit = Units[tokens[after].Key];
                tokens[after].Used = true;
                after++;
                // « 2 heures et demie »
                if (after + 1 < tokens.Count && tokens[after].Key == "et" && tokens[after + 1].Key is "demi" or "demie")
                {
                    quantity += 0.5m;
                    tokens[after].Used = tokens[after + 1].Used = true;
                }
            }
            return;
        }
    }

    private static bool IsUnitWord(List<Token> tokens, int index) => Units.ContainsKey(tokens[index].Key);

    /// <summary>Lit un nombre en chiffres (« 12,50 », « 1 200 ») ou en lettres (« quatre-vingt-dix », « deux et demi »).</summary>
    private static bool TryReadNumber(List<Token> tokens, int index, bool allowArticle, out decimal value, out int length,
        bool allowArticleBeforeUnit = false)
    {
        value = 0;
        length = 0;
        var key = tokens[index].Key;

        if (Regex.IsMatch(key, @"^\d+(?:[.,]\d+)?$"))
        {
            value = decimal.Parse(key.Replace(',', '.'), CultureInfo.InvariantCulture);
            length = 1;
            // « 1 200 » : milliers séparés par un espace
            if (!key.Contains(',') && !key.Contains('.') && key.Length <= 3 && index + 1 < tokens.Count &&
                Regex.IsMatch(tokens[index + 1].Key, @"^\d{3}(?:[.,]\d+)?$"))
            {
                value = value * 1000 + decimal.Parse(tokens[index + 1].Key.Replace(',', '.'), CultureInfo.InvariantCulture);
                length = 2;
            }
        }
        else
        {
            var words = new List<string>();
            var i = index;
            while (i < tokens.Count && IsNumberWord(tokens, i))
            {
                words.AddRange(tokens[i].Key.Split('-', StringSplitOptions.RemoveEmptyEntries));
                i++;
            }
            if (words.Count == 0)
                return false;
            // « un » / « une » seuls sont souvent des articles (« une fuite ») : nombre seulement en tête ou devant une unité.
            if (words is ["un"] or ["une"])
            {
                var beforeUnit = allowArticleBeforeUnit && i < tokens.Count && IsUnitWord(tokens, i);
                if (!allowArticle && !beforeUnit)
                    return false;
            }
            value = WordsToNumber(words);
            length = i - index;
        }

        // « 12 virgule 5 »
        if (index + length + 1 < tokens.Count && tokens[index + length].Key == "virgule" &&
            Regex.IsMatch(tokens[index + length + 1].Key, @"^\d+$"))
        {
            var decimals = tokens[index + length + 1].Key;
            value += decimal.Parse(decimals, CultureInfo.InvariantCulture) / (decimal)Math.Pow(10, decimals.Length);
            length += 2;
        }
        // « deux et demi »
        if (index + length + 1 < tokens.Count && tokens[index + length].Key == "et" &&
            tokens[index + length + 1].Key is "demi" or "demie")
        {
            value += 0.5m;
            length += 2;
        }
        return true;
    }

    private static bool IsNumberWord(List<Token> tokens, int i)
    {
        var parts = tokens[i].Key.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0] != "et" &&
            parts.All(p => SmallNumbers.ContainsKey(p) || Tens.ContainsKey(p) || p is "cent" or "cents" or "mille" or "et"))
            return true;
        // « vingt et un »
        return tokens[i].Key == "et" && i > 0 && i + 1 < tokens.Count &&
               Tens.ContainsKey(tokens[i - 1].Key.Split('-')[^1]) && tokens[i + 1].Key is "un" or "une" or "onze";
    }

    private static decimal WordsToNumber(List<string> words)
    {
        long total = 0, current = 0;
        foreach (var w in words)
        {
            if (w == "et")
                continue;
            if (w is "vingt" or "vingts" && current % 100 == 4)
                current += 76; // quatre-vingt
            else if (SmallNumbers.TryGetValue(w, out var small))
                current += small;
            else if (Tens.TryGetValue(w, out var ten))
                current += ten;
            else if (w is "cent" or "cents")
                current = (current == 0 ? 1 : current) * 100;
            else if (w == "mille")
            {
                total += (current == 0 ? 1 : current) * 1000;
                current = 0;
            }
        }
        return total + current;
    }

    private static string Description(List<Token> tokens)
    {
        var words = tokens.Where(t => !t.Used).Select(t => t.Text.Trim(',', ':', ';')).Where(w => w.Length > 0).ToList();
        while (words.Count > 0 && LeadingFillers.Contains(words[0].ToLowerInvariant()))
            words.RemoveAt(0);
        while (words.Count > 0 && TrailingFillers.Contains(words[^1].ToLowerInvariant()))
            words.RemoveAt(words.Count - 1);
        if (words.Count > 0 && Regex.IsMatch(words[0], "^[dl]'", RegexOptions.IgnoreCase))
            words[0] = words[0][2..];

        var text = string.Join(' ', words).Trim();
        return text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("fr-FR")) + text[1..];
    }

    /// <summary>Article du catalogue dont les mots significatifs correspondent à la désignation dictée.</summary>
    private static CatalogItem? MatchCatalog(string description, IReadOnlyList<CatalogItem> catalog)
    {
        var spoken = SignificantWords(description);
        if (spoken.Count == 0)
            return null;

        CatalogItem? best = null;
        var bestScore = 0.0;
        foreach (var item in catalog.Where(c => !c.IsDeleted))
        {
            var label = SignificantWords(item.Label);
            if (label.Count == 0)
                continue;
            var common = spoken.Count(label.Contains);
            // Tous les mots dits sont dans l'article, ou tous les mots de l'article ont été dits.
            if (common < Math.Min(spoken.Count, label.Count))
                continue;
            var score = common / (double)Math.Max(spoken.Count, label.Count);
            if (score > bestScore)
            {
                best = item;
                bestScore = score;
            }
        }
        return best;
    }

    private static HashSet<string> SignificantWords(string text) =>
        Regex.Split(RemoveDiacritics(text.ToLowerInvariant()), @"[^a-z0-9]+")
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.EndsWith('s') && w.Length > 3 ? w[..^1] : w) // pluriel simple
            .ToHashSet();

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
