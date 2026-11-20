using Codecrete.SwissQRBill.Generator;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Pdf.QrBill;
using InvoicingTool.Core.Services;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp;
using PdfSharp.Drawing;
using MigraDocRenderer = MigraDoc.Rendering.PdfDocumentRenderer;

namespace InvoicingTool.Core.Pdf;

/// <summary>Génère le PDF A4 d'un devis ou d'une facture, à envoyer ou imprimer.</summary>
public static class PdfDocumentRenderer
{
    private static readonly Color Muted = new(0x55, 0x55, 0x55);
    private static readonly Color LineColor = new(0xDD, 0xDD, 0xDD);

    public static byte[] Render(
        CommercialDocument doc, IReadOnlyList<DocumentLine> lines, CompanySettings company, DocumentSignature? signature = null) =>
        Render(PrintableDocument.Build(doc, lines, company, signature));

    public static byte[] Render(PrintableDocument p)
    {
        EmbeddedFontResolver.EnsureRegistered();

        var document = new Document();
        document.Info.Title = p.Heading;
        document.Info.Author = p.CompanyName;
        var accent = ParseColor(p.AccentColor);
        DefineStyles(document, accent);

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        var width = Unit.FromCentimeter(21 - 3.6);

        // Avec une QR-facture, le bas de la dernière page est réservé à la section paiement :
        // la numérotation des pages passe alors en haut.
        var footer = p.QrBill is null ? section.Footers.Primary.AddParagraph() : section.Headers.Primary.AddParagraph();
        footer.Style = "Small";
        footer.Format.Alignment = p.QrBill is null ? ParagraphAlignment.Center : ParagraphAlignment.Right;
        footer.AddText($"{p.Heading} — page ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        if (p.Banner is not null)
        {
            var banner = section.AddParagraph(p.Banner);
            banner.Format.Font.Bold = true;
            banner.Format.Font.Color = new Color(0x8A, 0x6D, 0x3B);
            banner.Format.Shading.Color = new Color(0xFF, 0xF3, 0xCD);
            banner.Format.Alignment = ParagraphAlignment.Center;
            banner.Format.SpaceAfter = Unit.FromPoint(10);
        }

        AddParties(section, p, width);

        var heading = section.AddParagraph(p.Heading, "Heading");
        var meta = section.AddParagraph(string.Join("   ·   ", p.Dates.Select(d => $"{d.Label} : {d.Value}")));
        meta.Format.Font.Color = Muted;
        meta.Format.SpaceAfter = Unit.FromPoint(8);
        if (p.Subject is not null)
            AddLabelled(section, "Objet : ", p.Subject);

        AddLines(section, p, width, accent);
        AddTotals(section, p, width, accent);

        if (p.TaxExemptionMention is not null)
            section.AddParagraph(p.TaxExemptionMention, "Small");
        if (p.Notes is not null)
        {
            var notes = section.AddParagraph(p.Notes);
            notes.Format.SpaceBefore = Unit.FromPoint(10);
        }
        if (p.SignatureText is not null)
            AddSignature(section, p.SignatureText, p.Signature, width);
        if (p.PaidMention is not null)
        {
            var paid = section.AddParagraph(p.PaidMention);
            paid.Format.Font.Bold = true;
            paid.Format.Font.Color = new Color(0x2E, 0x7D, 0x32);
            paid.Format.SpaceBefore = Unit.FromPoint(10);
        }
        if (p.FinePrint.Count > 0)
        {
            var first = true;
            foreach (var text in p.FinePrint)
            {
                var small = section.AddParagraph(text, "Small");
                if (first)
                    small.Format.SpaceBefore = Unit.FromPoint(14);
                first = false;
            }
        }

        var renderer = new MigraDocRenderer { Document = document };
        renderer.RenderDocument();
        if (p.QrBill is not null)
            AddQrBill(renderer, p.QrBill);
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        return output.ToArray();
    }

    private static Color ParseColor(string hex)
    {
        var value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// <summary>Hauteur de la section paiement (récépissé + section paiement) : 105 mm, en bas d'une page A4.</summary>
    private static readonly double QrBillHeightPt = XUnit.FromMillimeter(105).Point;

    /// <summary>
    /// Dessine la section paiement en bas de la dernière page si elle est libre, sinon sur une page supplémentaire.
    /// </summary>
    private static void AddQrBill(MigraDocRenderer renderer, Bill bill)
    {
        var pdf = renderer.PdfDocument;
        var page = pdf.Pages[pdf.PageCount - 1];
        var contentBottom = (renderer.DocumentRenderer.GetRenderInfoFromPage(pdf.PageCount) ?? [])
            .Select(info => info.LayoutInfo.ContentArea)
            .Select(area => area.Y.Point + area.Height.Point)
            .DefaultIfEmpty(0)
            .Max();
        var available = page.Height.Point - QrBillHeightPt - XUnit.FromMillimeter(5).Point;
        if (contentBottom > available)
        {
            page = pdf.AddPage();
            page.Size = PageSize.A4;
        }

        using var canvas = new PdfSharpCanvas(page, SwissQrBill.FontFamily);
        QRBill.Draw(bill, canvas);
    }

    private static void DefineStyles(Document document, Color accent)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 9.5;

        var heading = document.Styles.AddStyle("Heading", StyleNames.Normal);
        heading.Font.Size = 16;
        heading.Font.Bold = true;
        heading.Font.Color = accent;
        heading.ParagraphFormat.SpaceBefore = Unit.FromPoint(18);
        heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var small = document.Styles.AddStyle("Small", StyleNames.Normal);
        small.Font.Size = 7.5;
        small.Font.Color = Muted;
        small.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
    }

    private static void AddParties(Section section, PrintableDocument p, Unit width)
    {
        var table = section.AddTable();
        table.AddColumn(width * 0.55);
        table.AddColumn(width * 0.45);
        var row = table.AddRow();

        if (p.Logo is not null)
        {
            var logoParagraph = row.Cells[0].AddParagraph();
            logoParagraph.Format.SpaceAfter = Unit.FromPoint(6);
            var logo = logoParagraph.AddImage(Base64(p.Logo));
            logo.LockAspectRatio = true;
            logo.Height = Unit.FromCentimeter(1.8);
        }
        var company = row.Cells[0].AddParagraph();
        company.AddFormattedText(p.CompanyName, new Font { Bold = true, Size = 12 });
        foreach (var line in p.CompanyLines)
        {
            company.AddLineBreak();
            company.AddText(line);
        }

        var client = row.Cells[1];
        client.Format.Alignment = ParagraphAlignment.Right;
        var label = client.AddParagraph("Client");
        label.Format.Font.Color = Muted;
        label.Format.Font.Size = 8;
        var clientParagraph = client.AddParagraph();
        clientParagraph.AddFormattedText(p.ClientName, new Font { Bold = true, Size = 11 });
        foreach (var line in p.ClientLines)
        {
            clientParagraph.AddLineBreak();
            clientParagraph.AddText(line);
        }
    }

    private static void AddLabelled(Section section, string label, string value)
    {
        var paragraph = section.AddParagraph();
        paragraph.AddFormattedText(label, TextFormat.Bold);
        paragraph.AddText(value);
        paragraph.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static void AddLines(Section section, PrintableDocument p, Unit width, Color accent)
    {
        // Colonnes : désignation | qté | unité | prix unitaire | (taxe) | total
        var fixedWidths = p.ShowQuantities
            ? new List<(string Header, double Ratio, bool Right)> { ("Qté", 0.08, true), ("Unité", 0.09, false), (p.UnitPriceHeader, 0.15, true) }
            : [];
        if (p.ShowTax) fixedWidths.Add((p.TaxName, 0.08, true));
        fixedWidths.Add((p.TotalHeader, 0.16, true));
        var descriptionRatio = 1 - fixedWidths.Sum(c => c.Ratio);

        var table = section.AddTable();
        table.Borders.Bottom.Width = 0;
        table.TopPadding = Unit.FromPoint(3);
        table.BottomPadding = Unit.FromPoint(3);
        table.Format.SpaceBefore = Unit.FromPoint(8);
        table.AddColumn(width * descriptionRatio);
        foreach (var column in fixedWidths)
            table.AddColumn(width * column.Ratio).Format.Alignment = column.Right ? ParagraphAlignment.Right : ParagraphAlignment.Left;

        var header = table.AddRow();
        header.HeadingFormat = true; // répété sur chaque page
        header.Shading.Color = accent;
        header.Format.Font.Color = Colors.White;
        header.Format.Font.Bold = true;
        header.Cells[0].AddParagraph("Désignation");
        for (var i = 0; i < fixedWidths.Count; i++)
            header.Cells[i + 1].AddParagraph(fixedWidths[i].Header);

        foreach (var line in p.Lines)
        {
            var row = table.AddRow();
            row.Borders.Bottom.Color = LineColor;
            row.Borders.Bottom.Width = 0.5;
            row.Cells[0].AddParagraph(line.Description);
            var values = p.ShowQuantities ? new List<string> { line.Quantity, line.Unit, line.UnitPrice } : [];
            if (p.ShowTax) values.Add(line.TaxRate);
            values.Add(line.Total);
            for (var i = 0; i < values.Count; i++)
                row.Cells[i + 1].AddParagraph(values[i]);
        }
    }

    private static void AddTotals(Section section, PrintableDocument p, Unit width, Color accent)
    {
        var spacer = section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromPoint(6);

        var table = section.AddTable();
        table.Rows.LeftIndent = width * 0.45;
        table.TopPadding = Unit.FromPoint(2);
        table.BottomPadding = Unit.FromPoint(2);
        table.AddColumn(width * 0.35);
        table.AddColumn(width * 0.20).Format.Alignment = ParagraphAlignment.Right;
        foreach (var total in p.Totals)
        {
            var row = table.AddRow();
            row.KeepWith = 0;
            row.Cells[0].AddParagraph(total.Label);
            row.Cells[1].AddParagraph(total.Value);
            if (total.IsGrandTotal)
            {
                row.Format.Font.Bold = true;
                row.Format.Font.Size = 11;
                row.Borders.Top.Color = accent;
                row.Borders.Top.Width = 1.5;
            }
        }
    }

    private static void AddSignature(Section section, string text, PrintableSignature? signature, Unit width)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.SpaceBefore = Unit.FromPoint(18);
        paragraph.Format.KeepWithNext = true;

        var table = section.AddTable();
        table.Rows.LeftIndent = width * 0.45;
        table.AddColumn(width * 0.55);
        var row = table.AddRow();
        row.Height = Unit.FromCentimeter(3);
        row.Borders.Width = 0.75;
        row.Borders.Color = new Color(0x99, 0x99, 0x99);

        if (signature is null)
            return;
        var cell = row.Cells[0];
        cell.Format.Alignment = ParagraphAlignment.Center;
        var image = cell.AddParagraph().AddImage(Base64(signature.Image));
        image.LockAspectRatio = true;
        image.Height = Unit.FromCentimeter(2.6);
        var caption = section.AddParagraph(signature.Caption, "Small");
        caption.Format.Alignment = ParagraphAlignment.Right;
        caption.Format.SpaceBefore = Unit.FromPoint(3);
    }

    /// <summary>Image intégrée directement au PDF (format « base64: » de MigraDoc), sans fichier temporaire.</summary>
    private static string Base64(byte[] image) => "base64:" + Convert.ToBase64String(image);
}
