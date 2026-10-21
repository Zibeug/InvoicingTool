using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Informations de l'entreprise et préférences de facturation.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settings;
    private CompanySettings _company = new();

    public SettingsViewModel(SettingsRepository settings, IDialogService dialogs) : base(dialogs)
    {
        _settings = settings;
        Title = "Mon entreprise";
    }

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
    [ObservableProperty] private string _defaultVatRateText = "10";
    [ObservableProperty] private string _quoteValidityDaysText = "30";
    [ObservableProperty] private string _paymentTermDaysText = "30";
    [ObservableProperty] private string _quotePrefix = "D";
    [ObservableProperty] private string _invoicePrefix = "F";
    [ObservableProperty] private string? _paymentTerms;

    public override async Task OnAppearingAsync()
    {
        _company = await _settings.GetSettingsAsync();
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
        DefaultVatRateText = DecimalParser.Format(_company.DefaultVatRate);
        QuoteValidityDaysText = _company.QuoteValidityDays.ToString();
        PaymentTermDaysText = _company.PaymentTermDays.ToString();
        QuotePrefix = _company.QuotePrefix;
        InvoicePrefix = _company.InvoicePrefix;
        PaymentTerms = _company.PaymentTerms;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (!DecimalParser.TryParse(DefaultVatRateText, out var vat) ||
            !int.TryParse(QuoteValidityDaysText, out var validity) || validity < 0 ||
            !int.TryParse(PaymentTermDaysText, out var term) || term < 0)
        {
            await Dialogs.AlertAsync("Paramètres", "Vérifiez le taux de TVA et les délais saisis.");
            return;
        }
        if (string.IsNullOrWhiteSpace(QuotePrefix) || string.IsNullOrWhiteSpace(InvoicePrefix))
        {
            await Dialogs.AlertAsync("Paramètres", "Les préfixes de numérotation sont obligatoires.");
            return;
        }

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
        _company.DefaultVatRate = vat;
        _company.QuoteValidityDays = validity;
        _company.PaymentTermDays = term;
        _company.QuotePrefix = QuotePrefix.Trim();
        _company.InvoicePrefix = InvoicePrefix.Trim();
        _company.PaymentTerms = PaymentTerms;
        await _settings.SaveAsync(_company);
        await Dialogs.AlertAsync("Paramètres", "Informations enregistrées.");
    });
}
