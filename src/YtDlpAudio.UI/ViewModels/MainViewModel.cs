using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Process;

namespace YtDlpAudio.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDependencyManager _dependencyManager;
    private readonly IAuthManager _authManager;
    private readonly IYtDlpRunner _runner;
    private readonly ISearchService _searchService;
    private readonly IMetadataCleaner _cleaner;
    private readonly AudioDownloadService _downloadService;

    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private SearchFilterType _searchFilter = SearchFilterType.Tracks;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private AuthStatus _authStatus = new(false, false, null, null);
    [ObservableProperty] private DependencyStatus _dependencies = new(false, null, false, null);
    [ObservableProperty] private AudioQualityPreset _qualityPreset = AudioQualityPreset.Cbr320k;
    [ObservableProperty] private string _outputDirectory;
    [ObservableProperty] private bool _embedAlbumArt = true;
    [ObservableProperty] private bool _embedMetadata = true;
    [ObservableProperty] private bool _cleanTitle = true;
    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private double _overallProgress = 0;

    public ObservableCollection<SearchResultItemViewModel> SearchResults { get; } = new();
    public ObservableCollection<TrackItemViewModel> QueueTracks { get; } = new();

    public event Func<Task>? RequestLoginDialog;

    public MainViewModel(
        IDependencyManager dependencyManager,
        IAuthManager authManager,
        IYtDlpRunner runner,
        ISearchService searchService,
        IMetadataCleaner cleaner,
        AudioDownloadService downloadService)
    {
        _dependencyManager = dependencyManager;
        _authManager = authManager;
        _runner = runner;
        _searchService = searchService;
        _cleaner = cleaner;
        _downloadService = downloadService;

        _outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "YtDlpDownloads");
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking environment dependencies...";

        try
        {
            Dependencies = await _dependencyManager.CheckStatusAsync();
            AuthStatus = await _authManager.GetCurrentAuthStatusAsync();

            if (!Dependencies.AllReady)
            {
                StatusMessage = "yt-dlp or ffmpeg missing. Click 'Setup Dependencies' to install.";
            }
            else
            {
                StatusMessage = AuthStatus.IsLoggedIn 
                    ? $"Ready. Signed in as {AuthStatus.AccountName} ({AuthStatus.DisplayMode})"
                    : "Ready. Operating in Guest Mode.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Init warning: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SubmitInputAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        string query = InputText.Trim();
        IsBusy = true;

        try
        {
            if (IsUrl(query))
            {
                StatusMessage = "Inspecting URL metadata...";
                await FetchUrlMetadataAsync(query);
                SelectedTabIndex = 1; // Switch to Queue tab
            }
            else
            {
                StatusMessage = $"Searching for '{query}'...";
                await PerformSearchAsync(query);
                SelectedTabIndex = 0; // Switch to Search tab
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PerformSearchAsync(string query)
    {
        SearchResults.Clear();
        var results = await _searchService.SearchAsync(query, SearchFilter, maxResults: 25);
        foreach (var r in results)
        {
            SearchResults.Add(new SearchResultItemViewModel(r));
        }
        StatusMessage = $"Found {results.Count} results.";
    }

    private async Task FetchUrlMetadataAsync(string url)
    {
        string? cookiesPath = null;
        if (AuthStatus.IsLoggedIn)
        {
            cookiesPath = await _authManager.ExportCookiesToTempFileAsync();
        }

        try
        {
            var root = await _runner.QueryMetadataAsync(url, cookiesPath, isFlatPlaylist: true);
            ParseAndEnqueueMetadata(root, url);
            StatusMessage = $"Enqueued tracks from URL.";
        }
        finally
        {
            if (cookiesPath != null && File.Exists(cookiesPath))
            {
                try { File.Delete(cookiesPath); } catch { /* ignore */ }
            }
        }
    }

    private void ParseAndEnqueueMetadata(JsonElement root, string fallbackUrl)
    {
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                AddJsonEntryToQueue(entry, fallbackUrl);
            }
        }
        else
        {
            AddJsonEntryToQueue(root, fallbackUrl);
        }
    }

    private void AddJsonEntryToQueue(JsonElement entry, string fallbackUrl)
    {
        string id = entry.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
        string rawTitle = entry.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Unknown" : "Unknown";
        string rawArtist = entry.TryGetProperty("uploader", out var uploaderProp) ? uploaderProp.GetString() ?? "Unknown" : "Unknown";

        string title = CleanTitle ? _cleaner.CleanTitle(rawTitle) : rawTitle;
        string artist = CleanTitle ? _cleaner.CleanArtist(rawArtist) : rawArtist;

        TimeSpan? duration = null;
        if (entry.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out double durSec))
        {
            duration = TimeSpan.FromSeconds(durSec);
        }

        string? thumbnail = null;
        if (entry.TryGetProperty("thumbnail", out var thumbProp))
        {
            thumbnail = thumbProp.GetString();
        }

        string trackUrl = !string.IsNullOrEmpty(id) ? $"https://www.youtube.com/watch?v={id}" : fallbackUrl;

        QueueTracks.Add(new TrackItemViewModel
        {
            Id = id,
            Title = title,
            Artist = artist,
            Duration = duration,
            ThumbnailUrl = thumbnail,
            Url = trackUrl,
            IsSelected = true,
            State = DownloadState.Queued
        });
    }

    [RelayCommand]
    public async Task AddSearchResultToQueueAsync(SearchResultItemViewModel searchItem)
    {
        if (searchItem.ResultType == SearchResultType.Playlist)
        {
            IsBusy = true;
            StatusMessage = $"Fetching playlist tracks for '{searchItem.Title}'...";
            try
            {
                await FetchUrlMetadataAsync(searchItem.Url);
                SelectedTabIndex = 1;
            }
            finally
            {
                IsBusy = false;
            }
        }
        else
        {
            string title = CleanTitle ? _cleaner.CleanTitle(searchItem.Title) : searchItem.Title;
            string artist = CleanTitle ? _cleaner.CleanArtist(searchItem.Author) : searchItem.Author;

            QueueTracks.Add(new TrackItemViewModel
            {
                Id = searchItem.Id,
                Title = title,
                Artist = artist,
                Duration = searchItem.Model.Duration,
                ThumbnailUrl = searchItem.ThumbnailUrl,
                Url = searchItem.Url,
                IsSelected = true,
                State = DownloadState.Queued
            });

            StatusMessage = $"Added '{title}' to queue.";
        }
    }

    [RelayCommand]
    public async Task DownloadSelectedAsync()
    {
        var selectedTracks = QueueTracks.Where(t => t.IsSelected && t.State != DownloadState.Completed).ToList();
        if (!selectedTracks.Any())
        {
            StatusMessage = "No tracks selected for download.";
            return;
        }

        if (!Dependencies.AllReady)
        {
            StatusMessage = "Dependencies missing. Please click Setup Dependencies first.";
            return;
        }

        _downloadCts = new CancellationTokenSource();
        IsBusy = true;
        int completedCount = 0;
        int totalCount = selectedTracks.Count;

        var config = new AudioPipelineConfig(
            OutputDirectory: OutputDirectory,
            QualityPreset: QualityPreset,
            AudioFormat: "mp3",
            EmbedAlbumArt: EmbedAlbumArt,
            EmbedMetadata: EmbedMetadata,
            CleanNoiseFromTitle: CleanTitle
        );

        foreach (var track in selectedTracks)
        {
            if (_downloadCts.Token.IsCancellationRequested) break;

            track.State = DownloadState.Downloading;
            StatusMessage = $"Downloading ({completedCount + 1}/{totalCount}): {track.Title}";

            var progress = new Progress<DownloadProgressUpdate>(u =>
            {
                track.Progress = u.Percent;
                track.Speed = u.DownloadSpeed;
                track.Eta = u.Eta;
                track.State = u.State;
            });

            var result = await _downloadService.DownloadAudioAsync(track.Url, config, progress, _downloadCts.Token);

            if (result.Success)
            {
                track.State = DownloadState.Completed;
                track.Progress = 100;
                completedCount++;
            }
            else
            {
                track.State = DownloadState.Failed;
                track.ErrorMessage = result.Error;
            }

            OverallProgress = (double)completedCount / totalCount * 100.0;
        }

        IsBusy = false;
        StatusMessage = _downloadCts.Token.IsCancellationRequested 
            ? "Downloads cancelled." 
            : $"Completed {completedCount} of {totalCount} audio files.";
    }

    [RelayCommand]
    public void CancelDownloads()
    {
        _downloadCts?.Cancel();
        StatusMessage = "Cancelling downloads...";
    }

    [RelayCommand]
    public async Task SetupDependenciesAsync()
    {
        IsBusy = true;
        StatusMessage = "Provisioning yt-dlp and FFmpeg...";

        var progress = new Progress<ProvisioningProgress>(p =>
        {
            StatusMessage = $"{p.CurrentStep} ({p.PercentComplete:F0}%)";
        });

        bool success = await _dependencyManager.ProvisionAllAsync(progress);
        Dependencies = await _dependencyManager.CheckStatusAsync();
        IsBusy = false;

        StatusMessage = success ? "Dependencies installed and ready!" : "Failed to provision dependencies.";
    }

    [RelayCommand]
    public async Task SignInAsync()
    {
        if (RequestLoginDialog != null)
        {
            await RequestLoginDialog.Invoke();
            AuthStatus = await _authManager.GetCurrentAuthStatusAsync();
            StatusMessage = AuthStatus.IsLoggedIn 
                ? $"Logged in as {AuthStatus.AccountName} ({AuthStatus.DisplayMode})" 
                : "Still in Guest Mode.";
        }
    }

    [RelayCommand]
    public async Task SignOutAsync()
    {
        await _authManager.ClearSessionAsync();
        AuthStatus = await _authManager.GetCurrentAuthStatusAsync();
        StatusMessage = "Signed out. Switched to Guest Mode.";
    }

    private static bool IsUrl(string text)
    {
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }
}
