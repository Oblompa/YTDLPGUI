using Xunit;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Infrastructure.Process;

namespace YtDlpAudio.Core.Tests;

public class DownloadProgressRegexTests
{
    [Theory]
    [InlineData("[download]  45.2% of ~120.50MiB at 12.4MiB/s ETA 00:05", 45.2, "120.50MiB", "12.4MiB/s", "00:05")]
    [InlineData("[download]   2.0% of 45.00MiB at 1.2MiB/s ETA 01:23", 2.0, "45.00MiB", "1.2MiB/s", "01:23")]
    [InlineData("[download]  99.9% of ~10.00MiB at 5.0MiB/s ETA 00:00", 99.9, "10.00MiB", "5.0MiB/s", "00:00")]
    public void TryParseProgressLine_StandardProgress_ExtractsMetrics(
        string line, double expectedPercent, string expectedSize, string expectedSpeed, string expectedEta)
    {
        bool success = RegexPatterns.TryParseProgressLine(line, "test_vid", out var update);

        Assert.True(success);
        Assert.NotNull(update);
        Assert.Equal(expectedPercent, update.Percent);
        Assert.Equal(expectedSize, update.TotalSize);
        Assert.Equal(expectedSpeed, update.DownloadSpeed);
        Assert.Equal(expectedEta, update.Eta);
        Assert.Equal(DownloadState.Downloading, update.State);
    }

    [Fact]
    public void TryParseProgressLine_DownloadCompleted_ReportsComplete()
    {
        string line = "[download] 100% of 15.42MiB";
        bool success = RegexPatterns.TryParseProgressLine(line, "test_vid", out var update);

        Assert.True(success);
        Assert.NotNull(update);
        Assert.Equal(100.0, update.Percent);
        Assert.Equal(DownloadState.Completed, update.State);
    }

    [Fact]
    public void TryParseProgressLine_ExtractAudio_ReportsExtractingAudio()
    {
        string line = "[ExtractAudio] Destination: C:\\Music\\song.mp3";
        bool success = RegexPatterns.TryParseProgressLine(line, "test_vid", out var update);

        Assert.True(success);
        Assert.NotNull(update);
        Assert.Equal(DownloadState.ExtractingAudio, update.State);
    }

    [Fact]
    public void TryParseProgressLine_EmbedMetadata_ReportsTagging()
    {
        string line = "[Metadata] Adding metadata to C:\\Music\\song.mp3";
        bool success = RegexPatterns.TryParseProgressLine(line, "test_vid", out var update);

        Assert.True(success);
        Assert.NotNull(update);
        Assert.Equal(DownloadState.Tagging, update.State);
    }
}
