namespace InvoicingTool.Core.Sync;

/// <summary>Stockage sécurisé des jetons de connexion (trousseau iOS, Keystore Android, coffre Windows).</summary>
public interface ITokenStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    void Remove(string key);
}
