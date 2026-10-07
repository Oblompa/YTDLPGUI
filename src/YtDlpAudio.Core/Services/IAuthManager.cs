namespace YtDlpAudio.Core.Services;

public record AuthStatus(
    bool IsLoggedIn,
    bool HasPremium,
    string? AccountName,
    DateTimeOffset? LastValidated
)
{
    public bool IsGuest => !IsLoggedIn;
    public string DisplayMode => IsLoggedIn ? (HasPremium ? "YouTube Premium (High Quality)" : "Signed In (Standard)") : "Guest Mode (Standard Quality)";
}

public interface IAuthManager
{
    Task<AuthStatus> GetCurrentAuthStatusAsync(CancellationToken ct = default);
    Task<string> ExportCookiesToTempFileAsync(CancellationToken ct = default);
    Task<bool> ValidatePremiumStatusAsync(CancellationToken ct = default);
    Task ClearSessionAsync(CancellationToken ct = default);
    string GetWebView2UserDataFolder();
}
