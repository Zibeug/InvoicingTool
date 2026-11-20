using System.Net.Http.Json;
using InvoicingTool.Core.Models;
using InvoicingTool.Core.Sync;
using InvoicingTool.Sync.Contracts;
using InvoicingTool.Tests;

namespace InvoicingTool.Api.Tests;

/// <summary>Jetons en mémoire (le stockage sécurisé du téléphone dans l'application).</summary>
public sealed class MemoryTokenStore : ITokenStore
{
    private readonly Dictionary<string, string> _values = [];
    public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));
    public Task SetAsync(string key, string value) { _values[key] = value; return Task.CompletedTask; }
    public void Remove(string key) => _values.Remove(key);
}

/// <summary>Un appareil : sa propre base locale, son client d'API et son moteur de synchronisation.</summary>
public sealed class Device : IAsyncDisposable
{
    public const string Server = "http://localhost/";

    public Device(ApiFactory factory)
    {
        Tokens = new MemoryTokenStore();
        Api = new SyncApiClient(factory.CreateClient(), Tokens, Db.SyncState);
        Engine = new SyncEngine(Db.Database, Api, Db.SyncState, Db.Clock);
    }

    public TestDatabase Db { get; } = new();
    public MemoryTokenStore Tokens { get; }
    public SyncApiClient Api { get; }
    public SyncEngine Engine { get; }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

public class SyncEndToEndTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.ch";
    private const string Password = "motdepasse123";

    private static async Task<CommercialDocument> IssueInvoiceAsync(TestDatabase db, Client client, decimal amount)
    {
        var invoice = await db.Service.CreateDraftAsync(DocumentType.Invoice, client);
        await db.Service.SaveDraftAsync(invoice, [new() { Description = "Mandat", Quantity = 1, UnitPrice = amount, VatRate = 20m }]);
        await db.Service.IssueAsync(invoice);
        return invoice;
    }

    [Fact]
    public async Task Data_created_on_the_phone_appears_on_the_pc_and_back()
    {
        await using var phone = new Device(factory);
        await using var pc = new Device(factory);
        var email = NewEmail();

        // Téléphone : données existantes, puis création du compte et première synchronisation.
        await phone.Db.Settings.SaveAsync(new CompanySettings { CompanyName = "Atelier Nova", Iban = "CH9300762011623852957",
            TermsAcceptedVersion = TermsOfUse.Version });
        var client = new Client { Name = "Café du Marché", City = "Genève" };
        await phone.Db.Clients.SaveAsync(client);
        var invoice = await IssueInvoiceAsync(phone.Db, client, 1000m);
        await phone.Api.RegisterAsync(Device.Server, email, Password, "Téléphone");
        var first = await phone.Engine.SyncAsync();
        Assert.True(first.Sent >= 4); // paramètres, client, facture, ligne

        // PC : nouvel appareil (conditions acceptées localement), connexion puis synchronisation.
        await pc.Db.Settings.SaveAsync(new CompanySettings { TermsAcceptedVersion = TermsOfUse.Version });
        var auth = await pc.Api.LoginAsync(Device.Server, email, Password, "PC du bureau");
        Assert.Equal("B", auth.SeriesLetter);
        await pc.Engine.SyncAsync();

        var pcSettings = await pc.Db.Settings.GetSettingsAsync();
        Assert.Equal(("Atelier Nova", "CH9300762011623852957"), (pcSettings.CompanyName, pcSettings.Iban));
        Assert.Equal(TermsOfUse.Version, pcSettings.TermsAcceptedVersion); // acceptation propre à l'appareil, conservée
        Assert.Equal("Café du Marché", Assert.Single(await pc.Db.Clients.GetAllAsync()).Name);
        var pcInvoice = (await pc.Db.Documents.GetAsync(invoice.Id))!;
        Assert.Equal(("F-2026-0001", 1200m), (pcInvoice.Number, pcInvoice.TotalInclTax));
        Assert.Single(await pc.Db.Service.GetLinesAsync(invoice.Id));

        // Le PC émet une facture : série B, impossible d'entrer en collision avec le téléphone.
        var pcClient = (await pc.Db.Clients.GetAllAsync()).Single();
        var fromPc = await IssueInvoiceAsync(pc.Db, pcClient, 300m);
        Assert.Equal("F-2026-B-0001", fromPc.Number);
        pcClient.Name = "Café du Marché Sàrl";
        await pc.Db.Clients.SaveAsync(pcClient);
        await pc.Engine.SyncAsync();

        // Retour sur le téléphone
        await phone.Engine.SyncAsync();
        Assert.Equal("Café du Marché Sàrl", (await phone.Db.Clients.GetAsync(client.Id))!.Name);
        Assert.Equal("F-2026-B-0001", (await phone.Db.Documents.GetAsync(fromPc.Id))!.Number);
        // Le téléphone continue sa propre série.
        var next = await IssueInvoiceAsync(phone.Db, client, 50m);
        Assert.Equal("F-2026-0002", next.Number);
    }

