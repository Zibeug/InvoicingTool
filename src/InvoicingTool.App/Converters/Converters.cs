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
		value is not CommercialDocument doc ? Colors.Gray
		: DisplayText.IsOverdue(doc, DateTime.Today) ? ThemeColors.Get("Danger")
		: doc.Status switch
		{
			DocumentStatus.Draft => Colors.Gray,
			DocumentStatus.Issued => ThemeColors.Get("Warning"),
			DocumentStatus.Accepted or DocumentStatus.Paid => ThemeColors.Get("Success"),
			_ => ThemeColors.Get("Danger"),
		};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

/// <summary>Ton d'un état (« Success », « Warning », « Danger ») vers sa couleur dans le thème actif.</summary>
public sealed class ToneToColorConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is string tone ? ThemeColors.Get(tone) : Colors.Gray;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

/// <summary>Couleurs d'état de Colors.xaml, en version sombre quand l'application est en mode sombre.</summary>
internal static class ThemeColors
{
	public static Color Get(string name)
	{
		var app = Application.Current;
		var key = app?.RequestedTheme == AppTheme.Dark ? name + "Dark" : name;
		return app?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : Colors.Gray;
	}
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
