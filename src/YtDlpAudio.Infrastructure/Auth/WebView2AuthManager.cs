using System.Net;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Auth;

public class WebView2AuthManager : IAuthManager
{
    private readonly DpapiCookieStorage _storage;
    private readonly string _authBaseDir;
    private AuthStatus? _cachedStatus;

    public WebView2AuthManager(string? authBaseDir = null)
    {
        _authBaseDir = authBaseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpAudio",
            "auth"
        );
        _storage = new DpapiCookieStorage(Path.Combine(_authBaseDir, "cookies.dat"));
    }

    public string GetWebView2UserDataFolder()
    {
        string dir = Path.Combine(_authBaseDir, "webview2_data");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<AuthStatus> GetCurrentAuthStatusAsync(CancellationToken ct = default)
    {
        if (_cachedStatus != null) return _cachedStatus;

        if (!_storage.HasStoredSession())
        {
            _cachedStatus = new AuthStatus(
                IsLoggedIn: false,
                HasPremium: false,
                AccountName: null,
                LastValidated: DateTimeOffset.UtcNow
            );
            return _cachedStatus;
        }

        string? cookies = await _storage.LoadCookiesAsync(ct);
        if (string.IsNullOrWhiteSpace(cookies) || !cookies.Contains("LOGIN_INFO"))
        {
            _cachedStatus = new AuthStatus(
                IsLoggedIn: false,
                HasPremium: false,
                AccountName: null,
                LastValidated: DateTimeOffset.UtcNow
            );
            return _cachedStatus;
        }

        bool isPremium = await ValidatePremiumStatusAsync(ct);
        _cachedStatus = new AuthStatus(
            IsLoggedIn: true,
            HasPremium: isPremium,
            AccountName: "YouTube Account",
            LastValidated: DateTimeOffset.UtcNow
        );

        return _cachedStatus;
    }

    public async Task<string> ExportCookiesToTempFileAsync(CancellationToken ct = default)
    {
        string? cookies = await _storage.LoadCookiesAsync(ct);
        if (string.IsNullOrWhiteSpace(cookies))
        {
            throw new InvalidOperationException("No authenticated session available to export.");
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"ytdlp_cookies_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, cookies, ct);
        return tempFile;
    }

    public async Task<bool> ValidatePremiumStatusAsync(CancellationToken ct = default)
    {
        try
        {
            string? cookiesContent = await _storage.LoadCookiesAsync(ct);
            if (string.IsNullOrWhiteSpace(cookiesContent)) return false;

            var cookieContainer = new CookieContainer();
            var lines = cookiesContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length >= 7)
                {
                    string domain = parts[0].TrimStart('.');
                    string name = parts[5];
                    string val = parts[6];
                    try
                    {
                        cookieContainer.Add(new Cookie(name, val, "/", domain));
                    }
                    catch { /* skip malformed single cookie */ }
                }
            }

            using var handler = new HttpClientHandler { CookieContainer = cookieContainer, AllowAutoRedirect = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");

            var response = await client.GetAsync("https://www.youtube.com/paid_memberships", ct);
            if (!response.IsSuccessStatusCode) return false;

            string html = await response.Content.ReadAsStringAsync(ct);
            return html.Contains("YouTube Premium", StringComparison.OrdinalIgnoreCase) ||
                   html.Contains("\"membership_type\":\"PREMIUM\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveCookiesAsync(string netscapeCookies, CancellationToken ct = default)
    {
        await _storage.SaveCookiesAsync(netscapeCookies, ct);
        _cachedStatus = null; // Invalidate cache
    }

    public Task ClearSessionAsync(CancellationToken ct = default)
    {
        _storage.Clear();
        _cachedStatus = new AuthStatus(false, false, null, DateTimeOffset.UtcNow);

        // Best effort clean webview2 session
        try
        {
            string webViewDir = GetWebView2UserDataFolder();
            if (Directory.Exists(webViewDir))
            {
                // Can be deleted or reset
            }
        }
        catch { /* best effort */ }

        return Task.CompletedTask;
    }
}
