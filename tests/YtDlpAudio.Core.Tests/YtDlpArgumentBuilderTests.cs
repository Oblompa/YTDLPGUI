using Xunit;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Process;

namespace YtDlpAudio.Core.Tests;

public class YtDlpArgumentBuilderTests
{
    [Fact]
    public void Build_GuestModeAudio_ProducesStandardBestAudioFlags()
    {
        var builder = new YtDlpArgumentBuilder()
            .WithFfmpegLocation(@"C:\AppData\bin")
            .WithJavaScriptRuntime(@"C:\AppData\bin\deno.exe")
            .WithAudioOnly("mp3", AudioQualityPreset.Cbr320k)
            .WithStreamSelection(preferPremium: false)
            .WithEmbeddings()
            .WithPrintAfterMoveFilepath();

        string args = builder.Build("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.Contains(@"--ffmpeg-location ""C:\AppData\bin""", args);
        Assert.Contains(@"--js-runtimes ""deno:C:\AppData\bin\deno.exe""", args);
        Assert.Contains("-x --audio-format mp3 --audio-quality 320k", args);
        Assert.Contains(@"-f ""ba""", args);
        Assert.DoesNotContain("format_id=141", args);
        Assert.DoesNotContain("--cookies", args);
        Assert.Contains("--embed-metadata", args);
        Assert.Contains("--embed-thumbnail", args);
        Assert.Contains("--convert-thumbnails jpg", args);
        Assert.Contains(@"--print ""after_move:filepath""", args);
    }

    [Fact]
    public void WithEmbeddings_OnlyAddsRequestedPostprocessors()
    {
        string metadataOnly = new YtDlpArgumentBuilder()
            .WithEmbeddings(metadata: true, thumbnail: false)
            .Build("url");
        string thumbnailOnly = new YtDlpArgumentBuilder()
            .WithEmbeddings(metadata: false, thumbnail: true)
            .Build("url");

        Assert.Contains("--embed-metadata", metadataOnly);
        Assert.DoesNotContain("--embed-thumbnail", metadataOnly);
        Assert.Contains("--embed-thumbnail", thumbnailOnly);
        Assert.DoesNotContain("--embed-metadata", thumbnailOnly);
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

    [Fact]
    public void WithJavaScriptRuntime_QuotesPathContainingSpaces()
    {
        string args = new YtDlpArgumentBuilder()
            .WithJavaScriptRuntime(@"C:\Portable App\bin\deno.exe")
            .Build("url");

        Assert.Contains(@"--js-runtimes ""deno:C:\Portable App\bin\deno.exe""", args);
    }

    [Fact]
    public void WithIgnoreErrors_AddsYtDlpIgnoreErrorsFlag()
    {
        string args = new YtDlpArgumentBuilder()
            .WithIgnoreErrors()
            .Build("playlist-url");

        Assert.Contains("--ignore-errors", args);
    }

    [Fact]
    public void DependencyStatus_RequiresDenoForReadyState()
    {
        var status = new DependencyStatus(true, "test", true, "test", false, null);

        Assert.False(status.AllReady);
    }
}
