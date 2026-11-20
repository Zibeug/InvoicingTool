using System.Globalization;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation;

namespace InvoicingTool.App.Converters;

/// <summary>Libellé du statut d'un document (« Brouillon », « À payer »…).</summary>
public sealed class DocumentStatusTextConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is CommercialDocument doc ? DisplayText.StatusFor(doc, DateTime.Today) : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

public sealed class DocumentStatusColorConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is not CommercialDocument doc ? Colors.Gray : DisplayText.IsOverdue(doc, DateTime.Today) ? Color.FromArgb("#C62828") : doc.Status switch
		{
			DocumentStatus.Draft => Color.FromArgb("#8A8A8A"),
			DocumentStatus.Issued => Color.FromArgb("#E08A00"),
			DocumentStatus.Accepted or DocumentStatus.Paid => Color.FromArgb("#2E7D32"),
			_ => Color.FromArgb("#C62828"),
		};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

/// <summary>Image enregistrée en base (logo) vers une source affichable.</summary>
public sealed class BytesToImageSourceConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is byte[] { Length: > 0 } bytes ? ImageSource.FromStream(() => new MemoryStream(bytes)) : null;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

/// <summary>« #RRGGBB » vers une couleur (pastilles de choix de couleur).</summary>
public sealed class HexToColorConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is string hex && Color.TryParse(hex, out var color) ? color : Colors.Transparent;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

/// <summary>Vrai si le texte n'est pas vide (afficher une section seulement quand elle a du contenu).</summary>
public sealed class IsNotEmptyConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is string text && !string.IsNullOrWhiteSpace(text);

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

public sealed class InvertedBoolConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
