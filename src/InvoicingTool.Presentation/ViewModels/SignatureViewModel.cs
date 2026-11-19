using CommunityToolkit.Mvvm.ComponentModel;
using InvoicingTool.Core.Data;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;
using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>Signature du devis par le client, au doigt sur l'écran du téléphone.</summary>
public partial class SignatureViewModel(
    DocumentRepository documents,
    DocumentService service,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase(dialogs), IParameterReceiver
{
    private Guid _documentId;
    private CommercialDocument? _quote;

    [ObservableProperty] private string _signerName = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _acceptanceText = string.Empty;

    public void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(Routes.IdParameter, out var id))
            _documentId = (Guid)id;
    }

    public override async Task OnAppearingAsync()
    {
        _quote = await documents.GetAsync(_documentId);
        if (_quote is null)
        {
            await navigation.GoBackAsync();
            return;
        }

        Title = $"Signature du devis {_quote.DisplayNumber}";
        Summary = $"Devis n° {_quote.DisplayNumber} — {_quote.ClientName} — {_quote.TotalInclTaxDisplay}";
        AcceptanceText = CountryProfiles.Get(_quote.CountryCode).QuoteAcceptanceText;
        if (string.IsNullOrWhiteSpace(SignerName))
            SignerName = _quote.ClientName;
    }

    /// <summary>Enregistre la signature dessinée (image PNG) ; vide ou absente = refus avec message.</summary>
    public Task SaveAsync(byte[]? signaturePng) => RunAsync(async () =>
    {
        if (_quote is null)
            return;
        if (signaturePng is not { Length: > 0 })
        {
            await Dialogs.AlertAsync("Signature", "Le client doit signer dans le cadre.");
            return;
        }
        if (string.IsNullOrWhiteSpace(SignerName))
        {
            await Dialogs.AlertAsync("Signature", "Indiquez le nom du signataire.");
            return;
        }

        await service.SignQuoteAsync(_quote, signaturePng, SignerName);
        await navigation.GoBackAsync();
    });
}
