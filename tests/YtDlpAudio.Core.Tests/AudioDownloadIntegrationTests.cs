using System.Text.Json;
using Xunit;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Process;
using YtDlpAudio.Infrastructure.Services;

namespace YtDlpAudio.Core.Tests;

public class AudioDownloadIntegrationTests
{
    private class FakeDependencyManager : IDependencyManager
    {
        public string BinDirectory => @"C:\Fake\bin";
        public string YtDlpPath => @"C:\Fake\bin\yt-dlp.exe";
        public string FFmpegPath => @"C:\Fake\bin\ffmpeg.exe";
        public string FFprobePath => @"C:\Fake\bin\ffprobe.exe";

        public Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new DependencyStatus(true, "2026.01.01", true, "7.0"));

        public Task<bool> ProvisionAllAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> UpdateYtDlpAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>("2026.01.01");
    }

    [Fact]
    public async Task SearchAndDownloadFlow_ExecutesSuccessfullyWithProgress()
    {
        var mockRunner = new MockYtDlpRunner();

        mockRunner.QueryMetadataHandler = (url, cookies, flat, ct) =>
        {
            string json = """
            {
                "entries": [
                    {
                        "id": "vid123",
                        "title": "Bohemian Rhapsody (Official Video)",
                        "uploader": "Queen",
                        "duration": 355.0,
                        "url": "https://www.youtube.com/watch?v=vid123"
                    }
                ]
            }
            """;
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(doc.RootElement.Clone());
        };

        var searchService = new SearchService(mockRunner, authManager: null);
        var results = await searchService.SearchAsync("Bohemian Rhapsody");

        Assert.Single(results);
        var first = results[0];
        Assert.Equal("vid123", first.Id);
        Assert.Equal("Bohemian Rhapsody (Official Video)", first.Title);

        var cleaner = new MetadataCleaner();
        string cleanTitle = cleaner.CleanTitle(first.Title);
        Assert.Equal("Bohemian Rhapsody", cleanTitle);

        var progressUpdates = new List<DownloadProgressUpdate>();
        var progress = new Progress<DownloadProgressUpdate>(u => progressUpdates.Add(u));

        var depManager = new FakeDependencyManager();
        var downloadService = new AudioDownloadService(mockRunner, depManager, authManager: null);

        string testOutput = Path.Combine(Path.GetTempPath(), $"ytdlp_test_{Guid.NewGuid():N}");
        try
        {
            var config = new AudioPipelineConfig(
                OutputDirectory: testOutput,
                QualityPreset: AudioQualityPreset.Cbr320k
            );

            var downloadResult = await downloadService.DownloadAudioAsync(first.Url, config, progress);

            Assert.True(downloadResult.Success);
            Assert.NotEmpty(progressUpdates);
            Assert.Contains(progressUpdates, u => u.State == DownloadState.Completed);
        }
        finally
        {
            if (Directory.Exists(testOutput))
            {
                try { Directory.Delete(testOutput, recursive: true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task JsonSettingsService_PersistsAndLoadsAcrossSessions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}");
        try
        {
            var appPaths = new AppPathsService(forcePortable: true, customAppDir: tempDir);
            appPaths.EnsureDirectories();

            var settingsService = new JsonSettingsService(appPaths);
            var initialSettings = await settingsService.LoadSettingsAsync();

            Assert.Equal(AudioQualityPreset.Cbr320k, initialSettings.QualityPreset);

            var updated = initialSettings with
            {
                QualityPreset = AudioQualityPreset.VbrV0,
                CleanTitle = false,
                OutputDirectory = @"C:\Custom\Music"
            };

            await settingsService.SaveSettingsAsync(updated);

            var reloaded = await settingsService.LoadSettingsAsync();
            Assert.Equal(AudioQualityPreset.VbrV0, reloaded.QualityPreset);
            Assert.False(reloaded.CleanTitle);
            Assert.Equal(@"C:\Custom\Music", reloaded.OutputDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }
}
