using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InvoicingTool.Core.Models;
using InvoicingTool.Sync.Contracts;

namespace InvoicingTool.Core.Sync;

/// <summary>Erreur de synchronisation, avec un message compréhensible par l'utilisateur.</summary>
public sealed class SyncException(string message, bool requiresLogin = false, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>La session n'est plus valable : il faut se reconnecter.</summary>
    public bool RequiresLogin { get; } = requiresLogin;
}

/// <summary>
/// Appels à l'API de synchronisation. Le jeton d'accès (court) est renouvelé automatiquement avec le jeton
/// de renouvellement, conservé dans le stockage sécurisé de l'appareil.
/// </summary>
public class SyncApiClient(HttpClient http, ITokenStore tokens, SyncStateStore states)
{
    public const string AccessTokenKey = "sync.accessToken";
    public const string AccessTokenExpiryKey = "sync.accessTokenExpiry";
    public const string RefreshTokenKey = "sync.refreshToken";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<AuthResponse> RegisterAsync(string serverUrl, string email, string password, string deviceName) =>
        AuthenticateAsync(serverUrl, "api/auth/register", new RegisterRequest(email.Trim(), password, deviceName));

    public async Task<AuthResponse> LoginAsync(string serverUrl, string email, string password, string deviceName)
    {
        // Même appareil qui se reconnecte (même serveur, même compte) : il garde sa série de numérotation.
        var state = await states.GetAsync().ConfigureAwait(false);
        var knownDevice = state.DeviceId is { } id && SameServer(state.ServerUrl, serverUrl) &&
                          string.Equals(state.Email, email.Trim(), StringComparison.OrdinalIgnoreCase) ? id : (Guid?)null;
        return await AuthenticateAsync(serverUrl, "api/auth/login", new LoginRequest(email.Trim(), password, deviceName, knownDevice))
            .ConfigureAwait(false);
    }

    /// <summary>Ferme la session sur le serveur (si possible) et oublie les jetons.</summary>
    public async Task LogoutAsync()
    {
        var state = await states.GetAsync().ConfigureAwait(false);
        if (state.ServerUrl is not null && await tokens.GetAsync(RefreshTokenKey).ConfigureAwait(false) is { } refresh)
        {
            try
            {
                using var _ = await http.PostAsJsonAsync(Url(state.ServerUrl, "api/auth/logout"), new RefreshRequest(refresh), Json)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Hors ligne : la session expirera d'elle-même sur le serveur.
            }
        }
        ForgetTokens();
        state.IsEnabled = false;
        await states.SaveAsync(state).ConfigureAwait(false);
    }

    public async Task<PushResponse> PushAsync(IReadOnlyList<SyncChange> changes) =>
        await SendAuthorizedAsync<PushResponse>(server => new HttpRequestMessage(HttpMethod.Post, Url(server, "api/sync/push"))
        {
            Content = JsonContent.Create(new PushRequest(changes), options: Json),
        }).ConfigureAwait(false);

    public async Task<PullResponse> PullAsync(long since) =>
        await SendAuthorizedAsync<PullResponse>(server =>
            new HttpRequestMessage(HttpMethod.Get, Url(server, $"api/sync/pull?since={since}"))).ConfigureAwait(false);

    private async Task<AuthResponse> AuthenticateAsync<TRequest>(string serverUrl, string path, TRequest request)
    {
        var server = NormalizeServer(serverUrl);
        var auth = await ReadAsync<AuthResponse>(() => http.PostAsJsonAsync(Url(server, path), request, Json)).ConfigureAwait(false);
        await StoreTokensAsync(auth).ConfigureAwait(false);

        var state = await states.GetAsync().ConfigureAwait(false);
        var sameAccount = state.AccountId == auth.AccountId && SameServer(state.ServerUrl, server);
        state.IsEnabled = true;
        state.ServerUrl = server;
        state.Email = request switch { RegisterRequest r => r.Email, LoginRequest l => l.Email, _ => state.Email };
        state.AccountId = auth.AccountId;
        state.DeviceId = auth.DeviceId;
        state.DeviceName = request switch { RegisterRequest r => r.DeviceName, LoginRequest l => l.DeviceName, _ => state.DeviceName };
        state.SeriesLetter = auth.SeriesLetter;
        state.LastError = null;
        if (!sameAccount)
        {
            // Nouveau compte ou autre serveur : tout l'historique est à recevoir.
            state.LastPulledVersion = 0;
            state.LastSyncAt = null;
        }
        await states.SaveAsync(state).ConfigureAwait(false);
        return auth;
    }

