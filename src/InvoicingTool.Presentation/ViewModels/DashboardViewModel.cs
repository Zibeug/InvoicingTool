using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Barre du graphique des 12 derniers mois.</summary>
public sealed record MonthBar(string Label, string AmountDisplay, double Height, bool IsCurrentMonth);

/// <summary>Facture en retard, avec l'action de relance possible.</summary>
public sealed record OverdueRow(
    CommercialDocument Invoice, string Title, string AmountDisplay, string LateText, string ReminderText, string? ActionText)
{
    public bool CanRemind => ActionText is not null;
}

/// <summary>Tableau de bord : chiffre d'affaires, encaissements, impayés, factures à relancer, seuil de TVA.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    private const double MaxBarHeight = 90;

    private readonly DashboardService _dashboard;
    private readonly ReminderService _reminders;
    private readonly DocumentService _service;
    private readonly DocumentRepository _documents;
    private readonly SettingsRepository _settings;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _clock;

    public DashboardViewModel(
        DashboardService dashboard,
        ReminderService reminders,
        DocumentService service,
        DocumentRepository documents,
        SettingsRepository settings,
        INavigationService navigation,
        TimeProvider clock,
        IDialogService dialogs) : base(dialogs)
    {
        _dashboard = dashboard;
        _reminders = reminders;
        _service = service;
        _documents = documents;
        _settings = settings;
        _navigation = navigation;
        _clock = clock;
        Title = "Accueil";
    }

    public ObservableCollection<MonthBar> Months { get; } = [];
    public ObservableCollection<OverdueRow> Overdue { get; } = [];

    [ObservableProperty] private string _greeting = "Bonjour";
    [ObservableProperty] private string _yearLabel = string.Empty;
    [ObservableProperty] private string _revenueYearDisplay = string.Empty;
    [ObservableProperty] private string _revenueMonthDisplay = string.Empty;
    [ObservableProperty] private string _collectedYearDisplay = string.Empty;
    [ObservableProperty] private string _outstandingDisplay = string.Empty;
    [ObservableProperty] private string _outstandingCountText = string.Empty;
    [ObservableProperty] private string _overdueDisplay = string.Empty;
    [ObservableProperty] private string _overdueCountText = string.Empty;
    [ObservableProperty] private bool _hasOverdue;
    [ObservableProperty] private string _pendingQuotesText = string.Empty;
    [ObservableProperty] private bool _hasActivity;

    // Seuil de TVA (entreprise non assujettie)
    [ObservableProperty] private bool _hasVatThreshold;
    [ObservableProperty] private bool _isVatAlert;
    [ObservableProperty] private string _vatTitle = string.Empty;
    [ObservableProperty] private string _vatText = string.Empty;
    [ObservableProperty] private double _vatProgress;
    /// <summary>Ton de l'alerte (« Success », « Warning », « Danger ») : la vue choisit la couleur selon le thème.</summary>
    [ObservableProperty] private string _vatTone = "Success";

    [ObservableProperty] private string? _otherCurrenciesNote;
    [ObservableProperty] private string _backupText = string.Empty;
    [ObservableProperty] private bool _isBackupOverdue;

    public override async Task OnAppearingAsync()
    {
        var company = await _settings.GetSettingsAsync();
        var money = MoneyFormatter.For(company);
        var summary = await _dashboard.ComputeAsync();
        var today = _clock.GetLocalNow().Date;

        Greeting = string.IsNullOrWhiteSpace(company.CompanyName) ? "Bonjour" : $"Bonjour, {company.CompanyName}";
        YearLabel = $"Chiffre d'affaires {summary.Year}";
        RevenueYearDisplay = money.Format(summary.RevenueYear);
        RevenueMonthDisplay = $"dont {money.Format(summary.RevenueMonth)} ce mois-ci";
        CollectedYearDisplay = money.Format(summary.CollectedYear);
        OutstandingDisplay = money.Format(summary.OutstandingTotal);
        OutstandingCountText = Plural(summary.OutstandingCount, "facture à encaisser", "factures à encaisser");
        OverdueDisplay = money.Format(summary.OverdueTotal);
        OverdueCountText = Plural(summary.Overdue.Count, "facture en retard", "factures en retard");
        HasOverdue = summary.Overdue.Count > 0;
        PendingQuotesText = summary.PendingQuotesCount == 0
            ? "Aucun devis en attente de réponse"
            : $"{Plural(summary.PendingQuotesCount, "devis en attente", "devis en attente")} · {money.Format(summary.PendingQuotesTotal)}";
        HasActivity = summary.Last12Months.Any(m => m.Amount > 0) || summary.OutstandingCount > 0;
        OtherCurrenciesNote = summary.HasOtherCurrencies
            ? $"Les documents dans une autre devise que {company.CurrencyCode} ne sont pas comptés."
            : null;

        // Graphique : hauteur proportionnelle au meilleur mois.
        var max = summary.Last12Months.Max(m => m.Amount);
        Months.Clear();
        foreach (var month in summary.Last12Months)
        {
            var height = max > 0 ? Math.Max(2, (double)(month.Amount / max) * MaxBarHeight) : 2;
            Months.Add(new MonthBar(month.Label, money.Format(month.Amount), height,
                month.Month.Year == today.Year && month.Month.Month == today.Month));
        }

        Overdue.Clear();
        foreach (var o in summary.Overdue)
        {
            var reminderText = o.RemindersSent == 0 ? "Aucun rappel envoyé" : $"{PaymentReminder.LevelLabel(o.RemindersSent)} envoyé";
            var action = o.NextReminderLevel is { } level ? $"Envoyer le {PaymentReminder.LevelLabel(level).ToLowerInvariant()}" : null;
            Overdue.Add(new OverdueRow(o.Invoice, $"{o.Invoice.ClientName} · {o.Invoice.DisplayNumber}",
                money.Format(o.Invoice.TotalInclTax), $"En retard de {Plural(o.DaysLate, "jour", "jours")}", reminderText, action));
        }

        UpdateVat(summary, money);

        var days = company.LastBackupAt is { } last ? (int)(today - last.ToLocalTime().Date).TotalDays : (int?)null;
        IsBackupOverdue = days is null or > 30;
        BackupText = days switch
        {
            null => "Aucune sauvegarde : exportez vos données depuis l'onglet Entreprise.",
            0 => "Dernière sauvegarde aujourd'hui.",
            _ => $"Dernière sauvegarde il y a {Plural(days.Value, "jour", "jours")}.",
        };
    }

    private void UpdateVat(DashboardSummary summary, MoneyFormatter money)
    {
        HasVatThreshold = summary.VatThreshold is not null;
        if (summary.VatThreshold is not { } threshold)
            return;

        VatProgress = Math.Min(1.0, (double)(summary.RevenueYear / threshold));
        IsVatAlert = summary.VatAlert != VatThresholdAlert.None;
        (VatTitle, VatTone) = summary.VatAlert switch
        {
            VatThresholdAlert.Exceeded => ("Seuil de TVA dépassé", "Danger"),
            VatThresholdAlert.Approaching => ("Vous approchez du seuil de TVA", "Warning"),
            _ => ("Seuil d'assujettissement à la TVA", "Success"),
        };
        VatText = summary.VatAlert switch
        {
            VatThresholdAlert.Exceeded =>
                $"Votre chiffre d'affaires {summary.Year} ({money.Format(summary.RevenueYear)}) dépasse {money.Format(threshold)}. " +
                "Vous devez probablement vous inscrire à la TVA : renseignez-vous auprès de l'administration fiscale ou de votre fiduciaire.",
            VatThresholdAlert.Approaching =>
                $"{money.Format(summary.RevenueYear)} sur {money.Format(threshold)} ({VatProgress:P0}). " +
                "Au-delà, l'inscription à la TVA devient obligatoire.",
            _ => $"{money.Format(summary.RevenueYear)} sur {money.Format(threshold)} ({VatProgress:P0}).",
        };
    }

    [RelayCommand]
    private Task OpenInvoiceAsync(OverdueRow? row) => row is null
        ? Task.CompletedTask
        : _navigation.GoToAsync(Routes.DocumentEdit, new Dictionary<string, object> { [Routes.IdParameter] = row.Invoice.Id });

    /// <summary>Relance directe depuis le tableau de bord : crée le rappel et ouvre son PDF.</summary>
    [RelayCommand]
    private Task RemindAsync(OverdueRow? row) => row is null || !row.CanRemind ? Task.CompletedTask : RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Rappel",
                $"{row.ActionText} pour la facture {row.Invoice.DisplayNumber} ({row.Invoice.ClientName}) ?", "Créer le rappel", "Annuler"))
            return;
        var invoice = await _documents.GetAsync(row.Invoice.Id) ?? row.Invoice;
        var reminder = await _reminders.CreateNextAsync(invoice);
        await _navigation.GoToAsync(Routes.DocumentPreview, new Dictionary<string, object>
        {
            [Routes.IdParameter] = invoice.Id,
            [Routes.ReminderParameter] = reminder.Id,
        });
    });

    /// <summary>Paiement reçu aujourd'hui pour une facture en retard.</summary>
    [RelayCommand]
    private Task MarkPaidAsync(OverdueRow? row) => row is null ? Task.CompletedTask : RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Paiement",
                $"Marquer la facture {row.Invoice.DisplayNumber} ({row.Invoice.ClientName}) comme payée aujourd'hui ?",
                "Payée", "Annuler"))
            return;
        var invoice = await _documents.GetAsync(row.Invoice.Id) ?? row.Invoice;
        await _service.MarkPaidAsync(invoice, _clock.GetLocalNow().Date);
        await OnAppearingAsync();
    });

    [RelayCommand]
    private Task NewQuoteAsync() => _navigation.GoToAsync(Routes.ClientPicker,
        new Dictionary<string, object> { [Routes.DocumentTypeParameter] = DocumentType.Quote });

    [RelayCommand]
    private Task NewInvoiceAsync() => _navigation.GoToAsync(Routes.ClientPicker,
        new Dictionary<string, object> { [Routes.DocumentTypeParameter] = DocumentType.Invoice });

    private static string Plural(int count, string singular, string plural) => $"{count} {(count > 1 ? plural : singular)}";
}
