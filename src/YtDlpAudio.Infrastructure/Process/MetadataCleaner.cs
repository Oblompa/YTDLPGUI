using System.Text.RegularExpressions;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public partial class MetadataCleaner : IMetadataCleaner
{
    private static readonly Regex[] TitleNoiseRegexes = new[]
    {
        new Regex(@"\s*[\(\[]\s*(?:official\s+)?(?:music\s+)?(?:lyric\s+)?(?:audio|video|visualizer|4k\s*(?:remaster|uhd)?|hd|clip\s+officiel|video\s+oficial|remaster(?:ed)?(?:\s+\d{4})?)\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*official\s*(?:music\s*)?video\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*official\s*audio\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*lyric\s*video\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*lyrics?\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*4k\s*(?:remaster|uhd)?\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*hd\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*audio\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*visualizer\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*remaster(?:ed)?(?:\s+\d{4})?\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*clip\s*officiel\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*video\s*oficial\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    private static readonly Regex[] ArtistNoiseRegexes = new[]
    {
        new Regex(@"\s*-\s*Topic$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"VEVO$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    public string CleanTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return string.Empty;

        string cleaned = rawTitle;
        foreach (var regex in TitleNoiseRegexes)
        {
            cleaned = regex.Replace(cleaned, "");
        }

        return cleaned.Trim();
    }

    public string CleanArtist(string rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist)) return string.Empty;

        string cleaned = rawArtist;
        foreach (var regex in ArtistNoiseRegexes)
        {
            cleaned = regex.Replace(cleaned, "");
        }

        return cleaned.Trim();
    }
}
