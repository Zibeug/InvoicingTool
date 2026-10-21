using System.Globalization;
using System.Net;
using System.Text;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>
/// Produit une page HTML autonome d'un devis ou d'une facture, affichable sur le téléphone
/// pour la présenter au client, imprimable en PDF, ou partageable par e-mail / messagerie.
/// </summary>
public static class HtmlDocumentRenderer
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Render(CommercialDocument doc, IReadOnlyList<DocumentLine> lines, CompanySettings company)
    {
        var totals = DocumentCalculator.Compute(lines, company.VatExempt);
        var isQuote = doc.Type == DocumentType.Quote;
        var title = isQuote ? "DEVIS" : "FACTURE";
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>").Append(E($"{title} {doc.DisplayNumber}")).Append("</title>")
          .Append("<style>").Append(Css).Append("</style></head><body>");

        if (doc.Status == DocumentStatus.Draft)
            sb.Append("<div class=\"draft\">BROUILLON — document non émis</div>");
        if (doc.Status == DocumentStatus.Cancelled)
            sb.Append("<div class=\"draft\">ANNULÉ</div>");

        // En-tête : émetteur et client
        sb.Append("<header><div class=\"party\">")
          .Append("<strong>").Append(E(company.CompanyName)).Append("</strong>");
        Line(sb, company.OwnerName);
        Line(sb, company.Address);
        Line(sb, Join(" ", company.PostalCode, company.City));
        Line(sb, company.Phone);
        Line(sb, company.Email);
        Line(sb, Prefixed("SIRET : ", company.Siret));
        Line(sb, Prefixed("TVA intracom. : ", company.VatNumber));
        sb.Append("</div><div class=\"party client\"><span class=\"label\">Client</span><strong>")
          .Append(E(doc.ClientName)).Append("</strong>");
        Line(sb, doc.ClientAddress);
        Line(sb, Prefixed("SIRET / TVA : ", doc.ClientCompanyId));
        sb.Append("</div></header>");

        // Titre et dates
        sb.Append("<h1>").Append(title).Append(" n° ").Append(E(doc.DisplayNumber)).Append("</h1><p class=\"meta\">")
          .Append("Date : ").Append(Date(doc.IssueDate));
        if (doc.DueDate is { } due)
            sb.Append(" · ").Append(isQuote ? "Valable jusqu'au : " : "Échéance : ").Append(Date(due));
        sb.Append("</p>");
        if (!string.IsNullOrWhiteSpace(doc.Title))
            sb.Append("<p class=\"subject\"><strong>Objet :</strong> ").Append(E(doc.Title)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(doc.SiteAddress))
            sb.Append("<p><strong>Adresse du chantier :</strong> ").Append(E(doc.SiteAddress)).Append("</p>");

        // Lignes
        var showVat = !company.VatExempt;
        var showDiscount = lines.Any(l => l.DiscountPercent != 0);
        sb.Append("<table class=\"lines\"><thead><tr><th>Désignation</th><th class=\"n\">Qté</th><th>Unité</th>")
          .Append("<th class=\"n\">PU HT</th>");
        if (showDiscount) sb.Append("<th class=\"n\">Remise</th>");
        if (showVat) sb.Append("<th class=\"n\">TVA</th>");
        sb.Append("<th class=\"n\">Total HT</th></tr></thead><tbody>");
        foreach (var line in lines)
        {
            sb.Append("<tr><td>").Append(E(line.Description).Replace("\n", "<br>")).Append("</td>")
              .Append("<td class=\"n\">").Append(Num(line.Quantity)).Append("</td>")
              .Append("<td>").Append(E(line.Unit)).Append("</td>")
              .Append("<td class=\"n\">").Append(Money(line.UnitPrice)).Append("</td>");
            if (showDiscount) sb.Append("<td class=\"n\">").Append(Num(line.DiscountPercent)).Append(" %</td>");
            if (showVat) sb.Append("<td class=\"n\">").Append(Num(line.VatRate)).Append(" %</td>");
            sb.Append("<td class=\"n\">").Append(Money(DocumentCalculator.LineTotalExclTax(line))).Append("</td></tr>");
        }
        sb.Append("</tbody></table>");

        // Totaux
        sb.Append("<table class=\"totals\">");
        Row(sb, "Total HT", Money(totals.TotalExclTax));
        if (showVat)
        {
            foreach (var vat in totals.VatBreakdown)
                Row(sb, $"TVA {Num(vat.Rate)} % (base {Money(vat.BaseExclTax)})", Money(vat.VatAmount));
            Row(sb, "Total TTC", Money(totals.TotalInclTax), "grand");
        }
        else
        {
            Row(sb, "Net à payer", Money(totals.TotalInclTax), "grand");
        }
        if (doc.DepositAmount > 0)
        {
            Row(sb, "Acompte versé", "- " + Money(doc.DepositAmount));
            Row(sb, "Reste à payer", Money(totals.TotalInclTax - doc.DepositAmount), "grand");
        }
        sb.Append("</table>");

        if (company.VatExempt)
            sb.Append("<p class=\"small\">TVA non applicable, art. 293 B du CGI.</p>");

        if (!string.IsNullOrWhiteSpace(doc.Notes))
            sb.Append("<p class=\"notes\">").Append(E(doc.Notes).Replace("\n", "<br>")).Append("</p>");

        // Mentions et signature
        if (isQuote)
        {
            sb.Append("<div class=\"sign\"><p>Bon pour accord — date et signature du client, précédées de la mention « Devis reçu avant l'exécution des travaux » :</p>")
              .Append("<div class=\"box\"></div></div>");
        }
        else
        {
            if (doc.Status == DocumentStatus.Paid && doc.PaidDate is { } paid)
                sb.Append("<p class=\"paid\">Facture acquittée le ").Append(Date(paid)).Append("</p>");
            if (!string.IsNullOrWhiteSpace(company.Iban))
                sb.Append("<p class=\"small\">Règlement par virement — IBAN : ").Append(E(company.Iban)).Append("</p>");
            if (!string.IsNullOrWhiteSpace(company.PaymentTerms))
                sb.Append("<p class=\"small\">").Append(E(company.PaymentTerms)).Append("</p>");
        }
        if (!string.IsNullOrWhiteSpace(company.InsuranceInfo))
            sb.Append("<p class=\"small\">Assurance professionnelle : ").Append(E(company.InsuranceInfo)).Append("</p>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string Money(decimal amount) => amount.ToString("N2", Fr) + " €";

    private static string Num(decimal value) => value.ToString("0.##", Fr);

    private static string Date(DateTime date) => date.ToString("dd/MM/yyyy", Fr);

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static string? Join(string separator, params string?[] parts)
    {
        var joined = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return joined.Length == 0 ? null : joined;
    }

    private static string? Prefixed(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : prefix + value;

    private static void Line(StringBuilder sb, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            sb.Append("<br>").Append(E(text));
    }

    private static void Row(StringBuilder sb, string label, string value, string? cssClass = null)
    {
        sb.Append("<tr").Append(cssClass is null ? "" : $" class=\"{cssClass}\"").Append("><td>")
          .Append(E(label)).Append("</td><td class=\"n\">").Append(E(value)).Append("</td></tr>");
    }

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
        @media print{body{margin:0}}
        """;
}
