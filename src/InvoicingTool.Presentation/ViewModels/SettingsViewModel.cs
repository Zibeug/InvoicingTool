using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Choix de thème proposé dans les paramètres.</summary>
public sealed record ThemeOption(ThemeChoice Choice, string Name);

/// <summary>Informations de l'entreprise, pays et devise, TVA, sauvegarde des données et conditions d'utilisation.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public const string BackupContentType = "application/json";

    private readonly SettingsRepository _settings;
    private readonly BackupService _backup;
    private readonly IShareService _share;
    private readonly IFilePickerService _filePicker;
    private readonly INavigationService _navigation;
    private readonly IImagePickerService _imagePicker;
    private readonly DocumentService _documents;
    private readonly AccountingExportService _export;
    private readonly IThemeService _theme;
    private readonly TimeProvider _clock;
    private CompanySettings _company = new();
    private int _loadedNextQuote;
    private int _loadedNextInvoice;
    private bool _loading;

    public SettingsViewModel(
        SettingsRepository settings,
        BackupService backup,
        IShareService share,
        IFilePickerService filePicker,
        IImagePickerService imagePicker,
        DocumentService documents,
        AccountingExportService export,
        IThemeService theme,
        INavigationService navigation,
        TimeProvider clock,
        IDialogService dialogs) : base(dialogs)
    {
        _imagePicker = imagePicker;
        _export = export;
        _theme = theme;
        _selectedTheme = ThemeOptions.First(o => o.Choice == theme.Current);
        _documents = documents;
        _clock = clock;
        _settings = settings;
        _backup = backup;
        _share = share;
        _filePicker = filePicker;
        _navigation = navigation;
        Title = "Mon entreprise";
    }

    public IReadOnlyList<CountryProfile> Countries => CountryProfiles.All;

    /// <summary>Thème propre à l'application (ne suit pas Windows ni Android).</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemeChoice.Light, "Clair"),
        new(ThemeChoice.Dark, "Sombre"),
    ];

    [ObservableProperty] private ThemeOption _selectedTheme;

    /// <summary>Appliqué et enregistré tout de suite, sans attendre « Enregistrer ».</summary>
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _theme.Apply(value.Choice);
        if (!_loading)
            _ = SaveThemeAsync(value.Choice);
    }

    private async Task SaveThemeAsync(ThemeChoice choice)
    {
        var current = await _settings.GetSettingsAsync();
        current.AppTheme = choice.ToString();
        await _settings.SaveAsync(current);
    }

    public static ThemeChoice ParseTheme(string? value) =>
        Enum.TryParse<ThemeChoice>(value, out var choice) ? choice : ThemeChoice.Light;
    public IReadOnlyList<Currency> CurrencyList => Currencies.All;

    /// <summary>Couleurs proposées pour les documents ; une couleur libre (#RRGGBB) peut aussi être saisie.</summary>
    public IReadOnlyList<string> AccentColors { get; } =
        ["#2B5797", "#1B3A66", "#00838F", "#2E7D32", "#F9A825", "#EF6C00", "#C62828", "#C2185B", "#6A1B9A", "#424242"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private byte[]? _logo;

    public bool HasLogo => Logo is { Length: > 0 };

    [ObservableProperty] private string _accentColor = CompanySettings.DefaultAccentColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextQuotePreview))]
    private string _nextQuoteNumberText = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextInvoicePreview))]
    private string _nextInvoiceNumberText = "1";

    private int Year => _clock.GetLocalNow().Year;

    public string NextQuotePreview => Preview(QuotePrefix, NextQuoteNumberText);
    public string NextInvoicePreview => Preview(InvoicePrefix, NextInvoiceNumberText);

    partial void OnQuotePrefixChanged(string value) => OnPropertyChanged(nameof(NextQuotePreview));
    partial void OnInvoicePrefixChanged(string value) => OnPropertyChanged(nameof(NextInvoicePreview));

    private string Preview(string prefix, string numberText) =>
        int.TryParse(numberText, out var n) && n > 0 ? $"Prochain numéro : {prefix}-{Year}-{n:D4}" : "Numéro invalide";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompanyIdLabel), nameof(VatNumberLabel), nameof(TaxRateHint),
        nameof(CompanyIdExample), nameof(VatNumberExample))]
    private CountryProfile _selectedCountry = CountryProfiles.France;

    [ObservableProperty] private Currency _selectedCurrency = Currencies.Get(Currencies.Default);

    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private string? _ownerName;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _postalCode;
    [ObservableProperty] private string? _city;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _companyId;
    [ObservableProperty] private string? _vatNumber;
    [ObservableProperty] private string? _iban;

    /// <summary>QR-facture suisse en bas des factures en CHF.</summary>
    [ObservableProperty] private bool _qrBillEnabled = true;
    [ObservableProperty] private string _qrBillStatus = string.Empty;
    [ObservableProperty] private bool _isQrBillReady;
    [ObservableProperty] private bool _isSwitzerland;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVatRegistered))]
    private bool _vatExempt;

    [ObservableProperty] private string _lastBackupDisplay = "Jamais";

    // Rappels de paiement
    [ObservableProperty] private string _reminderFee1Text = "0";
    [ObservableProperty] private string _reminderFee2Text = "0";
    [ObservableProperty] private string _reminderFee3Text = "0";
    [ObservableProperty] private string _reminderDelayDaysText = "10";

    /// <summary>Seuil d'assujettissement à la TVA pour l'alerte du tableau de bord (vide = pas d'alerte).</summary>
    [ObservableProperty] private string _vatThresholdText = string.Empty;

    // Export pour la fiduciaire
    public ObservableCollection<int> ExportYears { get; } = [];
    [ObservableProperty] private int _selectedExportYear;
    [ObservableProperty] private string _termsAcceptedDisplay = string.Empty;

    public bool IsVatRegistered => !VatExempt;
    [ObservableProperty] private string? _vatExemptionMention;
    [ObservableProperty] private string _taxRatesText = string.Empty;
    [ObservableProperty] private string _defaultVatRateText = "10";
    [ObservableProperty] private bool _roundToFiveCents;
    [ObservableProperty] private string _quoteValidityDaysText = "30";
    [ObservableProperty] private string _paymentTermDaysText = "30";
    [ObservableProperty] private string _quotePrefix = "D";
    [ObservableProperty] private string _invoicePrefix = "F";
    [ObservableProperty] private string? _paymentTerms;

    public string CompanyIdLabel => SelectedCountry.CompanyIdLabel;
    public string VatNumberLabel => SelectedCountry.VatNumberLabel;
    public string? TaxRateHint => SelectedCountry.TaxRateHint;
    public string? CompanyIdExample => SelectedCountry.CompanyIdExample;
    public string? VatNumberExample => SelectedCountry.VatNumberExample;

    public override async Task OnAppearingAsync()
    {
        _company = await _settings.GetSettingsAsync();
        _loading = true;
        try
        {
            SelectedCountry = _company.Country;
            SelectedCurrency = Currencies.Get(_company.CurrencyCode);
            CompanyName = _company.CompanyName;
            SelectedTheme = ThemeOptions.First(o => o.Choice == ParseTheme(_company.AppTheme));
            OwnerName = _company.OwnerName;
            Address = _company.Address;
            PostalCode = _company.PostalCode;
            City = _company.City;
            Phone = _company.Phone;
            Email = _company.Email;
            CompanyId = _company.CompanyId;
            VatNumber = _company.VatNumber;
            Iban = string.IsNullOrWhiteSpace(_company.Iban) ? null : Core.Services.Iban.Format(_company.Iban);
            QrBillEnabled = _company.QrBillEnabled;
            Logo = _company.Logo;
            AccentColor = ImageData.NormalizeColor(_company.AccentColor, CompanySettings.DefaultAccentColor);
            VatExempt = _company.VatExempt;
            VatExemptionMention = _company.VatExemptionMention;
            TaxRatesText = FormatRates(_company.TaxRates);
            DefaultVatRateText = DecimalParser.Format(_company.DefaultVatRate);
            RoundToFiveCents = _company.RoundingIncrement >= 0.05m;
            QuoteValidityDaysText = _company.QuoteValidityDays.ToString();
            PaymentTermDaysText = _company.PaymentTermDays.ToString();
            QuotePrefix = _company.QuotePrefix;
            InvoicePrefix = _company.InvoicePrefix;
            PaymentTerms = _company.PaymentTerms;
            ReminderFee1Text = DecimalParser.Format(_company.ReminderFee1);
            ReminderFee2Text = DecimalParser.Format(_company.ReminderFee2);
            ReminderFee3Text = DecimalParser.Format(_company.ReminderFee3);
            ReminderDelayDaysText = _company.ReminderDelayDays.ToString();
            var threshold = _company.VatRegistrationThreshold ?? _company.Country.VatRegistrationThreshold;
            VatThresholdText = threshold is { } t ? DecimalParser.Format(t) : string.Empty;
            _loadedNextQuote = await _documents.GetNextNumberAsync(DocumentType.Quote, Year);
            _loadedNextInvoice = await _documents.GetNextNumberAsync(DocumentType.Invoice, Year);
            NextQuoteNumberText = _loadedNextQuote.ToString();
            NextInvoiceNumberText = _loadedNextInvoice.ToString();
            LastBackupDisplay = _company.LastBackupAt is { } last ? last.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Jamais";
            TermsAcceptedDisplay = _company.TermsAcceptedAt is { } accepted
                ? $"Acceptées le {accepted.ToLocalTime():dd.MM.yyyy} (version {_company.TermsAcceptedVersion})"
                : "Non acceptées";
        }
        finally
        {
            _loading = false;
        }
        IsSwitzerland = SelectedCountry.Code == CountryProfiles.Switzerland.Code;
        UpdateQrBillStatus();

        var years = await _export.GetYearsAsync();
        ExportYears.Clear();
        foreach (var year in years)
            ExportYears.Add(year);
        SelectedExportYear = years[0];
    }

    partial void OnVatExemptChanged(bool value)
    {
        // Propose la mention légale du pays quand l'entreprise devient non assujettie.
        if (!_loading && value && string.IsNullOrWhiteSpace(VatExemptionMention))
            VatExemptionMention = SelectedCountry.TaxExemptionMention;
    }

    partial void OnSelectedCountryChanged(CountryProfile value)
    {
        IsSwitzerland = value.Code == CountryProfiles.Switzerland.Code;
        UpdateQrBillStatus();
        if (!_loading)
            _ = ProposeCountryDefaultsAsync(value);
    }

    partial void OnSelectedCurrencyChanged(Currency value) => UpdateQrBillStatus();
    partial void OnIbanChanged(string? value) => UpdateQrBillStatus();
    partial void OnCompanyNameChanged(string value) => UpdateQrBillStatus();
    partial void OnPostalCodeChanged(string? value) => UpdateQrBillStatus();
    partial void OnCityChanged(string? value) => UpdateQrBillStatus();
    partial void OnQrBillEnabledChanged(bool value) => UpdateQrBillStatus();

    /// <summary>Indique en direct si la QR-facture pourra être imprimée, ou ce qu'il manque.</summary>
    private void UpdateQrBillStatus()
    {
        var issues = SwissQrBill.CompanyIssues(new CompanySettings
        {
            CountryCode = SelectedCountry.Code,
            CurrencyCode = SelectedCurrency.Code,
            Iban = Iban,
            CompanyName = CompanyName,
            PostalCode = PostalCode,
            City = City,
        });
        IsQrBillReady = QrBillEnabled && issues.Count == 0;
        QrBillStatus = !QrBillEnabled
            ? "Désactivée : les factures n'auront pas de section paiement."
            : issues.Count == 0
                ? "Prête : chaque facture en CHF aura sa section paiement avec code QR."
                : "À compléter :\n• " + string.Join("\n• ", issues);
    }

    /// <summary>Quand l'utilisateur change de pays, propose d'appliquer la devise, les taux et les mentions de ce pays.</summary>
    public async Task ProposeCountryDefaultsAsync(CountryProfile profile)
    {
        var currency = Currencies.Get(profile.DefaultCurrency);
        var message = $"Appliquer les valeurs habituelles pour « {profile.Name} » ?\n\n" +
                      $"• Devise : {currency.DisplayName}\n" +
                      $"• Taux de {profile.TaxName} : {FormatRates(profile.TaxRates)} %\n" +
                      "• Mentions légales et conditions de paiement" +
                      (profile.RoundingIncrement >= 0.05m ? "\n• Arrondi du total à 0,05" : string.Empty);
        if (!await Dialogs.ConfirmAsync("Pays", message, "Appliquer", "Garder mes réglages"))
            return;

        SelectedCurrency = currency;
        TaxRatesText = FormatRates(profile.TaxRates);
        DefaultVatRateText = DecimalParser.Format(profile.DefaultTaxRate);
        VatExemptionMention = profile.TaxExemptionMention;
        PaymentTerms = profile.DefaultPaymentTerms;
        RoundToFiveCents = profile.RoundingIncrement >= 0.05m;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var rates = CompanySettings.ParseRates(TaxRatesText);
        if (!DecimalParser.TryParse(DefaultVatRateText, out var vat) || vat is < 0 or > 100 ||
            !int.TryParse(QuoteValidityDaysText, out var validity) || validity < 0 ||
            !int.TryParse(PaymentTermDaysText, out var term) || term < 0)
        {
            await Dialogs.AlertAsync("Paramètres", "Vérifiez le taux de TVA et les délais saisis.");
            return;
        }
        if (!VatExempt && rates.Count == 0)
        {
            await Dialogs.AlertAsync("Paramètres", "Indiquez au moins un taux de TVA (ex. « 8,1 ; 2,6 ; 3,8 »).");
            return;
        }
        if (string.IsNullOrWhiteSpace(QuotePrefix) || string.IsNullOrWhiteSpace(InvoicePrefix))
        {
            await Dialogs.AlertAsync("Paramètres", "Les préfixes de numérotation sont obligatoires.");
            return;
        }
        if (!int.TryParse(NextQuoteNumberText, out var nextQuote) || nextQuote < 1 ||
            !int.TryParse(NextInvoiceNumberText, out var nextInvoice) || nextInvoice < 1)
        {
            await Dialogs.AlertAsync("Paramètres", "Les numéros de départ doivent être des nombres entiers à partir de 1.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(Iban) && !Core.Services.Iban.IsValid(Iban))
        {
            await Dialogs.AlertAsync("Paramètres", "L'IBAN saisi n'est pas valide. Vérifiez qu'il n'y a pas de faute de frappe.");
            return;
        }
        if (!DecimalParser.TryParse(ReminderFee1Text, out var fee1) || !DecimalParser.TryParse(ReminderFee2Text, out var fee2) ||
            !DecimalParser.TryParse(ReminderFee3Text, out var fee3) || fee1 < 0 || fee2 < 0 || fee3 < 0 ||
            !int.TryParse(ReminderDelayDaysText, out var reminderDelay) || reminderDelay < 1)
        {
            await Dialogs.AlertAsync("Paramètres", "Vérifiez les frais de rappel et le délai (au moins 1 jour).");
            return;
        }
        decimal? vatThreshold = null;
        if (!string.IsNullOrWhiteSpace(VatThresholdText))
        {
            if (!DecimalParser.TryParse(VatThresholdText, out var parsedThreshold) || parsedThreshold < 0)
            {
                await Dialogs.AlertAsync("Paramètres", "Le seuil de TVA doit être un montant (ex. 100000).");
                return;
            }
            vatThreshold = parsedThreshold;
        }
        var accent = ImageData.NormalizeColor(AccentColor, string.Empty);
        if (accent.Length == 0)
        {
            await Dialogs.AlertAsync("Paramètres", "La couleur doit être au format #RRGGBB (ex. #2B5797).");
            return;
        }

        // Numérotation d'abord : un numéro refusé (doublon) n'enregistre rien d'autre.
        if (nextQuote != _loadedNextQuote)
            await _documents.SetNextNumberAsync(DocumentType.Quote, Year, nextQuote);
        _loadedNextQuote = nextQuote;
        if (nextInvoice != _loadedNextInvoice)
            await _documents.SetNextNumberAsync(DocumentType.Invoice, Year, nextInvoice);
        _loadedNextInvoice = nextInvoice;

        // Repart des réglages enregistrés : la date de sauvegarde ou l'acceptation des CGU
        // ont pu changer depuis l'ouverture de l'écran.
        _company = await _settings.GetSettingsAsync();
        _company.CountryCode = SelectedCountry.Code;
        _company.CurrencyCode = SelectedCurrency.Code;
        _company.CompanyName = CompanyName.Trim();
        _company.OwnerName = OwnerName;
        _company.Address = Address;
        _company.PostalCode = PostalCode;
        _company.City = City;
        _company.Phone = Phone;
        _company.Email = Email;
        _company.CompanyId = CompanyId;
        _company.VatNumber = VatNumber;
        _company.Iban = string.IsNullOrWhiteSpace(Iban) ? null : Core.Services.Iban.Normalize(Iban);
        Iban = _company.Iban is null ? null : Core.Services.Iban.Format(_company.Iban);
        _company.Logo = HasLogo ? Logo : null;
        _company.QrBillEnabled = QrBillEnabled;
        _company.AccentColor = accent;
        AccentColor = accent;
        _company.VatExempt = VatExempt;
        _company.VatExemptionMention = VatExemptionMention;
        _company.TaxRatesText = CompanySettings.FormatRates(rates);
        _company.DefaultVatRate = vat;
        _company.RoundingIncrement = RoundToFiveCents ? 0.05m : 0.01m;
        _company.QuoteValidityDays = validity;
        _company.PaymentTermDays = term;
        _company.QuotePrefix = QuotePrefix.Trim();
        _company.InvoicePrefix = InvoicePrefix.Trim();
        _company.PaymentTerms = PaymentTerms;
        _company.ReminderFee1 = fee1;
        _company.ReminderFee2 = fee2;
        _company.ReminderFee3 = fee3;
        _company.ReminderDelayDays = reminderDelay;
        // 0 = alerte désactivée ; vide = valeur habituelle du pays.
        _company.VatRegistrationThreshold = vatThreshold;
        await _settings.SaveAsync(_company);
        await Dialogs.AlertAsync("Paramètres", "Informations enregistrées.");
    });

    /// <summary>Exporte toutes les données dans un fichier à envoyer ou enregistrer hors du téléphone (Drive, e-mail…).</summary>
    [RelayCommand]
    private Task ExportBackupAsync() => RunAsync(async () =>
    {
        var content = await _backup.ExportAsync();
        await _share.ShareFileAsync(_backup.SuggestedFileName(), content, BackupContentType, "Sauvegarde des données");
        LastBackupDisplay = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
    });

    /// <summary>Remplace les données du téléphone par celles d'un fichier de sauvegarde.</summary>
    [RelayCommand]
    private Task RestoreBackupAsync() => RunAsync(async () =>
    {
        await using var stream = await _filePicker.PickBackupFileAsync();
        if (stream is null)
            return;

        var backup = BackupService.Read(stream);
        var summary = BackupService.Summarize(backup);
        var confirmed = await Dialogs.ConfirmAsync("Restaurer la sauvegarde",
            $"Sauvegarde du {summary.ExportedAt.ToLocalTime():dd.MM.yyyy HH:mm} : {summary.Clients} client(s), " +
            $"{summary.Documents} devis et facture(s), {summary.CatalogItems} article(s).\n\n" +
            "Toutes les données actuelles du téléphone seront remplacées. Pensez à les exporter d'abord si besoin.",
            "Remplacer", "Annuler");
        if (!confirmed)
            return;

        await _backup.RestoreAsync(backup);
        await OnAppearingAsync();
        await Dialogs.AlertAsync("Restaurer la sauvegarde", "Les données ont été restaurées.");
    });

    [RelayCommand]
    private Task PickLogoAsync() => RunAsync(async () =>
    {
        var image = await _imagePicker.PickLogoAsync();
        if (image is null)
            return;
        if (!ImageData.IsSupported(image))
        {
            await Dialogs.AlertAsync("Logo", "Format d'image non pris en charge (PNG ou JPEG).");
            return;
        }
        Logo = image;
    });

    [RelayCommand]
    private void RemoveLogo() => Logo = null;

    [RelayCommand]
    private void SelectColor(string? hex)
    {
        if (hex is not null)
            AccentColor = hex;
    }

    /// <summary>Fichier ZIP de l'année pour la fiduciaire : factures (CSV + PDF), TVA, journal Banana.</summary>
    [RelayCommand]
    private Task ExportAccountingAsync() => RunAsync(async () =>
    {
        var year = SelectedExportYear;
        var content = await _export.ExportYearAsync(year);
        await _share.ShareFileAsync(AccountingExportService.FileName(year), content, "application/zip", $"Export fiduciaire {year}");
    });

    [RelayCommand]
    private Task ShowTermsAsync() => _navigation.GoToAsync(Routes.Terms);

    private static string FormatRates(IEnumerable<decimal> rates) => string.Join(" ; ", rates.Select(DecimalParser.Format));
}
