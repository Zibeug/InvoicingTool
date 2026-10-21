using System.Reflection;
using PdfSharp.Fonts;

namespace InvoicingTool.Core.Pdf;

/// <summary>Fournit à PDFsharp les polices OpenSans embarquées dans l'assembly (aucune police système requise).</summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "OpenSans";
    private const string Regular = "OpenSans-Regular";
    private const string Semibold = "OpenSans-Semibold";

    private static readonly Lock InitLock = new();
    private static bool _registered;

    /// <summary>Enregistre le résolveur une seule fois pour toute l'application (réglage global de PDFsharp).</summary>
    public static void EnsureRegistered()
    {
        lock (InitLock)
        {
            if (_registered)
                return;
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        // Toute famille demandée est servie par OpenSans ; l'italique est simulé.
        new(isBold ? Semibold : Regular, false, isItalic);

    public byte[]? GetFont(string faceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"InvoicingTool.Fonts.{faceName}.ttf");
        if (stream is null)
            return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
