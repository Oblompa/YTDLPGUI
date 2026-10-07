using Xunit;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Infrastructure.Process;

namespace YtDlpAudio.Core.Tests;

public class YtDlpArgumentBuilderTests
{
    [Fact]
    public void Build_GuestModeAudio_ProducesStandardBestAudioFlags()
    {
        var builder = new YtDlpArgumentBuilder()
            .WithFfmpegLocation(@"C:\AppData\bin")
            .WithAudioOnly("mp3", AudioQualityPreset.Cbr320k)
            .WithStreamSelection(preferPremium: false)
            .WithEmbeddings();

        string args = builder.Build("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.Contains(@"--ffmpeg-location ""C:\AppData\bin""", args);
        Assert.Contains("-x --audio-format mp3 --audio-quality 320k", args);
        Assert.Contains(@"-f ""ba""", args);
        Assert.DoesNotContain("format_id=141", args);
        Assert.DoesNotContain("--cookies", args);
        Assert.Contains("--embed-metadata", args);
        Assert.Contains("--embed-thumbnail", args);
        Assert.Contains("--convert-thumbnails jpg", args);
    }

    [Fact]
    public void Build_PremiumModeAudio_ProducesFormat141Flags()
    {
        var builder = new YtDlpArgumentBuilder()
            .WithStreamSelection(preferPremium: true);

        string args = builder.Build("https://music.youtube.com/watch?v=test");

        Assert.Contains(@"-f ""ba[format_id=141]/ba""", args);
    }

    [Fact]
    public void Build_QualityPresets_MapsCorrectQualityValues()
    {
        var builderV0 = new YtDlpArgumentBuilder().WithAudioOnly("mp3", AudioQualityPreset.VbrV0);
        Assert.Contains("--audio-quality 0", builderV0.Build("url"));

        var builder192 = new YtDlpArgumentBuilder().WithAudioOnly("mp3", AudioQualityPreset.Cbr192k);
        Assert.Contains("--audio-quality 192k", builder192.Build("url"));
    }
}
