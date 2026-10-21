using System.Net;
using System.Text;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>Page HTML autonome d'un devis ou d'une facture, pour l'aperçu à l'écran devant le client.</summary>
public static class HtmlDocumentRenderer
{
    public static string Render(CommercialDocument doc, IReadOnlyList<DocumentLine> lines, CompanySettings company) =>
        Render(PrintableDocument.Build(doc, lines, company));

    public static string Render(PrintableDocument p)
    {
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>").Append(E(p.Heading)).Append("</title>")
          .Append("<style>").Append(Css).Append("</style></head><body>");

        if (p.Banner is not null)
            sb.Append("<div class=\"draft\">").Append(E(p.Banner)).Append("</div>");

        // En-tête : émetteur et client
        sb.Append("<header><div class=\"party\"><strong>").Append(E(p.CompanyName)).Append("</strong>");
        foreach (var line in p.CompanyLines)
            sb.Append("<br>").Append(E(line));
        sb.Append("</div><div class=\"party client\"><span class=\"label\">Client</span><strong>")
          .Append(E(p.ClientName)).Append("</strong>");
        foreach (var line in p.ClientLines)
            sb.Append("<br>").Append(E(line));
        sb.Append("</div></header>");

        sb.Append("<h1>").Append(E(p.Heading)).Append("</h1><p class=\"meta\">")
          .Append(string.Join(" · ", p.Dates.Select(d => $"{E(d.Label)} : {E(d.Value)}"))).Append("</p>");
        if (p.Subject is not null)
            sb.Append("<p class=\"subject\"><strong>Objet :</strong> ").Append(E(p.Subject)).Append("</p>");
        if (p.SiteAddress is not null)
            sb.Append("<p><strong>Adresse du chantier :</strong> ").Append(E(p.SiteAddress)).Append("</p>");

        // Lignes
        sb.Append("<table class=\"lines\"><thead><tr><th>Désignation</th><th class=\"n\">Qté</th><th>Unité</th>")
          .Append("<th class=\"n\">PU HT</th>");
        if (p.ShowDiscount) sb.Append("<th class=\"n\">Remise</th>");
        if (p.ShowTax) sb.Append("<th class=\"n\">").Append(E(p.TaxName)).Append("</th>");
        sb.Append("<th class=\"n\">Total HT</th></tr></thead><tbody>");
        foreach (var line in p.Lines)
        {
            sb.Append("<tr><td>").Append(E(line.Description).Replace("\n", "<br>")).Append("</td>")
              .Append("<td class=\"n\">").Append(E(line.Quantity)).Append("</td>")
              .Append("<td>").Append(E(line.Unit)).Append("</td>")
              .Append("<td class=\"n\">").Append(E(line.UnitPrice)).Append("</td>");
            if (p.ShowDiscount) sb.Append("<td class=\"n\">").Append(E(line.Discount)).Append("</td>");
            if (p.ShowTax) sb.Append("<td class=\"n\">").Append(E(line.TaxRate)).Append("</td>");
            sb.Append("<td class=\"n\">").Append(E(line.Total)).Append("</td></tr>");
        }
        sb.Append("</tbody></table>");

        // Totaux
        sb.Append("<table class=\"totals\">");
        foreach (var total in p.Totals)
        {
            sb.Append(total.IsGrandTotal ? "<tr class=\"grand\">" : "<tr>")
              .Append("<td>").Append(E(total.Label)).Append("</td><td class=\"n\">").Append(E(total.Value)).Append("</td></tr>");
        }
        sb.Append("</table>");

        if (p.TaxExemptionMention is not null)
            sb.Append("<p class=\"small\">").Append(E(p.TaxExemptionMention)).Append("</p>");
        if (p.Notes is not null)
            sb.Append("<p class=\"notes\">").Append(E(p.Notes).Replace("\n", "<br>")).Append("</p>");
        if (p.SignatureText is not null)
            sb.Append("<div class=\"sign\"><p>").Append(E(p.SignatureText)).Append("</p><div class=\"box\"></div></div>");
        if (p.PaidMention is not null)
            sb.Append("<p class=\"paid\">").Append(E(p.PaidMention)).Append("</p>");
        foreach (var text in p.FinePrint)
            sb.Append("<p class=\"small\">").Append(E(text)).Append("</p>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private const string Css = """
        body{font-family:-apple-system,Roboto,"Segoe UI",Arial,sans-serif;color:#222;margin:16px;font-size:14px}
        header{display:flex;justify-content:space-between;gap:16px;flex-wrap:wrap}
        .party{line-height:1.4}.client{text-align:right}.label{display:block;color:#777;font-size:12px}
        h1{font-size:20px;margin:24px 0 4px}.meta{color:#555;margin:0 0 12px}
        table{width:100%;border-collapse:collapse}
        .lines th{background:#2b5797;color:#fff;text-align:left;padding:6px;font-weight:600}
        .lines td{border-bottom:1px solid #ddd;padding:6px;vertical-align:top}
        .n{text-align:right;white-space:nowrap}
        .totals{width:auto;margin:12px 0 0 auto;min-width:260px}.totals td{padding:4px 6px}
        .totals .grand td{font-weight:700;border-top:2px solid #2b5797}
        .draft{background:#fff3cd;color:#8a6d3b;padding:6px;text-align:center;font-weight:700;margin-bottom:12px}
        .notes{margin-top:16px;white-space:normal}.small{font-size:11px;color:#555}
        .paid{color:#2e7d32;font-weight:700}
        .sign{margin-top:24px}.box{border:1px solid #999;height:90px;width:60%;margin-left:auto}
        """;
}
