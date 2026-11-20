using System.Globalization;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;

namespace InvoicingTool.Core.Services;

public enum VatThresholdAlert
{
    None,
    /// <summary>80 % du seuil atteint.</summary>
    Approaching,
    Exceeded,
}

public sealed record OverdueInvoice(CommercialDocument Invoice, int DaysLate, int RemindersSent, int? NextReminderLevel);

public sealed record MonthRevenue(DateTime Month, string Label, decimal Amount);

public sealed record DashboardSummary
{
    public required int Year { get; init; }
    /// <summary>Chiffre d'affaires facturé (hors TVA) de l'année et du mois en cours.</summary>
    public required decimal RevenueYear { get; init; }
    public required decimal RevenueMonth { get; init; }
    /// <summary>Paiements reçus cette année (factures marquées payées).</summary>
    public required decimal CollectedYear { get; init; }
    /// <summary>Factures émises non payées (montant TTC à encaisser).</summary>
    public required decimal OutstandingTotal { get; init; }
    public required int OutstandingCount { get; init; }
    public required decimal OverdueTotal { get; init; }
    public required IReadOnlyList<OverdueInvoice> Overdue { get; init; }
    public required int PendingQuotesCount { get; init; }
    public required decimal PendingQuotesTotal { get; init; }
    /// <summary>Chiffre d'affaires des 12 derniers mois, du plus ancien au mois en cours.</summary>
    public required IReadOnlyList<MonthRevenue> Last12Months { get; init; }
    public decimal? VatThreshold { get; init; }
    public VatThresholdAlert VatAlert { get; init; }
    /// <summary>Des documents dans une autre devise que celle de l'entreprise ne sont pas comptés.</summary>
    public bool HasOtherCurrencies { get; init; }
}

/// <summary>Chiffres clés de l'activité, calculés sur les documents de la devise de l'entreprise.</summary>
public class DashboardService(DocumentRepository documents, SettingsRepository settings, TimeProvider clock)
{
    public async Task<DashboardSummary> ComputeAsync()
    {
        var company = await settings.GetSettingsAsync().ConfigureAwait(false);
        var invoices = await documents.GetByTypeAsync(DocumentType.Invoice).ConfigureAwait(false);
        var quotes = await documents.GetByTypeAsync(DocumentType.Quote).ConfigureAwait(false);
        var reminders = await documents.GetAllRemindersAsync().ConfigureAwait(false);
        return Compute(company, invoices, quotes, reminders, clock.GetLocalNow().Date);
    }

    public static DashboardSummary Compute(
        CompanySettings company,
        IReadOnlyList<CommercialDocument> invoices,
        IReadOnlyList<CommercialDocument> quotes,
        IReadOnlyList<PaymentReminder> reminders,
        DateTime today)
    {
        var currency = company.CurrencyCode;
        bool SameCurrency(CommercialDocument d) => (d.CurrencyCode ?? Currencies.Default) == currency;

        // Factures « réelles » : émises ou payées (ni brouillon ni annulée).
        var billed = invoices.Where(d => d.Status is DocumentStatus.Issued or DocumentStatus.Paid).ToList();
        var counted = billed.Where(SameCurrency).ToList();

        var remindersByInvoice = reminders.GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => (IReadOnlyList<PaymentReminder>)g.ToList());
        var outstanding = counted.Where(d => d.Status == DocumentStatus.Issued).ToList();
        var overdue = outstanding
            .Select(d => (Invoice: d, State: ReminderService.ComputeState(d, remindersByInvoice.GetValueOrDefault(d.Id) ?? [], today)))
            .Where(x => x.State.IsOverdue)
            .Select(x => new OverdueInvoice(x.Invoice, x.State.DaysLate, x.State.Reminders.Count, x.State.NextLevel))
            .OrderByDescending(o => o.DaysLate)
            .ToList();

        var pendingQuotes = quotes.Where(q => q.Status == DocumentStatus.Issued && SameCurrency(q)).ToList();

        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var months = Enumerable.Range(0, 12)
            .Select(i => firstMonth.AddMonths(i))
            .Select(m => new MonthRevenue(m, m.ToString("MMM", fr).TrimEnd('.'),
                counted.Where(d => d.IssueDate.Year == m.Year && d.IssueDate.Month == m.Month).Sum(d => d.TotalExclTax)))
            .ToList();

        var revenueYear = counted.Where(d => d.IssueDate.Year == today.Year).Sum(d => d.TotalExclTax);
        var threshold = company.VatExempt ? company.VatRegistrationThreshold ?? company.Country.VatRegistrationThreshold : null;
        var alert = threshold is > 0
            ? revenueYear >= threshold ? VatThresholdAlert.Exceeded
            : revenueYear >= threshold * 0.8m ? VatThresholdAlert.Approaching
            : VatThresholdAlert.None
            : VatThresholdAlert.None;

        return new DashboardSummary
        {
            Year = today.Year,
            RevenueYear = revenueYear,
            RevenueMonth = counted.Where(d => d.IssueDate.Year == today.Year && d.IssueDate.Month == today.Month).Sum(d => d.TotalExclTax),
            CollectedYear = counted.Where(d => d.Status == DocumentStatus.Paid && d.PaidDate?.Year == today.Year).Sum(d => d.TotalInclTax),
            OutstandingTotal = outstanding.Sum(d => d.TotalInclTax),
            OutstandingCount = outstanding.Count,
            OverdueTotal = overdue.Sum(o => o.Invoice.TotalInclTax),
            Overdue = overdue,
            PendingQuotesCount = pendingQuotes.Count,
            PendingQuotesTotal = pendingQuotes.Sum(q => q.TotalInclTax),
            Last12Months = months,
            VatThreshold = threshold is > 0 ? threshold : null,
            VatAlert = alert,
            HasOtherCurrencies = billed.Concat(quotes).Any(d => d.Status != DocumentStatus.Draft && !SameCurrency(d)),
        };
    }
}
