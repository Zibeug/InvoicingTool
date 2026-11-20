using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

/// <summary>État de relance d'une facture.</summary>
public sealed record ReminderState(
    bool IsOverdue,
    int DaysLate,
    IReadOnlyList<PaymentReminder> Reminders,
    int? NextLevel,
    DateTime? CurrentDueDate)
{
    public PaymentReminder? LastReminder => Reminders.Count == 0 ? null : Reminders[^1];
}

/// <summary>
/// Rappels de paiement : une facture émise, impayée et échue peut recevoir jusqu'à trois rappels,
/// chacun accordant un nouveau délai. Un rappel suivant n'est possible qu'après l'échéance du précédent.
/// </summary>
public class ReminderService(AppDatabase database, DocumentRepository documents, SettingsRepository settings, TimeProvider clock)
{
    public async Task<ReminderState> GetStateAsync(CommercialDocument invoice) =>
        ComputeState(invoice, await documents.GetRemindersAsync(invoice.Id).ConfigureAwait(false), Today());

    /// <summary>Règles de relance, sans accès à la base (utilisées aussi par le tableau de bord).</summary>
    public static ReminderState ComputeState(CommercialDocument invoice, IReadOnlyList<PaymentReminder> reminders, DateTime today)
    {
        var ordered = reminders.Where(r => !r.IsDeleted).OrderBy(r => r.Level).ToList();
        if (invoice.Type != DocumentType.Invoice || invoice.Status != DocumentStatus.Issued || invoice.DueDate is null)
            return new ReminderState(false, 0, ordered, null, invoice.DueDate);

        var currentDue = ordered.Count == 0 ? invoice.DueDate.Value : ordered[^1].NewDueDate;
        var originalDue = invoice.DueDate.Value;
        var isOverdue = today > originalDue;
        var daysLate = isOverdue ? (today - originalDue).Days : 0;
        int? next = ordered.Count < PaymentReminder.MaxLevel && today > currentDue ? ordered.Count + 1 : null;
        return new ReminderState(isOverdue, daysLate, ordered, next, currentDue);
    }

    /// <summary>Crée le rappel suivant (frais et nouveau délai selon les paramètres).</summary>
    public async Task<PaymentReminder> CreateNextAsync(CommercialDocument invoice)
    {
        var state = await GetStateAsync(invoice).ConfigureAwait(false);
        if (state.NextLevel is not { } level)
        {
            throw new InvalidOperationException(state.Reminders.Count >= PaymentReminder.MaxLevel
                ? "Le dernier rappel a déjà été envoyé."
                : "Cette facture n'est pas encore échue : un rappel n'est pas possible.");
        }

        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        var today = Today();
        var fee = Math.Max(0m, company.ReminderFee(level));
        var reminder = new PaymentReminder
        {
            DocumentId = invoice.Id,
            Level = level,
            ReminderDate = today,
            NewDueDate = today.AddDays(Math.Max(1, company.ReminderDelayDays)),
            Fee = fee,
            TotalDue = invoice.TotalInclTax + state.Reminders.Sum(r => r.Fee) + fee,
        };
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.InsertAsync(reminder).ConfigureAwait(false);
        return reminder;
    }

    /// <summary>Contenu imprimable d'un rappel : facture, frais cumulés, nouveau délai, QR-facture du montant total.</summary>
    public static PrintableDocument BuildPrintable(
        PaymentReminder reminder, IReadOnlyList<PaymentReminder> allReminders, CommercialDocument invoice, CompanySettings company)
    {
        var fmt = MoneyFormatter.For(invoice);
        var previousAndThis = allReminders.Where(r => r.Level <= reminder.Level).OrderBy(r => r.Level).ToList();
        var invoiceLine = $"Facture n° {invoice.DisplayNumber} du {fmt.Date(invoice.IssueDate)}" +
                          (invoice.DueDate is { } due ? $", échue le {fmt.Date(due)}" : "");

        var lines = new List<PrintableLine> { new(invoiceLine, "1", "", fmt.Format(invoice.TotalInclTax), "", fmt.Format(invoice.TotalInclTax)) };
        foreach (var r in previousAndThis.Where(r => r.Fee > 0))
            lines.Add(new($"Frais de {PaymentReminder.LevelLabel(r.Level).ToLowerInvariant()}", "1", "", fmt.Format(r.Fee), "", fmt.Format(r.Fee)));

        var heading = reminder.Level switch
        {
            1 => "RAPPEL",
            2 => "2e RAPPEL",
            _ => "DERNIER RAPPEL",
        };
        var deadline = fmt.Date(reminder.NewDueDate);
        var text = reminder.Level switch
        {
            1 => $"Sauf erreur de notre part, la facture ci-dessous n'a pas encore été réglée. Nous vous remercions " +
                 $"d'effectuer le paiement d'ici au {deadline}. Si votre paiement a croisé ce rappel, merci de ne pas en tenir compte.",
            2 => $"Malgré notre premier rappel, la facture ci-dessous reste impayée. Nous vous prions de régler le montant dû " +
                 $"d'ici au {deadline}.",
            _ => $"Malgré nos rappels, la facture ci-dessous reste impayée. Sans paiement d'ici au {deadline}, nous nous verrons " +
                 "contraints d'engager les démarches de recouvrement prévues par la loi.",
        };

        var invoicePrintable = PrintableDocument.Build(invoice, [], company);
        var finePrint = new List<string>();
        if (!string.IsNullOrWhiteSpace(company.Iban))
            finePrint.Add($"Règlement par virement — IBAN : {Iban.Format(company.Iban)}");

        return invoicePrintable with
        {
            Heading = $"{heading} — Facture n° {invoice.DisplayNumber}",
            Banner = null,
            Dates = [("Date", fmt.Date(reminder.ReminderDate)), ("À payer d'ici au", deadline)],
            Subject = invoice.Title,
            ShowTax = false,
            ShowQuantities = false,
            TotalHeader = "Montant",
            Lines = lines,
            Totals = [new PrintableTotal("Total à payer", fmt.Format(reminder.TotalDue), IsGrandTotal: true)],
            TaxExemptionMention = null,
            Notes = text,
            PaidMention = null,
            FinePrint = finePrint,
            SignatureText = null,
            Signature = null,
            QrBill = SwissQrBill.Create(invoice, company, reminder.TotalDue,
                $"{PaymentReminder.LevelLabel(reminder.Level)} - Facture {invoice.DisplayNumber}"),
            FileNameWithoutExtension = $"Rappel{reminder.Level}_{invoicePrintable.FileNameWithoutExtension}",
        };
    }

    private DateTime Today() => clock.GetLocalNow().Date;
}