    [Fact]
    public async Task Deletions_payments_and_signatures_are_synchronized()
    {
        await using var phone = new Device(factory);
        await using var pc = new Device(factory);
        var email = NewEmail();
        await phone.Api.RegisterAsync(Device.Server, email, Password, "Téléphone");
        await pc.Api.LoginAsync(Device.Server, email, Password, "PC");

        var client = new Client { Name = "Studio Lumen" };
        await phone.Db.Clients.SaveAsync(client);
        var temporary = new Client { Name = "Client en double" };
        await phone.Db.Clients.SaveAsync(temporary);
        var invoice = await IssueInvoiceAsync(phone.Db, client, 400m);
        await phone.Engine.SyncAsync();
        await pc.Engine.SyncAsync();
        Assert.Equal(2, (await pc.Db.Clients.GetAllAsync()).Count);

        await phone.Db.Clients.DeleteAsync(temporary);
        await phone.Db.Service.MarkPaidAsync(invoice, invoice.IssueDate);
        await phone.Engine.SyncAsync();
        await pc.Engine.SyncAsync();

        Assert.Equal("Studio Lumen", Assert.Single(await pc.Db.Clients.GetAllAsync()).Name);
        Assert.Equal(DocumentStatus.Paid, (await pc.Db.Documents.GetAsync(invoice.Id))!.Status);
    }

    [Fact]
    public async Task Unsent_local_change_is_never_overwritten_and_last_sync_wins()
    {
        await using var phone = new Device(factory);
        await using var pc = new Device(factory);
        var email = NewEmail();
        await phone.Api.RegisterAsync(Device.Server, email, Password, "Téléphone");
        await pc.Api.LoginAsync(Device.Server, email, Password, "PC");
        var client = new Client { Name = "Version initiale" };
        await phone.Db.Clients.SaveAsync(client);
        await phone.Engine.SyncAsync();
        await pc.Engine.SyncAsync();

        // Les deux appareils modifient le même client hors ligne.
        var onPhone = (await phone.Db.Clients.GetAsync(client.Id))!;
        onPhone.Name = "Modifié sur le téléphone";
        await phone.Db.Clients.SaveAsync(onPhone);
        var onPc = (await pc.Db.Clients.GetAsync(client.Id))!;
        onPc.Name = "Modifié sur le PC";
        await pc.Db.Clients.SaveAsync(onPc);

        await phone.Engine.SyncAsync();
        await pc.Engine.SyncAsync();     // le PC n'écrase pas sa modification non envoyée : il l'envoie, elle l'emporte
        await phone.Engine.SyncAsync();

        Assert.Equal("Modifié sur le PC", (await pc.Db.Clients.GetAsync(client.Id))!.Name);
        Assert.Equal("Modifié sur le PC", (await phone.Db.Clients.GetAsync(client.Id))!.Name);
    }

    [Fact]
    public async Task Expired_session_disables_sync_with_a_clear_message()
    {
        await using var phone = new Device(factory);
        await phone.Api.RegisterAsync(Device.Server, NewEmail(), Password, "Téléphone");
        // Session fermée côté serveur (ex. jeton volé, appareil retiré) : les jetons locaux ne valent plus rien.
        var refresh = (await phone.Tokens.GetAsync(SyncApiClient.RefreshTokenKey))!;
        await factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new RefreshRequest(refresh));
        phone.Tokens.Remove(SyncApiClient.AccessTokenKey);

        var error = await Assert.ThrowsAsync<SyncException>(() => phone.Engine.SyncAsync());

        Assert.True(error.RequiresLogin);
        var state = await phone.Db.SyncState.GetAsync();
        Assert.False(state.IsEnabled);
        Assert.Contains("reconnectez", state.LastError);
    }

    [Fact]
    public async Task Wrong_password_and_unreachable_server_give_readable_errors()
    {
        await using var phone = new Device(factory);
        var email = NewEmail();
        await phone.Api.RegisterAsync(Device.Server, email, Password, "Téléphone");

        var wrong = await Assert.ThrowsAsync<SyncException>(() => phone.Api.LoginAsync(Device.Server, email, "mauvais-mdp", "PC"));
        Assert.Equal("Adresse e-mail ou mot de passe incorrect.", wrong.Message);

        var offline = new SyncApiClient(new HttpClient(), new MemoryTokenStore(), phone.Db.SyncState);
        var unreachable = await Assert.ThrowsAsync<SyncException>(() =>
            offline.LoginAsync("http://127.0.0.1:1", email, Password, "PC"));
        Assert.StartsWith("Serveur injoignable", unreachable.Message);
    }
}
