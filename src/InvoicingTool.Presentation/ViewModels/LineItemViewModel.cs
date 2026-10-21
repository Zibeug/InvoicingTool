using CommunityToolkit.Mvvm.ComponentModel;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;

namespace InvoicingTool.Presentation.ViewModels;

/// <summary>
/// Ligne de devis / facture éditable. Les nombres sont saisis en texte pour accepter
/// la virgule comme le point, puis convertis vers le modèle.
/// </summary>
public partial class LineItemViewModel : ObservableObject
{
    public LineItemViewModel(DocumentLine line)
    {
        Line = line;
        _description = line.Description;
        _unit = line.Unit;
        _quantityText = DecimalParser.Format(line.Quantity);
        _unitPriceText = DecimalParser.Format(line.UnitPrice);
        _vatRateText = DecimalParser.Format(line.VatRate);
        _discountText = DecimalParser.Format(line.DiscountPercent);
    }

    public DocumentLine Line { get; }

    /// <summary>Déclenché quand un montant change, pour recalculer les totaux du document.</summary>
    public event EventHandler? AmountChanged;

    [ObservableProperty] private string _description;
    [ObservableProperty] private string _unit;
    [ObservableProperty] private string _quantityText;
    [ObservableProperty] private string _unitPriceText;
    [ObservableProperty] private string _vatRateText;
    [ObservableProperty] private string _discountText;

    public decimal TotalExclTax => DocumentCalculator.LineTotalExclTax(Line);

    public string TotalDisplay => HtmlDocumentRenderer.Money(TotalExclTax);

    public bool HasErrors =>
        !DecimalParser.TryParse(QuantityText, out _) || !DecimalParser.TryParse(UnitPriceText, out _) ||
        !DecimalParser.TryParse(VatRateText, out _) || !DecimalParser.TryParse(DiscountText, out _);

    partial void OnDescriptionChanged(string value) => Line.Description = value;
    partial void OnUnitChanged(string value) => Line.Unit = value;
    partial void OnQuantityTextChanged(string value) => UpdateAmount(() => Line.Quantity = DecimalParser.ParseOrZero(value));
    partial void OnUnitPriceTextChanged(string value) => UpdateAmount(() => Line.UnitPrice = DecimalParser.ParseOrZero(value));
    partial void OnVatRateTextChanged(string value) => UpdateAmount(() => Line.VatRate = DecimalParser.ParseOrZero(value));
    partial void OnDiscountTextChanged(string value) => UpdateAmount(() => Line.DiscountPercent = DecimalParser.ParseOrZero(value));

    private void UpdateAmount(Action apply)
    {
        apply();
        OnPropertyChanged(nameof(TotalExclTax));
        OnPropertyChanged(nameof(TotalDisplay));
        OnPropertyChanged(nameof(HasErrors));
        AmountChanged?.Invoke(this, EventArgs.Empty);
    }
}
