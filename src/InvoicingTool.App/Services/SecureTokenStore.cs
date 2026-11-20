using InvoicingTool.Core.Sync;
#if WINDOWS
using System.Security.Cryptography;
using System.Text;
#endif

namespace InvoicingTool.App.Services;

/// <summary>
/// Jetons de connexion à la synchronisation : trousseau (iOS) ou Keystore (Android) via SecureStorage.
/// Sous Windows (application non empaquetée, où SecureStorage n'est pas disponible), fichier chiffré avec
/// DPAPI, lisible uniquement par l'utilisateur Windows connecté.
/// </summary>
public sealed class SecureTokenStore : ITokenStore
{
#if WINDOWS
	private static string PathFor(string key) => Path.Combine(FileSystem.AppDataDirectory, "tokens", key + ".bin");

	public async Task<string?> GetAsync(string key)
	{
		var path = PathFor(key);
		if (!File.Exists(path))
			return null;
		try
		{
			var data = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(data);
		}
		catch (CryptographicException)
		{
			return null;
		}
	}

	public async Task SetAsync(string key, string value)
	{
		var path = PathFor(key);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllBytesAsync(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
	}

	public void Remove(string key)
	{
		var path = PathFor(key);
		if (File.Exists(path))
			File.Delete(path);
	}
#else
	public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

	public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

	public void Remove(string key) => SecureStorage.Default.Remove(key);
#endif
}
