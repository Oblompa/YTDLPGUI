using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
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
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private SearchFilterType _searchFilter = SearchFilterType.Tracks;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private AuthStatus _authStatus = new(false, false, null, null);
    [ObservableProperty] private DependencyStatus _dependencies = new(false, null, false, null, false, null);
    [ObservableProperty] private AudioQualityPreset _qualityPreset = AudioQualityPreset.Cbr320k;
    [ObservableProperty] private string _outputDirectory = string.Empty;
    [ObservableProperty] private bool _embedAlbumArt = true;
    [ObservableProperty] private bool _embedMetadata = true;
    [ObservableProperty] private bool _cleanTitle = true;
    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private double _overallProgress = 0;
    [ObservableProperty] private bool _isDownloadingQueue;
    [ObservableProperty] private bool _isLoadingAccountPlaylists;
    [ObservableProperty] private string _accountLibraryStatus = "Sign in to browse playlists in your YouTube account.";
    [ObservableProperty] private bool _isLoadingYouTubeMusicPlaylists;
    [ObservableProperty] private string _youTubeMusicLibraryStatus = "Sign in to browse playlists in your YouTube Music account.";

    public ObservableCollection<SearchResultItemViewModel> SearchResults { get; } = new();
    public ObservableCollection<SearchResultItemViewModel> AccountPlaylists { get; } = new();
    public ObservableCollection<SearchResultItemViewModel> YouTubeMusicPlaylists { get; } = new();
    public ObservableCollection<TrackItemViewModel> QueueTracks { get; } = new();
    public ObservableCollection<TrackItemViewModel> SelectedQueueTracks { get; } = new();

    public event Func<Task>? RequestLoginDialog;
    public event Func<int, Task<bool>>? RequestClearQueueConfirmation;

    public MainViewModel(
        IDependencyManager dependencyManager,
        IAuthManager authManager,
        IYtDlpRunner runner,
        ISearchService searchService,
        IMetadataCleaner cleaner,
        AudioDownloadService downloadService,
        ISettingsService settingsService,
        ILogger<MainViewModel>? logger = null)
    {
        _dependencyManager = dependencyManager;
        _authManager = authManager;
        _runner = runner;
        _searchService = searchService;
        _cleaner = cleaner;
        _downloadService = downloadService;
        _settingsService = settingsService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MainViewModel>.Instance;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking environment dependencies and loading settings...";

        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            OutputDirectory = settings.OutputDirectory;
            QualityPreset = settings.QualityPreset;
            EmbedAlbumArt = settings.EmbedAlbumArt;
            EmbedMetadata = settings.EmbedMetadata;
            CleanTitle = settings.CleanTitle;
            SearchFilter = settings.SearchFilter;

            Dependencies = await _dependencyManager.CheckStatusAsync();
            AuthStatus = await _authManager.GetCurrentAuthStatusAsync();
            UpdateAccountLibraryStatus();

            if (!Dependencies.AllReady)
            {
                StatusMessage = !Dependencies.YtDlpInstalled
                    ? "yt-dlp and Deno will be installed automatically when you search. FFmpeg is needed to download MP3s."
                    : !Dependencies.DenoInstalled
                        ? "Deno is needed to access YouTube media and will be installed when you search."
                        : "FFmpeg is needed to download MP3s and will be installed when you start a download.";
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
                SelectedTabIndex = 2; // Switch to Queue tab
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
        await EnsureYtDlpReadyAsync();
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
        var root = await QueryMetadataWithCurrentAuthAsync(url);
        int addedCount = ParseAndEnqueueMetadata(root);
        StatusMessage = addedCount == 0
            ? "No available tracks were found in this playlist."
            : $"Added {addedCount} available track(s) to the queue.";
    }

    private async Task<JsonElement> QueryMetadataWithCurrentAuthAsync(
        string url,
        CancellationToken ct = default)
    {
        await EnsureYtDlpReadyAsync();
        var authStatus = await _authManager.GetCurrentAuthStatusAsync(ct);
        string? cookiesPath = authStatus.IsLoggedIn
            ? await _authManager.ExportCookiesToTempFileAsync(ct)
            : null;

        try
        {
            return await _runner.QueryMetadataAsync(
                url,
                cookiesPath,
                isFlatPlaylist: true,
                ct: ct);
        }
        finally
        {
            if (cookiesPath is not null && File.Exists(cookiesPath))
            {
                File.Delete(cookiesPath);
            }
        }
    }

    private int ParseAndEnqueueMetadata(JsonElement root)
    {
        var tracks = ParseAvailablePlaylistTracks(root, includeSingleTrack: true);
        bool hasPlaylistEntries = root.ValueKind == JsonValueKind.Object &&
                                  root.TryGetProperty("entries", out var entries) &&
                                  entries.ValueKind == JsonValueKind.Array;
        string? playlistTitle = hasPlaylistEntries ? GetStringProperty(root, "title") : null;
        foreach (var track in tracks)
        {
            QueueTracks.Add(CreateQueueTrack(track, playlistTitle));
        }

        return tracks.Count;
    }

    private IReadOnlyList<SearchResultItem> ParseAvailablePlaylistTracks(
        JsonElement root,
        bool includeSingleTrack = false)
    {
        var tracks = new List<SearchResultItem>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("entries", out var entries) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var track = ParseAvailableTrack(entry);
                if (track is not null)
                {
                    tracks.Add(track);
                }
            }
        }
        else if (includeSingleTrack)
        {
            var track = ParseAvailableTrack(root);
            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return tracks;
    }

    private static SearchResultItem? ParseAvailableTrack(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object ||
            (entry.TryGetProperty("is_unavailable", out var unavailable) &&
             unavailable.ValueKind == JsonValueKind.True))
        {
            return null;
        }

        string? id = GetStringProperty(entry, "id");
        string? title = GetStringProperty(entry, "title");
        string? availability = GetStringProperty(entry, "availability");
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Equals("Video unavailable", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("[Private video]", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("[Deleted video]", StringComparison.OrdinalIgnoreCase) ||
            availability?.Equals("unavailable", StringComparison.OrdinalIgnoreCase) == true)
        {
            return null;
        }

        string author = GetStringProperty(entry, "uploader") ??
                        GetStringProperty(entry, "channel") ??
                        "Unknown Artist";
        string url = GetStringProperty(entry, "webpage_url") ??
                     GetStringProperty(entry, "url") ??
                     string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var trackUri) ||
            !(trackUri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
              trackUri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)) ||
            !trackUri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}";
        }

        TimeSpan? duration = entry.TryGetProperty("duration", out var durationElement) &&
                             durationElement.ValueKind == JsonValueKind.Number &&
                             durationElement.TryGetDouble(out double seconds) &&
                             seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
        string? thumbnail = GetStringProperty(entry, "thumbnail");

        return new SearchResultItem(
            Id: id,
            Title: title,
            Author: author,
            Duration: duration,
            TrackCount: null,
            ThumbnailUrl: thumbnail,
            Url: url,
            ResultType: SearchResultType.Track);
    }

    private static string? GetStringProperty(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private TrackItemViewModel CreateQueueTrack(SearchResultItem track, string? outputFolderName = null)
    {
        string artist = CleanTitle ? _cleaner.CleanArtist(track.Author) : track.Author;
        return new TrackItemViewModel
        {
            Id = track.Id,
            Title = CleanTitle ? _cleaner.CleanTitle(track.Title) : track.Title,
            Artist = artist,
            OutputFolderName = string.IsNullOrWhiteSpace(outputFolderName) ? artist : outputFolderName,
            Duration = track.Duration,
            ThumbnailUrl = track.ThumbnailUrl,
            Url = track.Url,
            IsSelected = true,
            State = DownloadState.Queued,
            IsPlaylist = false
        };
    }

    [RelayCommand]
    public async Task ToggleSearchResultPlaylistExpansionAsync(SearchResultItemViewModel playlist)
    {
        if (!playlist.IsPlaylist)
        {
            return;
        }

        if (playlist.IsPlaylistExpanded)
        {
            playlist.IsPlaylistExpanded = false;
            return;
        }

        if (IsBusy || IsDownloadingQueue)
        {
            const string busyMessage = "Finish the current operation before expanding a playlist.";
            playlist.PlaylistTracksStatus = busyMessage;
            StatusMessage = busyMessage;
            return;
        }

        playlist.IsPlaylistExpanded = true;
        if (playlist.HasLoadedPlaylistTracks)
        {
            return;
        }

        IsBusy = true;
        playlist.IsLoadingPlaylistTracks = true;
        playlist.PlaylistTracksStatus = $"Loading tracks for '{playlist.Title}'...";
        try
        {
            var root = await QueryMetadataWithCurrentAuthAsync(playlist.Url);
            var tracks = ParseAvailablePlaylistTracks(root);
            foreach (var track in tracks)
            {
                playlist.PlaylistTracks.Add(new SearchResultItemViewModel(track));
            }

            playlist.HasLoadedPlaylistTracks = true;
            playlist.PlaylistTracksStatus = tracks.Count == 0
                ? "No available tracks were found in this playlist."
                : $"Loaded {tracks.Count} available track(s).";
        }
        catch (Exception ex)
        {
            playlist.PlaylistTracksStatus = $"Could not load playlist tracks: {ex.Message}";
            StatusMessage = playlist.PlaylistTracksStatus;
        }
        finally
        {
            playlist.IsLoadingPlaylistTracks = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExpandPlaylistAsync(TrackItemViewModel trackItem)
    {
        if (!trackItem.IsPlaylist || string.IsNullOrWhiteSpace(trackItem.Url) || IsBusy || IsDownloadingQueue)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Expanding playlist '{trackItem.Title}'...";

        try
        {
            var root = await QueryMetadataWithCurrentAuthAsync(trackItem.Url);
            var tracks = ParseAvailablePlaylistTracks(root);
            if (tracks.Count == 0)
            {
                throw new InvalidOperationException("No available tracks were found in this playlist.");
            }

            int originalIndex = QueueTracks.IndexOf(trackItem);
            if (originalIndex < 0)
            {
                throw new InvalidOperationException("The playlist is no longer in the queue.");
            }

            QueueTracks.RemoveAt(originalIndex);
            foreach (var track in tracks)
            {
                QueueTracks.Insert(originalIndex++, CreateQueueTrack(track, trackItem.Title));
            }
            StatusMessage = $"Expanded playlist into {tracks.Count} available track(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to expand playlist: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddSearchResultToQueueAsync(SearchResultItemViewModel searchItem)
    {
        if (IsBusy || IsDownloadingQueue)
        {
            StatusMessage = "Finish the current operation before adding search results.";
            return;
        }

        if (searchItem.ResultType == SearchResultType.Playlist)
        {
            IsBusy = true;
            StatusMessage = $"Fetching playlist tracks for '{searchItem.Title}'...";
            try
            {
                await FetchUrlMetadataAsync(searchItem.Url);
                SelectedTabIndex = 2;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to add playlist tracks: {ex.Message}";
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
                OutputFolderName = artist,
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
    public Task BrowseAccountPlaylistsAsync() =>
        BrowsePlaylistsAsync(
            "YouTube",
            AccountPlaylists,
            _searchService.BrowseAccountPlaylistsAsync,
            status => AccountLibraryStatus = status,
            isLoading => IsLoadingAccountPlaylists = isLoading);

    [RelayCommand]
    public Task BrowseYouTubeMusicPlaylistsAsync() =>
        BrowsePlaylistsAsync(
            "YouTube Music",
            YouTubeMusicPlaylists,
            _searchService.BrowseYouTubeMusicPlaylistsAsync,
            status => YouTubeMusicLibraryStatus = status,
            isLoading => IsLoadingYouTubeMusicPlaylists = isLoading);

    private async Task BrowsePlaylistsAsync(
        string serviceName,
        ObservableCollection<SearchResultItemViewModel> target,
        Func<CancellationToken, Task<IReadOnlyList<SearchResultItem>>> browseAsync,
        Action<string> setLibraryStatus,
        Action<bool> setLoading)
    {
        if (IsBusy || IsDownloadingQueue)
        {
            setLibraryStatus("Finish the current operation before refreshing playlists.");
            return;
        }

        IsBusy = true;
        setLoading(true);
        target.Clear();

        try
        {
            AuthStatus = await _authManager.GetCurrentAuthStatusAsync();
            if (!AuthStatus.IsLoggedIn)
            {
                setLibraryStatus($"Sign in to browse playlists in your {serviceName} account.");
                StatusMessage = $"Sign in to browse playlists in your {serviceName} account.";
                return;
            }

            setLibraryStatus($"Loading playlists from your {serviceName} account...");
            await EnsureYtDlpReadyAsync();
            var playlists = await browseAsync(CancellationToken.None);
            foreach (var playlist in playlists)
            {
                target.Add(new SearchResultItemViewModel(playlist));
            }

            string status = playlists.Count == 0
                ? $"No accessible playlists were found in your {serviceName} account."
                : $"Loaded {playlists.Count} {serviceName} playlists. Choose one to add its tracks to the queue.";
            setLibraryStatus(status);
            StatusMessage = status;
        }
        catch (Exception ex)
        {
            target.Clear();
            string status =
                $"Could not load your {serviceName} playlists. Your sign-in may have expired; sign in again and retry. {ex.Message}";
            setLibraryStatus(status);
            StatusMessage = status;
        }
        finally
        {
            setLoading(false);
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddAccountPlaylistToQueueAsync(SearchResultItemViewModel playlist)
    {
        if (playlist.ResultType != SearchResultType.Playlist || IsBusy || IsDownloadingQueue)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Loading playlist '{playlist.Title}' into the queue...";
            await FetchUrlMetadataAsync(playlist.Url);
            SelectedTabIndex = 2;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not add playlist to the queue: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetSelectedQueueTracks(IEnumerable<TrackItemViewModel> selectedTracks)
    {
        SelectedQueueTracks.Clear();
        foreach (var track in selectedTracks)
        {
            if (QueueTracks.Contains(track) && !SelectedQueueTracks.Contains(track))
            {
                SelectedQueueTracks.Add(track);
            }
        }
    }

    [RelayCommand]
    public void RemoveSelectedQueueTracks()
    {
        if (IsDownloadingQueue)
        {
            StatusMessage = "Cancel the current download batch and wait for it to stop before removing queue rows.";
            return;
        }

        var selected = SelectedQueueTracks.ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "Select one or more queue rows to remove.";
            return;
        }

        int removedCount = 0;
        int protectedCount = 0;
        foreach (var track in selected)
        {
            if (IsActiveDownloadState(track.State))
            {
                protectedCount++;
                continue;
            }

            if (QueueTracks.Remove(track))
            {
                removedCount++;
            }
        }

        SelectedQueueTracks.Clear();
        StatusMessage = protectedCount > 0
            ? $"Removed {removedCount} queue rows; kept {protectedCount} active download(s)."
            : $"Removed {removedCount} queue row(s).";
    }

    [RelayCommand]
    public async Task ClearQueueAsync()
    {
        if (IsDownloadingQueue)
        {
            StatusMessage = "Cancel the current download batch and wait for it to stop before clearing the queue.";
            return;
        }

        if (QueueTracks.Count == 0)
        {
            StatusMessage = "The queue is already empty.";
            return;
        }

        if (RequestClearQueueConfirmation is null ||
            !await RequestClearQueueConfirmation.Invoke(QueueTracks.Count))
        {
            StatusMessage = "Queue clear cancelled.";
            return;
        }

        int removedCount = 0;
        int protectedCount = 0;
        foreach (var track in QueueTracks.ToArray())
        {
            if (IsActiveDownloadState(track.State))
            {
                protectedCount++;
                continue;
            }

            if (QueueTracks.Remove(track))
            {
                removedCount++;
            }
        }

        SelectedQueueTracks.Clear();
        StatusMessage = protectedCount > 0
            ? $"Cleared {removedCount} queue row(s); kept {protectedCount} active download(s)."
            : "Queue cleared.";
    }

    [RelayCommand]
    public void ClearCompletedQueueTracks()
    {
        var completedTracks = QueueTracks
            .Where(track => track.State == DownloadState.Completed)
            .ToArray();
        if (completedTracks.Length == 0)
        {
            StatusMessage = "There are no completed downloads to clear.";
            return;
        }

        foreach (var track in completedTracks)
        {
            QueueTracks.Remove(track);
            SelectedQueueTracks.Remove(track);
        }

        StatusMessage = $"Cleared {completedTracks.Length} completed download(s).";
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

        var queueStopwatch = Stopwatch.StartNew();
        var dependencyStopwatch = Stopwatch.StartNew();
        IsBusy = true;
        try
        {
            await EnsureAudioDependenciesReadyAsync();
            dependencyStopwatch.Stop();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not prepare download dependencies: {ex.Message}";
            IsBusy = false;
            return;
        }

        _downloadCts = new CancellationTokenSource();
        var downloadCts = _downloadCts;
        IsDownloadingQueue = true;
        int completedCount = 0;
        int totalCount = selectedTracks.Count;
        bool downloadCancelled = false;
        var failures = new List<(string Title, string Message)>();
        var downloadTimings = new List<DownloadTiming>();
        var queueProcessingStopwatch = Stopwatch.StartNew();

        var config = new AudioPipelineConfig(
            OutputDirectory: OutputDirectory,
            QualityPreset: QualityPreset,
            AudioFormat: "mp3",
            EmbedAlbumArt: EmbedAlbumArt,
            EmbedMetadata: EmbedMetadata,
            CleanNoiseFromTitle: CleanTitle
        );

        try
        {
            foreach (var track in selectedTracks)
            {
                if (downloadCts.Token.IsCancellationRequested)
                {
                    track.State = DownloadState.Cancelled;
                    downloadCancelled = true;
                    break;
                }

                track.ErrorMessage = null;
                track.State = DownloadState.Downloading;
                StatusMessage = $"Downloading ({completedCount + failures.Count + 1}/{totalCount}): {track.Title}";

                var progress = new Progress<DownloadProgressUpdate>(u =>
                {
                    track.Progress = u.Percent;
                    track.Speed = u.DownloadSpeed;
                    track.Eta = u.Eta;
                    track.State = u.State;
                });

                try
                {
                    var result = await _downloadService.DownloadAudioAsync(
                        track.Url,
                        config,
                        progress,
                        downloadCts.Token,
                        track.OutputFolderName);
                    if (result.Timing is not null)
                    {
                        downloadTimings.Add(result.Timing);
                    }

                    if (result.Success)
                    {
                        track.State = DownloadState.Completed;
                        track.Progress = 100;
                        completedCount++;
                    }
                    else if (downloadCts.IsCancellationRequested || result.ExitCode == -2)
                    {
                        track.State = DownloadState.Cancelled;
                        downloadCancelled = true;
                        break;
                    }
                    else
                    {
                        track.State = DownloadState.Failed;
                        track.ErrorMessage = string.IsNullOrWhiteSpace(result.Error)
                            ? $"yt-dlp failed with exit code {result.ExitCode} and returned no diagnostic."
                            : result.Error;
                        failures.Add((track.Title, track.ErrorMessage));
                    }
                }
                catch (OperationCanceledException) when (downloadCts.IsCancellationRequested)
                {
                    track.State = DownloadState.Cancelled;
                    downloadCancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    track.State = DownloadState.Failed;
                    track.ErrorMessage = ex.Message;
                    failures.Add((track.Title, ex.Message));
                }

                OverallProgress = (double)(completedCount + failures.Count) / totalCount * 100.0;
            }

            if (downloadCancelled || downloadCts.IsCancellationRequested)
            {
                StatusMessage = $"Downloads cancelled. Completed {completedCount} of {totalCount} audio files.";
            }
            else
            {
                StatusMessage = $"Completed {completedCount} of {totalCount} audio files.";
                if (failures.Count > 0)
                {
                    var firstFailure = failures[0];
                    string conciseError = firstFailure.Message.Replace(Environment.NewLine, " ").Trim();
                    if (conciseError.Length > 180)
                    {
                        conciseError = conciseError[..180] + "...";
                    }
                    StatusMessage += $" {failures.Count} failed. First failure ({firstFailure.Title}): {conciseError}";
                }
            }

            queueProcessingStopwatch.Stop();
            queueStopwatch.Stop();
            AppendDownloadTimingSummary(
                queueStopwatch.Elapsed,
                dependencyStopwatch.Elapsed,
                queueProcessingStopwatch.Elapsed,
                downloadTimings);
        }
        finally
        {
            downloadCts.Dispose();
            _downloadCts = null;
            IsDownloadingQueue = false;
            IsBusy = false;
        }
    }

    private void AppendDownloadTimingSummary(
        TimeSpan total,
        TimeSpan dependencySetup,
        TimeSpan queueProcessing,
        IReadOnlyCollection<DownloadTiming> timings)
    {
        TimeSpan process = TimeSpan.FromTicks(timings.Sum(timing => timing.Process.Ticks));
        TimeSpan transfer = TimeSpan.FromTicks(timings.Sum(timing => timing.Transfer?.Ticks ?? 0));
        TimeSpan postProcessing = TimeSpan.FromTicks(timings.Sum(timing => timing.PostProcessing?.Ticks ?? 0));
        string summary =
            $"Time: {FormatElapsed(total)} total; {FormatElapsed(dependencySetup)} setup; " +
            $"{FormatElapsed(queueProcessing)} queue; yt-dlp {FormatElapsed(process)} " +
            $"(transfer {FormatElapsed(transfer)}, post-processing {FormatElapsed(postProcessing)}).";

        _logger.LogInformation(
            "Download queue timing: items={ItemCount}, total={TotalMilliseconds} ms, dependency-setup={DependencySetupMilliseconds} ms, queue-processing={QueueProcessingMilliseconds} ms, yt-dlp-process-sum={ProcessMilliseconds} ms, transfer-sum={TransferMilliseconds} ms, post-processing-sum={PostProcessingMilliseconds} ms.",
            timings.Count,
            total.TotalMilliseconds,
            dependencySetup.TotalMilliseconds,
            queueProcessing.TotalMilliseconds,
            process.TotalMilliseconds,
            transfer.TotalMilliseconds,
            postProcessing.TotalMilliseconds);
        StatusMessage += $" {summary}";
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");

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
        try
        {
            await EnsureAudioDependenciesReadyAsync();
            StatusMessage = "Dependencies installed and ready!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to provision dependencies: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnsureYtDlpReadyAsync()
    {
        if (await _dependencyManager.GetYtDlpVersionAsync() is null)
        {
            StatusMessage = "yt-dlp is missing. Downloading it before continuing...";
            var progress = new Progress<ProvisioningProgress>(p =>
            {
                StatusMessage = $"{p.CurrentStep} ({p.PercentComplete:F0}%)";
            });

            bool installed = await _dependencyManager.UpdateYtDlpAsync(progress);
            if (!installed)
            {
                throw new InvalidOperationException(
                    $"yt-dlp and Deno could not be prepared. Check your internet connection and verify the files in '{_dependencyManager.BinDirectory}'.");
            }
        }

        StatusMessage = "Preparing Deno for YouTube's JavaScript challenge...";
        var denoProgress = new Progress<ProvisioningProgress>(p =>
        {
            StatusMessage = $"{p.CurrentStep} ({p.PercentComplete:F0}%)";
        });

        bool denoReady = await _dependencyManager.EnsureDenoAsync(denoProgress);
        Dependencies = await _dependencyManager.CheckStatusAsync();
        if (!denoReady || !Dependencies.YtDlpInstalled || !Dependencies.DenoInstalled)
        {
            throw new InvalidOperationException(
                $"yt-dlp could not prepare the required YouTube extraction components. Check your internet connection and verify yt-dlp.exe and deno.exe in '{_dependencyManager.BinDirectory}'.");
        }
    }

    private async Task EnsureAudioDependenciesReadyAsync()
    {
        Dependencies = await _dependencyManager.CheckStatusAsync();
        if (Dependencies.AllReady)
        {
            return;
        }

        StatusMessage = "Preparing yt-dlp, Deno, and FFmpeg for audio downloads...";
        var progress = new Progress<ProvisioningProgress>(p =>
        {
            StatusMessage = $"{p.CurrentStep} ({p.PercentComplete:F0}%)";
        });

        bool installed = await _dependencyManager.ProvisionAllAsync(progress);
        Dependencies = await _dependencyManager.CheckStatusAsync();
        if (!installed || !Dependencies.AllReady)
        {
            throw new InvalidOperationException(
                $"yt-dlp, Deno, and FFmpeg could not all be installed. Check your internet connection. Binaries are expected in '{_dependencyManager.BinDirectory}'.");
        }
    }

    [RelayCommand]
    public async Task SignInAsync()
    {
        if (RequestLoginDialog != null)
        {
            await RequestLoginDialog.Invoke();
            AuthStatus = await _authManager.GetCurrentAuthStatusAsync();
            UpdateAccountLibraryStatus();
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
        AccountPlaylists.Clear();
        AccountLibraryStatus = "Sign in to browse playlists in your YouTube account.";
        StatusMessage = "Signed out. Switched to Guest Mode.";
    }

    [RelayCommand]
    public async Task SaveCurrentSettingsAsync()
    {
        var settings = new UserSettings(
            OutputDirectory: OutputDirectory,
            QualityPreset: QualityPreset,
            EmbedAlbumArt: EmbedAlbumArt,
            EmbedMetadata: EmbedMetadata,
            CleanTitle: CleanTitle,
            SearchFilter: SearchFilter
        );
        await _settingsService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public async Task BrowseOutputFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Destination Folder for Audio Files",
            InitialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
            await SaveCurrentSettingsAsync();
        }
    }

    private static bool IsUrl(string text)
    {
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateAccountLibraryStatus()
    {
        AccountLibraryStatus = AuthStatus.IsLoggedIn
            ? "Refresh to load playlists available to this YouTube account."
            : "Sign in to browse playlists in your YouTube account.";
    }

    private static bool IsActiveDownloadState(DownloadState state) =>
        state is DownloadState.Analyzing
            or DownloadState.Downloading
            or DownloadState.ExtractingAudio
            or DownloadState.Tagging
            or DownloadState.EmbeddingThumbnail;
}
