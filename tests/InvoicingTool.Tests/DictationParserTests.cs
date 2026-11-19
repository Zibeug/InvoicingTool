using InvoicingTool.Core.Models;
using InvoicingTool.Core.Services;

namespace InvoicingTool.Tests;

public class DictationParserTests
{
    private static DictatedLine Single(string text, IReadOnlyList<CatalogItem>? catalog = null) =>
        Assert.Single(DictationParser.Parse(text, catalog));

    [Theory]
    [InlineData("3 heures de conseil à 120 francs", "Conseil", 3, "h", 120)]
    [InlineData("trois heures de conseil à cent vingt francs l'heure", "Conseil", 3, "h", 120)]
    [InlineData("un forfait déplacement à 50 francs", "Déplacement", 1, "forfait", 50)]
    [InlineData("2 coupes homme à 35 euros", "Coupes homme", 2, "u", 35)]
    [InlineData("Création du logo pour 950 francs", "Création du logo", 1, "u", 950)]
    [InlineData("4 heures de cours pour 200 euros au total", "Cours", 4, "h", 50)]
    [InlineData("12,5 mètres carrés de carrelage à 45 euros le mètre carré", "Carrelage", 12.5, "m²", 45)]
    [InlineData("deux heures et demie de montage à 80 € de l'heure", "Montage", 2.5, "h", 80)]
    [InlineData("frais de déplacement 90 kilomètres à 0,70 le kilomètre", "Frais de déplacement", 90, "km", 0.70)]
    [InlineData("Installation de la borne pour 1 200 francs", "Installation de la borne", 1, "u", 1200)]
    [InlineData("septante-cinq kilos de terreau à 2 francs 50 le kilo", "Terreau", 75, "kg", 2.50)]
    [InlineData("quatre-vingt-dix minutes de massage à 1,50 CHF la minute", "Massage", 90, "min", 1.50)]
    [InlineData("une demi-journée de formation à 400 euros", "Formation", 0.5, "jour", 400)]
    [InlineData("Peinture 2 couches à 30 euros", "Peinture 2 couches", 1, "u", 30)]
    [InlineData("réparation d'une fuite à 80 euros", "Réparation d'une fuite", 1, "u", 80)]
    [InlineData("vingt et une ampoules à 3 euros pièce", "Ampoules", 21, "pièce", 3)]
    [InlineData("quatre-vingts flyers à 0,20 €", "Flyers", 80, "u", 0.20)]
    [InlineData("deux cent cinquante grammes de café à 12 francs", "Grammes de café", 250, "u", 12)]
    [InlineData("1 nuit d'hôtel au prix de 140 francs", "Hôtel", 1, "nuit", 140)]
    [InlineData("10 séances de coaching à 90 francs HT", "Coaching", 10, "séance", 90)]
    [InlineData("5 jours de développement à 650 € par jour", "Développement", 5, "jour", 650)]
    [InlineData("1 200 flyers à 0,15 euros", "Flyers", 1200, "u", 0.15)]
    [InlineData("3 x nettoyage vitres à 45 francs", "Nettoyage vitres", 3, "u", 45)]
    public void Understands_common_dictations(string text, string description, double quantity, string unit, double price)
    {
        var line = Single(text);
        Assert.Equal(description, line.Description);
        Assert.Equal((decimal)quantity, line.Quantity);
        Assert.Equal(unit, line.Unit);
        Assert.Equal((decimal)price, line.UnitPrice);
    }

    [Fact]
    public void Several_lines_in_one_dictation()
    {
        var lines = DictationParser.Parse(
            "3 heures de conseil à 120 francs, puis un forfait déplacement à 50 francs. Ensuite deux rapports à 80 francs");

        Assert.Equal(3, lines.Count);
        Assert.Equal(("Conseil", 3m, 120m), (lines[0].Description, lines[0].Quantity, lines[0].UnitPrice!.Value));
        Assert.Equal(("Déplacement", 1m, 50m), (lines[1].Description, lines[1].Quantity, lines[1].UnitPrice!.Value));
        Assert.Equal(("Rapports", 2m, 80m), (lines[2].Description, lines[2].Quantity, lines[2].UnitPrice!.Value));
    }

    [Fact]
    public void Line_without_price_is_kept_with_missing_price()
    {
        var line = Single("2 heures de rangement");
        Assert.Equal("Rangement", line.Description);
        Assert.Null(line.UnitPrice);
    }

    [Fact]
    public void Catalog_article_supplies_label_price_unit_and_vat()
    {
        var catalog = new List<CatalogItem>
        {
            new() { Label = "Heure de conseil", Unit = "h", UnitPrice = 120m, VatRate = 8.1m },
            new() { Label = "Forfait déplacement", Unit = "forfait", UnitPrice = 50m, VatRate = 8.1m },
            new() { Label = "Coupe homme", Unit = "u", UnitPrice = 35m, VatRate = 8.1m },
        };

        var lines = DictationParser.Parse("3 heures de conseil puis un déplacement puis deux coupes hommes à 30 francs", catalog);

        Assert.Equal(3, lines.Count);
        Assert.Equal(("Heure de conseil", 3m, "h", 120m), (lines[0].Description, lines[0].Quantity, lines[0].Unit, lines[0].UnitPrice!.Value));
        Assert.Equal(8.1m, lines[0].VatRate);
        Assert.Equal(("Forfait déplacement", 1m, "forfait", 50m), (lines[1].Description, lines[1].Quantity, lines[1].Unit, lines[1].UnitPrice!.Value));
        // Un prix dit à voix haute l'emporte sur le prix du catalogue.
        Assert.Equal(("Coupe homme", 2m, 30m), (lines[2].Description, lines[2].Quantity, lines[2].UnitPrice!.Value));
    }

    [Fact]
    public void Unrelated_catalog_articles_are_not_matched()
    {
        var catalog = new List<CatalogItem> { new() { Label = "Heure de conseil", UnitPrice = 120m } };
        var line = Single("2 heures de ménage à 30 francs", catalog);
        Assert.Null(line.CatalogItem);
        Assert.Equal("Ménage", line.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("puis, ensuite.")]
    public void Empty_dictation_gives_no_line(string text) => Assert.Empty(DictationParser.Parse(text));
}
