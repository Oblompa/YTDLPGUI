using System.Text.Json;
using Xunit;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Process;
using YtDlpAudio.UI.ViewModels;

namespace YtDlpAudio.Core.Tests;

public class MainViewModelQueueTests
{
    private sealed class TestDependencyManager : IDependencyManager
    {
        public string BinDirectory => Path.GetTempPath();
        public string YtDlpPath => Path.Combine(BinDirectory, "yt-dlp.exe");
        public string FFmpegPath => Path.Combine(BinDirectory, "ffmpeg.exe");
        public string FFprobePath => Path.Combine(BinDirectory, "ffprobe.exe");
        public string DenoPath => Path.Combine(BinDirectory, "deno.exe");

        public Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new DependencyStatus(true, "test", true, "test", true, "deno 2.9.7"));

        public Task<bool> ProvisionAllAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> UpdateYtDlpAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> EnsureDenoAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>("test");
    }

    private sealed class TestAuthManager : IAuthManager
    {
        public Task<AuthStatus> GetCurrentAuthStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthStatus(false, false, null, DateTimeOffset.UtcNow));

        public Task<string> ExportCookiesToTempFileAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("A guest test should not export cookies.");

        public Task<bool> ValidatePremiumStatusAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task ClearSessionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public string GetWebView2UserDataFolder() => Path.GetTempPath();
    }

    private sealed class TestSearchService : ISearchService
    {
        public IReadOnlyList<SearchResultItem> SearchResults { get; set; } = Array.Empty<SearchResultItem>();

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            string query,
            SearchFilterType filter = SearchFilterType.Tracks,
            int maxResults = 20,
            CancellationToken ct = default) =>
            Task.FromResult(SearchResults);

        public Task<IReadOnlyList<SearchResultItem>> BrowseAccountPlaylistsAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SearchResultItem>>(Array.Empty<SearchResultItem>());

        public Task<IReadOnlyList<SearchResultItem>> BrowseYouTubeMusicPlaylistsAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SearchResultItem>>(Array.Empty<SearchResultItem>());
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public Task<UserSettings> LoadSettingsAsync(CancellationToken ct = default) =>
            Task.FromResult(new UserSettings());

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class BlockingRunner : IYtDlpRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<YtDlpResult> ExecuteAsync(
            string arguments,
            IProgress<DownloadProgressUpdate>? progress = null,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new YtDlpResult(false, 1, string.Empty, "Unexpected completion.");
        }

        public Task<JsonElement> QueryMetadataAsync(
            string url,
            string? cookiesFilePath = null,
            bool isFlatPlaylist = true,
            CancellationToken ct = default,
            int? playlistEnd = null) =>
            throw new NotSupportedException();
    }

    private static MainViewModel CreateViewModel(IYtDlpRunner runner)
    {
        var dependencies = new TestDependencyManager();
        var auth = new TestAuthManager();
        var search = new TestSearchService();
        return new MainViewModel(
            dependencies,
            auth,
            runner,
            search,
            new MetadataCleaner(),
            new AudioDownloadService(runner, dependencies),
            new TestSettingsService());
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static TrackItemViewModel CreateTrack(string title, bool selectedForDownload) => new()
    {
        Title = title,
        Url = "https://www.youtube.com/watch?v=test",
        IsSelected = selectedForDownload,
        State = DownloadState.Queued
    };

    [Fact]
    public void RemoveSelectedQueueTracks_RemovesRowsWithoutChangingDownloadCheckboxes()
    {
        var viewModel = CreateViewModel(new MockYtDlpRunner());
        var selectedRow = CreateTrack("Not selected for download", selectedForDownload: false);
        var remainingRow = CreateTrack("Selected for download", selectedForDownload: true);
        viewModel.QueueTracks.Add(selectedRow);
        viewModel.QueueTracks.Add(remainingRow);

        viewModel.SetSelectedQueueTracks([selectedRow]);
        viewModel.RemoveSelectedQueueTracks();

        Assert.DoesNotContain(selectedRow, viewModel.QueueTracks);
        Assert.Contains(remainingRow, viewModel.QueueTracks);
        Assert.True(remainingRow.IsSelected);
        Assert.False(selectedRow.IsSelected);
        Assert.Empty(viewModel.SelectedQueueTracks);
    }

    [Fact]
    public async Task ClearQueueAsync_RequiresConfirmationAndPreservesQueueWhenDeclined()
    {
        var viewModel = CreateViewModel(new MockYtDlpRunner());
        viewModel.QueueTracks.Add(CreateTrack("First", selectedForDownload: true));
        viewModel.QueueTracks.Add(CreateTrack("Second", selectedForDownload: false));
        int requestedCount = 0;
        bool confirm = false;
        viewModel.RequestClearQueueConfirmation += count =>
        {
            requestedCount = count;
            return Task.FromResult(confirm);
        };

        await viewModel.ClearQueueAsync();
        Assert.Equal(2, requestedCount);
        Assert.Equal(2, viewModel.QueueTracks.Count);

        confirm = true;
        await viewModel.ClearQueueAsync();
        Assert.Empty(viewModel.QueueTracks);
    }

    [Fact]
    public async Task RemoveSelectedQueueTracks_RequiresCancelBeforeChangingQueueDuringDownload()
    {
        var runner = new BlockingRunner();
        var viewModel = CreateViewModel(runner);
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"ytdlp_queue_test_{Guid.NewGuid():N}");
        viewModel.OutputDirectory = outputDirectory;
        var track = CreateTrack("Active track", selectedForDownload: true);
        viewModel.QueueTracks.Add(track);

        try
        {
            Task downloadTask = viewModel.DownloadSelectedAsync();
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.SetSelectedQueueTracks([track]);

            viewModel.RemoveSelectedQueueTracks();

            Assert.Contains(track, viewModel.QueueTracks);
            Assert.Contains("Cancel the current download batch", viewModel.StatusMessage);

            viewModel.CancelDownloads();
            await downloadTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DownloadState.Cancelled, track.State);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SearchPlaylistTracks_AreLoadedOnlyAfterExpandingThatPlaylist()
    {
        var requestedUrls = new List<string>();
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (url, _, _, _) =>
            {
                requestedUrls.Add(url);
                return Task.FromResult(ParseJson("""
                    {
                      "entries": [
                        { "id": "available", "title": "Available Song", "uploader": "Artist" },
                        { "id": "missing", "title": "Video unavailable", "is_unavailable": true }
                      ]
                    }
                    """));
            }
        };
        var viewModel = CreateViewModel(runner);
        var selectedPlaylist = new SearchResultItemViewModel(new SearchResultItem(
            "playlist-1", "First Playlist", "Owner", null, 2, null,
            "https://www.youtube.com/playlist?list=playlist-1", SearchResultType.Playlist));
        viewModel.SearchResults.Add(selectedPlaylist);
        viewModel.SearchResults.Add(new SearchResultItemViewModel(new SearchResultItem(
            "playlist-2", "Second Playlist", "Owner", null, 2, null,
            "https://www.youtube.com/playlist?list=playlist-2", SearchResultType.Playlist)));

        Assert.Empty(requestedUrls);
        Assert.Empty(selectedPlaylist.PlaylistTracks);

        await viewModel.ToggleSearchResultPlaylistExpansionAsync(selectedPlaylist);

        Assert.Equal(new[] { "https://www.youtube.com/playlist?list=playlist-1" }, requestedUrls);
        var track = Assert.Single(selectedPlaylist.PlaylistTracks);
        Assert.Equal("available", track.Id);
        Assert.Equal("Loaded 1 available track(s).", selectedPlaylist.PlaylistTracksStatus);
    }

    [Fact]
    public async Task ExpandPlaylistAsync_PreservesQueueRowWhenMetadataFetchFails()
    {
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) =>
                throw new InvalidOperationException("Playlist metadata unavailable.")
        };
        var viewModel = CreateViewModel(runner);
        var playlist = CreateTrack("Unavailable playlist", selectedForDownload: true);
        playlist.IsPlaylist = true;
        viewModel.QueueTracks.Add(playlist);

        await viewModel.ExpandPlaylistAsync(playlist);

        Assert.Same(playlist, Assert.Single(viewModel.QueueTracks));
        Assert.Contains("Failed to expand playlist", viewModel.StatusMessage);
    }

    [Fact]
    public async Task AddSearchResultToQueueAsync_SkipsUnavailablePlaylistEntries()
    {
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) => Task.FromResult(ParseJson("""
                {
                  "entries": [
                    { "id": "available", "title": "Available Song", "uploader": "Artist" },
                    { "id": "missing", "title": "Video unavailable", "is_unavailable": true },
                    { "title": "Missing ID" }
                  ]
                }
                """))
        };
        var viewModel = CreateViewModel(runner);
        var playlist = new SearchResultItemViewModel(new SearchResultItem(
            "playlist", "Playlist", "Owner", null, 3, null,
            "https://www.youtube.com/playlist?list=playlist", SearchResultType.Playlist));

        await viewModel.AddSearchResultToQueueAsync(playlist);

        var queuedTrack = Assert.Single(viewModel.QueueTracks);
        Assert.Equal("available", queuedTrack.Id);
        Assert.Equal("Available Song", queuedTrack.Title);
    }

    [Fact]
    public async Task SubmitInputAsync_QueuesSingleTrackMetadataForVideoUrl()
    {
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) => Task.FromResult(ParseJson("""
                { "id": "video-id", "title": "Single Song", "uploader": "Artist" }
                """))
        };
        var viewModel = CreateViewModel(runner);
        viewModel.InputText = "https://www.youtube.com/watch?v=video-id";

        await viewModel.SubmitInputAsync();

        var track = Assert.Single(viewModel.QueueTracks);
        Assert.Equal("video-id", track.Id);
        Assert.Equal("Single Song", track.Title);
    }
}
