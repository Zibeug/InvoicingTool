using InvoicingTool.Core.Models;

namespace InvoicingTool.Tests;

public class RepositoryTests
{
    [Fact]
    public async Task Client_round_trips_and_search_matches_name_and_city()
    {
        await using var t = new TestDatabase();
        await t.Clients.SaveAsync(new Client { Name = "Martin Dupont", City = "Lyon" });
        await t.Clients.SaveAsync(new Client { Name = "Boulangerie Petit", City = "Villeurbanne" });

        Assert.Equal(2, (await t.Clients.SearchAsync(null)).Count);
        Assert.Equal("Martin Dupont", Assert.Single(await t.Clients.SearchAsync("lyon")).Name);
        Assert.Equal("Boulangerie Petit", Assert.Single(await t.Clients.SearchAsync("PETIT")).Name);
    }

    [Fact]
    public async Task Delete_is_logical_and_kept_as_pending_change_for_sync()
    {
        await using var t = new TestDatabase();
        var client = new Client { Name = "À supprimer" };
        await t.Clients.SaveAsync(client);

        await t.Clients.DeleteAsync(client);

        Assert.Null(await t.Clients.GetAsync(client.Id));
        Assert.Empty(await t.Clients.GetAllAsync());
        var pending = Assert.Single(await t.Clients.GetPendingChangesAsync());
        Assert.True(pending.IsDeleted);
    }

    [Fact]
    public async Task Save_updates_modification_date()
    {
        await using var t = new TestDatabase();
        var client = new Client { Name = "A" };
        await t.Clients.SaveAsync(client);
        t.Clock.Advance(TimeSpan.FromHours(2));

        client.Name = "B";
        await t.Clients.SaveAsync(client);

        var reloaded = await t.Clients.GetAsync(client.Id);
        Assert.Equal(new DateTime(2026, 3, 15, 12, 0, 0), reloaded!.UpdatedAt);
        Assert.Equal("B", reloaded.Name);
    }

    [Fact]
    public async Task Decimal_amounts_survive_storage()
    {
        await using var t = new TestDatabase();
        var item = new CatalogItem { Label = "Peinture murs", Unit = "m²", UnitPrice = 23.45m, VatRate = 5.5m };
        await t.Catalog.SaveAsync(item);

        var reloaded = await t.Catalog.GetAsync(item.Id);
        Assert.Equal(23.45m, reloaded!.UnitPrice);
        Assert.Equal(5.5m, reloaded.VatRate);
        Assert.Equal("m²", reloaded.Unit);
    }

    [Fact]
    public async Task Settings_default_when_never_saved()
    {
        await using var t = new TestDatabase();
        var settings = await t.Settings.GetSettingsAsync();
        Assert.Equal("F", settings.InvoicePrefix);

        settings.CompanyName = "Peinture Durand";
        await t.Settings.SaveAsync(settings);
        Assert.Equal("Peinture Durand", (await t.Settings.GetSettingsAsync()).CompanyName);
    }
}
