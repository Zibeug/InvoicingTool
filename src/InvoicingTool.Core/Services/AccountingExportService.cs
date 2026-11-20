using System.Globalization;
using System.IO.Compression;
using System.Text;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Pdf;

namespace InvoicingTool.Core.Services;

/// <summary>
/// Export annuel pour la fiduciaire ou le comptable : un fichier ZIP avec
/// la liste des factures (CSV), la ventilation de la TVA (CSV), un journal d'écritures importable dans
/// Banana Comptabilité, le PDF de chaque facture et un mode d'emploi.
/// </summary>
public class AccountingExportService(DocumentRepository documents, SettingsRepository settings, TimeProvider clock)
{
    // Comptes du plan comptable PME suisse utilisés par défaut dans le journal (à adapter avec la fiduciaire).
    public const string DebtorsAccount = "1100";
    public const string RevenueAccount = "3400";
    public const string VatAccount = "2200";
    public const string BankAccount = "1020";

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Années qui contiennent des factures émises, plus l'année en cours, de la plus récente à la plus ancienne.</summary>
    public async Task<IReadOnlyList<int>> GetYearsAsync()
    {
        var invoices = await documents.GetByTypeAsync(DocumentType.Invoice).ConfigureAwait(false);
        return invoices.Where(d => d.Status != DocumentStatus.Draft)
            .Select(d => d.IssueDate.Year)
            .Append(clock.GetLocalNow().Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();
    }

    public static string FileName(int year) => $"export-fiduciaire-{year}.zip";

    public async Task<byte[]> ExportYearAsync(int year)
    {
        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        var all = await documents.GetByTypeAsync(DocumentType.Invoice).ConfigureAwait(false);
        var invoices = all.Where(d => d.Status != DocumentStatus.Draft && d.IssueDate.Year == year)
            .OrderBy(d => d.IssueDate).ThenBy(d => d.Number, StringComparer.Ordinal).ToList();
        // Paiements de l'année, y compris ceux de factures émises l'année précédente.
        var payments = all.Where(d => d.Status == DocumentStatus.Paid && d.PaidDate?.Year == year)
            .OrderBy(d => d.PaidDate).ToList();

        var details = new List<(CommercialDocument Doc, IReadOnlyList<DocumentLine> Lines, DocumentTotals Totals, int Reminders)>();
        foreach (var doc in invoices)
        {
            var lines = await documents.GetLinesAsync(doc.Id).ConfigureAwait(false);
            var reminders = await documents.GetRemindersAsync(doc.Id).ConfigureAwait(false);
            details.Add((doc, lines, DocumentCalculator.Compute(doc, lines), reminders.Count));
        }

        var decimalComma = company.CountryCode != CountryProfiles.Switzerland.Code;
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(zip, $"factures-{year}.csv", InvoicesCsv(details, decimalComma));
            if (details.Any(d => !d.Doc.VatExempt))
                AddText(zip, $"tva-{year}.csv", VatCsv(details, decimalComma));
            AddText(zip, $"journal-banana-{year}.txt", BananaJournal(details, payments));
            AddText(zip, "LISEZMOI.txt", ReadMe(year, company, details.Count, payments.Count));

            foreach (var (doc, lines, _, _) in details)
            {
                var entry = zip.CreateEntry($"factures-pdf/{SafeName(doc.DisplayNumber)}.pdf", CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await stream.WriteAsync(PdfDocumentRenderer.Render(doc, lines, company)).ConfigureAwait(false);
            }
        }
        return output.ToArray();
    }

    private static string InvoicesCsv(
        IEnumerable<(CommercialDocument Doc, IReadOnlyList<DocumentLine> Lines, DocumentTotals Totals, int Reminders)> details, bool decimalComma)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Numéro;Date;Échéance;Client;N° entreprise client;Objet;Statut;Devise;Montant HT;TVA;Total;Payée le;Rappels envoyés");
        foreach (var (doc, _, _, reminders) in details)
        {
            sb.AppendLine(string.Join(';',
                Csv(doc.DisplayNumber), Date(doc.IssueDate), Date(doc.DueDate), Csv(doc.ClientName), Csv(doc.ClientCompanyId),
                Csv(doc.Title), Status(doc.Status), doc.CurrencyCode ?? Currencies.Default,
                Number(doc.TotalExclTax, decimalComma), Number(doc.TotalVat, decimalComma), Number(doc.TotalInclTax, decimalComma),
                Date(doc.PaidDate), reminders.ToString(CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }

    private static string VatCsv(
        IEnumerable<(CommercialDocument Doc, IReadOnlyList<DocumentLine> Lines, DocumentTotals Totals, int Reminders)> details, bool decimalComma)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Numéro;Date;Statut;Taux TVA (%);Base HT;TVA");
        foreach (var (doc, _, totals, _) in details.Where(d => !d.Doc.VatExempt))
        {
            foreach (var vat in totals.VatBreakdown)
            {
                sb.AppendLine(string.Join(';', Csv(doc.DisplayNumber), Date(doc.IssueDate), Status(doc.Status),
                    Number(vat.Rate, decimalComma), Number(vat.BaseExclTax, decimalComma), Number(vat.VatAmount, decimalComma)));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Journal au format d'import de Banana Comptabilité (texte séparé par des tabulations) :
    /// facture = débit Débiteurs / crédit Ventes (et TVA due) ; paiement = débit Banque / crédit Débiteurs.
    /// Les factures annulées ne sont pas comptabilisées.
    /// </summary>
    private static string BananaJournal(
        IEnumerable<(CommercialDocument Doc, IReadOnlyList<DocumentLine> Lines, DocumentTotals Totals, int Reminders)> details,
        IEnumerable<CommercialDocument> payments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date\tDoc\tDescription\tAccountDebit\tAccountCredit\tAmount");
        void Entry(DateTime date, string doc, string description, string debit, string credit, decimal amount)
        {
            if (amount == 0)
                return;
            if (amount < 0)
                (debit, credit, amount) = (credit, debit, -amount);
            sb.AppendLine(string.Join('\t', date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Tab(doc), Tab(description),
                debit, credit, amount.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        foreach (var (doc, _, totals, _) in details.Where(d => d.Doc.Status != DocumentStatus.Cancelled))
        {
            var label = $"Facture {doc.DisplayNumber} – {doc.ClientName}";
            Entry(doc.IssueDate, doc.DisplayNumber, label, DebtorsAccount, RevenueAccount, totals.TotalExclTax);
            foreach (var vat in totals.VatBreakdown.Where(v => v.VatAmount != 0))
                Entry(doc.IssueDate, doc.DisplayNumber, $"TVA {vat.Rate.ToString("0.##", CultureInfo.InvariantCulture)} % – {label}",
                    DebtorsAccount, VatAccount, vat.VatAmount);
            Entry(doc.IssueDate, doc.DisplayNumber, $"Arrondi – {label}", DebtorsAccount, RevenueAccount, totals.RoundingAdjustment);
        }
        foreach (var doc in payments)
            Entry(doc.PaidDate!.Value, doc.DisplayNumber, $"Paiement facture {doc.DisplayNumber} – {doc.ClientName}",
                BankAccount, DebtorsAccount, doc.TotalInclTax);
        return sb.ToString();
    }

    private static string ReadMe(int year, CompanySettings company, int invoiceCount, int paymentCount) => $"""
        Export pour la fiduciaire — {company.CompanyName} — année {year}
        Généré le {DateTime.Now:dd.MM.yyyy HH:mm}

        Contenu
        - factures-{year}.csv : les {invoiceCount} facture(s) émise(s) en {year} (brouillons exclus), avec statut,
          montants, date de paiement et nombre de rappels. Séparateur « ; », encodage UTF-8 (ouvrable dans Excel).
        - tva-{year}.csv : ventilation de la TVA par facture et par taux (si l'entreprise est assujettie).
        - journal-banana-{year}.txt : écritures à importer dans Banana Comptabilité
          (Actions > Importer dans la comptabilité > Écritures, fichier texte avec en-têtes).
          {paymentCount} paiement(s) enregistré(s) en {year}. Les factures annulées ne sont pas comptabilisées.
        - factures-pdf/ : le PDF de chaque facture.

        Comptes utilisés dans le journal (plan comptable PME suisse) — à vérifier et adapter avec votre fiduciaire :
        - {DebtorsAccount} Créances résultant de livraisons et prestations (débiteurs)
        - {RevenueAccount} Produits des prestations de services
        - {VatAccount} TVA due
        - {BankAccount} Banque

        Les paiements sont ceux marqués « Payée » dans l'application, à la date indiquée.
        Les frais de rappel encaissés ne sont pas comptabilisés automatiquement.
        """;

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(content)).ToArray();
        stream.Write(bytes);
    }

    private static string Status(DocumentStatus status) => status switch
    {
        DocumentStatus.Paid => "Payée",
        DocumentStatus.Cancelled => "Annulée",
        _ => "Impayée",
    };

    private static string Date(DateTime? date) => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";

    private static string Number(decimal value, bool decimalComma)
    {
        var text = value.ToString("0.00", CultureInfo.InvariantCulture);
        return decimalComma ? text.Replace('.', ',') : text;
    }

    /// <summary>Champ CSV : entre guillemets s'il contient « ; », des guillemets ou un retour à la ligne.</summary>
    private static string Csv(string? value)
    {
        var text = value ?? "";
        return text.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static string Tab(string text) => text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static string SafeName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        return new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