    private async Task<T> SendAuthorizedAsync<T>(Func<string, HttpRequestMessage> createRequest)
    {
        var state = await states.GetAsync().ConfigureAwait(false);
        if (!state.IsEnabled || state.ServerUrl is null)
            throw new SyncException("La synchronisation n'est pas activée sur cet appareil.", requiresLogin: true);

        var token = await ValidAccessTokenAsync(state.ServerUrl).ConfigureAwait(false);
        var result = await TrySendAsync<T>(createRequest(state.ServerUrl), token).ConfigureAwait(false);
        if (result.Unauthorized)
        {
            // Jeton refusé (expiré plus tôt que prévu, clé du serveur changée…) : un renouvellement, puis un essai.
            token = await RefreshAsync(state.ServerUrl).ConfigureAwait(false);
            result = await TrySendAsync<T>(createRequest(state.ServerUrl), token).ConfigureAwait(false);
            if (result.Unauthorized)
                throw new SyncException("Session expirée : reconnectez-vous.", requiresLogin: true);
        }
        return result.Value!;
    }

    private async Task<(bool Unauthorized, T? Value)> TrySendAsync<T>(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using (request)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new SyncException("Serveur injoignable : vérifiez la connexion internet et l'adresse du serveur.", inner: ex);
            }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return (true, default);
                return (false, await ReadBodyAsync<T>(response).ConfigureAwait(false));
            }
        }
    }

    private async Task<string> ValidAccessTokenAsync(string server)
    {
        var token = await tokens.GetAsync(AccessTokenKey).ConfigureAwait(false);
        var expiry = await tokens.GetAsync(AccessTokenExpiryKey).ConfigureAwait(false);
        var stillValid = token is not null && DateTime.TryParse(expiry, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt) &&
                         expiresAt > DateTime.UtcNow.AddMinutes(1);
        return stillValid ? token! : await RefreshAsync(server).ConfigureAwait(false);
    }

    private async Task<string> RefreshAsync(string server)
    {
        var refresh = await tokens.GetAsync(RefreshTokenKey).ConfigureAwait(false)
            ?? throw new SyncException("Session expirée : reconnectez-vous.", requiresLogin: true);
        try
        {
            var auth = await ReadAsync<AuthResponse>(() => http.PostAsJsonAsync(Url(server, "api/auth/refresh"), new RefreshRequest(refresh), Json))
                .ConfigureAwait(false);
            await StoreTokensAsync(auth).ConfigureAwait(false);
            return auth.AccessToken;
        }
        catch (SyncException ex) when (ex.Message.Contains("reconnectez", StringComparison.OrdinalIgnoreCase))
        {
            ForgetTokens();
            throw new SyncException(ex.Message, requiresLogin: true, ex);
        }
    }

    private async Task<T> ReadAsync<T>(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage response;
        try
        {
            response = await send().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            throw new SyncException("Serveur injoignable : vérifiez la connexion internet et l'adresse du serveur.", inner: ex);
        }
        using (response)
            return await ReadBodyAsync<T>(response).ConfigureAwait(false);
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<T>(Json).ConfigureAwait(false))!;

        // Message d'erreur du serveur (ProblemDetails « detail »), sinon message générique.
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("detail", out var d))
                detail = d.GetString();
        }
        catch (JsonException)
        {
        }
        throw response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new SyncException("Trop de tentatives : réessayez dans une minute."),
            HttpStatusCode.Unauthorized => new SyncException(detail ?? "Session expirée : reconnectez-vous.", requiresLogin: true),
            _ => new SyncException(detail ?? $"Erreur du serveur ({(int)response.StatusCode})."),
        };
    }

    private async Task StoreTokensAsync(AuthResponse auth)
    {
        await tokens.SetAsync(AccessTokenKey, auth.AccessToken).ConfigureAwait(false);
        await tokens.SetAsync(AccessTokenExpiryKey, auth.AccessTokenExpiresAt.ToUniversalTime().ToString("O")).ConfigureAwait(false);
        await tokens.SetAsync(RefreshTokenKey, auth.RefreshToken).ConfigureAwait(false);
    }

    private void ForgetTokens()
    {
        tokens.Remove(AccessTokenKey);
        tokens.Remove(AccessTokenExpiryKey);
        tokens.Remove(RefreshTokenKey);
    }

    /// <summary>« monserveur.ch » → « https://monserveur.ch/ ».</summary>
    public static string NormalizeServer(string url)
    {
        var text = url.Trim();
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            throw new SyncException("Adresse du serveur invalide.");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }

    private static bool SameServer(string? a, string? b) =>
        a is not null && b is not null && string.Equals(NormalizeServer(a), NormalizeServer(b), StringComparison.OrdinalIgnoreCase);

    private static Uri Url(string server, string path) => new(new Uri(NormalizeServer(server)), path);
}
