using Xunit;
using YtDlpAudio.Infrastructure.Auth;

namespace YtDlpAudio.Core.Tests;

public class NetscapeCookieFormatterTests
{
    [Fact]
    public void Format_GivenValidCookies_ProducesValidNetscapeContent()
    {
        var cookies = new[]
        {
            new CookieRecord(
                Domain: "youtube.com",
                Name: "LOGIN_INFO",
                Value: "test_login_value",
                Path: "/",
                Expires: 1775000000,
                IsSecure: true
            ),
            new CookieRecord(
                Domain: ".google.com",
                Name: "SID",
                Value: "test_sid_value",
                Path: "/",
                Expires: 1775000000,
                IsSecure: true
            )
        };

        string formatted = NetscapeCookieFormatter.Format(cookies);

        Assert.StartsWith("# Netscape HTTP Cookie File", formatted);
        Assert.Contains(".youtube.com\tTRUE\t/\tTRUE\t1775000000\tLOGIN_INFO\ttest_login_value", formatted);
        Assert.Contains(".google.com\tTRUE\t/\tTRUE\t1775000000\tSID\ttest_sid_value", formatted);
    }
}
