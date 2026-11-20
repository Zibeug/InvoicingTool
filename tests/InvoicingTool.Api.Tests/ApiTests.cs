using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InvoicingTool.Sync.Contracts;

namespace InvoicingTool.Api.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.ch";

    private async Task<AuthResponse> RegisterAsync(HttpClient http, string email, string device = "Téléphone")
    {
        var response = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "motdepasse123", device));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static HttpClient As(HttpClient http, AuthResponse auth)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return http;
    }

    private static SyncChange Change(string type, Guid id, string name, bool deleted = false) =>
        new(type, id, DateTime.UtcNow, deleted, JsonSerializer.SerializeToElement(new { id, name }));

    [Fact]
    public async Task Health_check_answers()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_validates_input_and_refuses_duplicates()
    {
        var http = factory.CreateClient();
        var email = NewEmail();
        var auth = await RegisterAsync(http, email);
        Assert.Null(auth.SeriesLetter); // premier appareil : série principale

        var duplicate = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email.ToUpperInvariant(), "motdepasse123", "PC"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var shortPassword = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest(NewEmail(), "court", "PC"));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        Assert.Contains("au moins 8 caractères", await shortPassword.Content.ReadAsStringAsync());

        var badEmail = await http.PostAsJsonAsync("/api/auth/register", new RegisterRequest("pas-un-email", "motdepasse123", "PC"));
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
    }

    [Fact]
    public async Task Each_new_device_gets_its_own_series_and_known_devices_keep_theirs()
    {
        var http = factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(http, email);

        async Task<AuthResponse> Login(Guid? deviceId = null) => (await (await http.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "motdepasse123", "PC", deviceId))).Content.ReadFromJsonAsync<AuthResponse>())!;

        var pc = await Login();
        var tablet = await Login();
        var pcAgain = await Login(pc.DeviceId);

        Assert.Equal(("B", "C"), (pc.SeriesLetter, tablet.SeriesLetter));
        Assert.Equal((pc.DeviceId, "B"), (pcAgain.DeviceId, pcAgain.SeriesLetter));

        var wrong = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "mauvais-mdp", "PC"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_single_use_and_reuse_closes_the_device_sessions()
    {
        var http = factory.CreateClient();
        var auth = await RegisterAsync(http, NewEmail());

        var first = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var renewed = (await first.Content.ReadFromJsonAsync<AuthResponse>())!;

        // Réutiliser l'ancien jeton (vol possible) : refusé, et le nouveau jeton est révoqué aussi.
        var reuse = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        var afterTheft = await http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(renewed.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheft.StatusCode);
    }

    [Fact]
    public async Task Sync_requires_a_valid_token()
    {
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/sync/pull?since=0")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "faux-jeton");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/sync/pull?since=0")).StatusCode);
    }

    [Fact]
    public async Task Changes_flow_to_other_devices_of_the_same_account_only()
    {
        var email = NewEmail();
        var phone = As(factory.CreateClient(), await RegisterAsync(factory.CreateClient(), email));
        var pcAuth = (await (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "motdepasse123", "PC"))).Content.ReadFromJsonAsync<AuthResponse>())!;
        var pc = As(factory.CreateClient(), pcAuth);
        var stranger = As(factory.CreateClient(), await RegisterAsync(factory.CreateClient(), NewEmail()));

        var clientId = Guid.NewGuid();
        var push = await phone.PostAsJsonAsync("/api/sync/push", new PushRequest([
            Change(SyncEntityTypes.Client, clientId, "Brouillon du nom"),
            Change(SyncEntityTypes.Client, clientId, "Café du Marché"), // même élément deux fois : la dernière version compte
            Change(SyncEntityTypes.CatalogItem, Guid.NewGuid(), "Heure de conseil"),
        ]));
        Assert.Equal(new PushResponse(2, 2), await push.Content.ReadFromJsonAsync<PushResponse>());

        var onPc = (await pc.GetFromJsonAsync<PullResponse>("/api/sync/pull?since=0"))!;
        Assert.Equal(2, onPc.Changes.Count);
        Assert.Equal("Café du Marché", onPc.Changes.Single(c => c.EntityId == clientId).Payload.GetProperty("name").GetString());
        Assert.Equal(2, onPc.Version);

        var onPhone = (await phone.GetFromJsonAsync<PullResponse>("/api/sync/pull?since=0"))!;
        Assert.Empty(onPhone.Changes); // ses propres envois ne lui reviennent pas
        Assert.Equal(2, onPhone.Version);

        var forStranger = (await stranger.GetFromJsonAsync<PullResponse>("/api/sync/pull?since=0"))!;
        Assert.Empty(forStranger.Changes);

        // Suppression : propagée comme une modification
        await pc.PostAsJsonAsync("/api/sync/push", new PushRequest([Change(SyncEntityTypes.Client, clientId, "Café du Marché", deleted: true)]));
        var deleted = (await phone.GetFromJsonAsync<PullResponse>("/api/sync/pull?since=2"))!;
        Assert.True(Assert.Single(deleted.Changes).IsDeleted);
    }

    [Fact]
    public async Task Pull_is_paged()
    {
        var email = NewEmail();
        var phone = As(factory.CreateClient(), await RegisterAsync(factory.CreateClient(), email));
        var pc = As(factory.CreateClient(), (await (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "motdepasse123", "PC"))).Content.ReadFromJsonAsync<AuthResponse>())!);
        await phone.PostAsJsonAsync("/api/sync/push",
            new PushRequest(Enumerable.Range(1, 5).Select(i => Change(SyncEntityTypes.Client, Guid.NewGuid(), $"Client {i}")).ToList()));

        var page1 = (await pc.GetFromJsonAsync<PullResponse>("/api/sync/pull?since=0&limit=3"))!;
        var page2 = (await pc.GetFromJsonAsync<PullResponse>($"/api/sync/pull?since={page1.Version}&limit=3"))!;

        Assert.Equal((3, true, 3L), (page1.Changes.Count, page1.HasMore, page1.Version));
        Assert.Equal((2, false, 5L), (page2.Changes.Count, page2.HasMore, page2.Version));
    }

    [Fact]
    public async Task Unknown_types_and_oversized_batches_are_rejected()
    {
        var http = As(factory.CreateClient(), await RegisterAsync(factory.CreateClient(), NewEmail()));

        var unknown = await http.PostAsJsonAsync("/api/sync/push", new PushRequest([Change("virus", Guid.NewGuid(), "x")]));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var tooMany = await http.PostAsJsonAsync("/api/sync/push", new PushRequest(
            Enumerable.Range(0, SyncLimits.MaxBatchSize + 1).Select(_ => Change(SyncEntityTypes.Client, Guid.NewGuid(), "x")).ToList()));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_removes_everything()
    {
        var email = NewEmail();
        var http = As(factory.CreateClient(), await RegisterAsync(factory.CreateClient(), email));
        await http.PostAsJsonAsync("/api/sync/push", new PushRequest([Change(SyncEntityTypes.Client, Guid.NewGuid(), "x")]));

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync("/api/account")).StatusCode);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "motdepasse123", "PC"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}

/// <summary>API avec une limite basse de tentatives de connexion.</summary>
public class StrictRateLimitFactory : ApiFactory
{
    protected override int AuthRequestsPerMinute => 3;
}

public class RateLimitTests(StrictRateLimitFactory factory) : IClassFixture<StrictRateLimitFactory>
{
    [Fact]
    public async Task Too_many_login_attempts_are_refused()
    {
        var http = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await http.PostAsJsonAsync("/api/auth/login", new LoginRequest("x@example.ch", "mauvais-mdp", "PC"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
