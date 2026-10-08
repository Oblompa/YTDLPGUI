using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace YtDlpAudio.Infrastructure.Auth;

[SupportedOSPlatform("windows")]
public class DpapiCookieStorage
{
    private readonly string _storageFilePath;

    public DpapiCookieStorage(string? customPath = null)
    {
        _storageFilePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpAudio",
            "auth",
            "cookies.dat"
        );
    }

    public bool HasStoredSession() => File.Exists(_storageFilePath);

    [SupportedOSPlatform("windows")]
    public async Task SaveCookiesAsync(string netscapeCookieContent, CancellationToken ct = default)
    {
        string directory = Path.GetDirectoryName(_storageFilePath)!;
        Directory.CreateDirectory(directory);

        byte[] rawBytes = Encoding.UTF8.GetBytes(netscapeCookieContent);
        byte[] encryptedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(_storageFilePath, encryptedBytes, ct);
    }

    [SupportedOSPlatform("windows")]
    public async Task<string?> LoadCookiesAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_storageFilePath)) return null;

        try
        {
            byte[] encryptedBytes = await File.ReadAllBytesAsync(_storageFilePath, ct);
            byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            // If decryption fails (e.g. machine change or corrupted file), return null
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_storageFilePath))
        {
            try { File.Delete(_storageFilePath); } catch { /* best effort */ }
        }
    }
}
