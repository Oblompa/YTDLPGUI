using System.Windows;
using Microsoft.Web.WebView2.Core;
using YtDlpAudio.Infrastructure.Auth;

namespace YtDlpAudio.UI.Views;

public partial class LoginWindow : Window
{
    private readonly WebView2AuthManager _authManager;

    public LoginWindow(WebView2AuthManager? authManager = null)
    {
        InitializeComponent();
        _authManager = authManager ?? new WebView2AuthManager();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _authManager.GetWebView2UserDataFolder());
            await AuthWebView.EnsureCoreWebView2Async(env);
            AuthWebView.CoreWebView2.Navigate("https://accounts.google.com/ServiceLogin?service=youtube&uilel=3&passive=true&continue=https%3A%2F%2Fwww.youtube.com%2Fsignin%3Faction_handle_signin%3Dtrue");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnSaveSessionClicked(object sender, RoutedEventArgs e)
    {
        if (AuthWebView.CoreWebView2 == null)
        {
            DialogResult = false;
            Close();
            return;
        }

        try
        {
            var cookieManager = AuthWebView.CoreWebView2.CookieManager;
            var ytCookies = await cookieManager.GetCookiesAsync("https://www.youtube.com");
            var googleCookies = await cookieManager.GetCookiesAsync("https://accounts.google.com");

            var allCookies = ytCookies.Concat(googleCookies).Select(c => new CookieRecord(
                Domain: c.Domain,
                Name: c.Name,
                Value: c.Value,
                Path: c.Path,
                Expires: c.Expires,
                IsSecure: c.IsSecure,
                IsHttpOnly: c.IsHttpOnly
            ));

            string netscape = NetscapeCookieFormatter.Format(allCookies);
            await _authManager.SaveCookiesAsync(netscape);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export cookies: {ex.Message}", "Save Session Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
