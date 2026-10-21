using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Informations de l'entreprise, pays (règles de TVA), devise et préférences de facturation.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settings;
    private CompanySettings _company = new();
    private bool _loading;

    public SettingsViewModel(SettingsRepository settings, IDialogService dialogs) : base(dialogs)
    {
        _settings = settings;
        Title = "Mon entreprise";
    }

    public IReadOnlyList<CountryProfile> Countries => CountryProfiles.All;
    public IReadOnlyList<Currency> CurrencyList => Currencies.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompanyIdLabel), nameof(VatNumberLabel), nameof(InsuranceLabel), nameof(TaxRateHint),
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
    [ObservableProperty] private string? _siret;
    [ObservableProperty] private string? _vatNumber;
    [ObservableProperty] private string? _insuranceInfo;
    [ObservableProperty] private string? _iban;
    [ObservableProperty] private bool _vatExempt;
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
    public string InsuranceLabel => SelectedCountry.InsuranceLabel;
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
            OwnerName = _company.OwnerName;
            Address = _company.Address;
            PostalCode = _company.PostalCode;
            City = _company.City;
            Phone = _company.Phone;
            Email = _company.Email;
            Siret = _company.Siret;
            VatNumber = _company.VatNumber;
            InsuranceInfo = _company.InsuranceInfo;
            Iban = _company.Iban;
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
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedCountryChanged(CountryProfile value)
    {
        if (!_loading)
            _ = ProposeCountryDefaultsAsync(value);
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

        _company.CountryCode = SelectedCountry.Code;
        _company.CurrencyCode = SelectedCurrency.Code;
        _company.CompanyName = CompanyName.Trim();
        _company.OwnerName = OwnerName;
        _company.Address = Address;
        _company.PostalCode = PostalCode;
        _company.City = City;
        _company.Phone = Phone;
        _company.Email = Email;
        _company.Siret = Siret;
        _company.VatNumber = VatNumber;
        _company.InsuranceInfo = InsuranceInfo;
        _company.Iban = Iban;
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
        await _settings.SaveAsync(_company);
        await Dialogs.AlertAsync("Paramètres", "Informations enregistrées.");
    });

    private static string FormatRates(IEnumerable<decimal> rates) => string.Join(" ; ", rates.Select(DecimalParser.Format));
}
