using System.Globalization;
using InvoicingTool.Core.Models;
using InvoicingTool.Presentation;

namespace InvoicingTool.App.Converters;

/// <summary>Libellé du statut d'un document (« Brouillon », « À payer »…).</summary>
public sealed class DocumentStatusTextConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is CommercialDocument doc ? DisplayText.Status(doc.Type, doc.Status) : string.Empty;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

public sealed class DocumentStatusColorConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is not CommercialDocument doc ? Colors.Gray : doc.Status switch
		{
			DocumentStatus.Draft => Color.FromArgb("#8A8A8A"),
			DocumentStatus.Issued => Color.FromArgb("#E08A00"),
			DocumentStatus.Accepted or DocumentStatus.Paid => Color.FromArgb("#2E7D32"),
			_ => Color.FromArgb("#C62828"),
		};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

public sealed class InvertedBoolConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
