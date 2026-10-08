# Specialist Plan 05: GUI & MVVM Architecture

## Objective
Design the Windows Presentation Foundation (WPF) / WinUI 3 desktop interface, following the MVVM pattern with `CommunityToolkit.Mvvm`. Provide an audio-centric workflow featuring single/playlist URL fetching, track selection, live progress monitoring, and account management.

---

## 1. UI Layout & Visual Wireframe

```
┌────────────────────────────────────────────────────────────────────────┐
│  🎵 YtDlpAudio   [● Guest Mode / 👑 Premium]      [⚙️ Settings] [🔄 CLI]│
├────────────────────────────────────────────────────────────────────────┤
│  Search or Paste URL:                                                  │
│  ┌──────────────────────────────────────────────┬──────────┬─────────┐ │
│  │ bohemian rhapsody / https://...              │ [Tracks▼]│ [🔍 Run]│ │
│  └──────────────────────────────────────────────┴──────────┴─────────┘ │
├────────────────────────────────────────────────────────────────────────┤
│  Audio Settings:                                                       │
│  Format: [MP3 (libmp3lame) ▼]   Quality: (•) 320k CBR  ( ) VBR V0      │
│  Save To: [ C:\Users\Bruno\Music\Downloads               ] [Browse...] │
│  [x] Embed Album Art   [x] Embed ID3 Tags   [x] Clean Title Clutter    │
├────────────────────────────────────────────────────────────────────────┤
│  [ Search Results (25) ]      [ Download Queue (4 Selected) ]          │
│  ┌──────┬────────────────────────────┬──────────────────┬──────┬─────┐ │
│  │ Type │ Title                      │ Artist / Channel │Dur/Tr│Act  │ │
│  ├──────┼────────────────────────────┼──────────────────┼──────┼─────┤ │
│  │Track │ Bohemian Rhapsody          │ Queen Official   │05:55 │[+Q] │ │
│  │Track │ Bohemian Rhapsody (Live)   │ Queen Official   │06:12 │[+Q] │ │
│  │Playl.│ Queen - Greatest Hits      │ Top Albums       │17 tr │[+Q] │ │
│  └──────┴────────────────────────────┴──────────────────┴──────┴─────┘ │
├────────────────────────────────────────────────────────────────────────┤
│  Active Task: Bohemian Rhapsody.mp3                                    │
│  [████████████████████████████░░░░░░░░░░░░░░░░░░] 62% • 12.8 MB/s     │
│  Overall: [████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░] 1 of 4 completed   │
│                                                                        │
│  [▶ Download Selected]              [⏸ Pause]               [⏹ Cancel]│
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. ViewModel Hierarchy

```
MainViewModel
├── AuthViewModel            (Login modal state, Premium badge status)
├── DependencyViewModel      (CLI provisioner state, download progress)
├── SettingsViewModel        (Default output folder, bitrates, templates)
├── TrackListViewModel       (Collection of TrackItemViewModel for playlists)
└── DownloadQueueViewModel   (Active downloads, speeds, ETAs, aggregate progress)
```

---

## 3. Core ViewModel Definitions

### 3.1 `TrackItemViewModel`
```csharp
public partial class TrackItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private string _thumbnailUrl = string.Empty;
    [ObservableProperty] private DownloadState _state = DownloadState.Queued;
    [ObservableProperty] private double _progress;
}
```

### 3.2 `MainViewModel`
```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly IDependencyManager _dependencyManager;
    private readonly IAuthManager _authManager;
    private readonly IYtDlpRunner _runner;

    [ObservableProperty] private string _urlInput = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private AuthStatus _authStatus = new(false, false, null, null);
    [ObservableProperty] private ObservableCollection<TrackItemViewModel> _tracks = new();

    [RelayCommand]
    private async Task FetchMetadataAsync()
    {
        // 1. Validate URL
        // 2. Run --flat-playlist --dump-single-json
        // 3. Populate Tracks collection
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        // 1. Filter selected tracks
        // 2. Iterate queue and execute audio conversion pipeline
        // 3. Update real-time progress per track and overall
    }
}
```

---

## 4. WebView2 Authentication Dialog View
* Dedicated window `LoginWindow.xaml` hosting `<wv2:WebView2 Name="AuthWebView" />`.
* Displays Google/YouTube login interface.
* Listens for navigation completions to detect when login is achieved (`youtube.com` with `SAPISID` or `LOGIN_INFO` cookies present).
* Closes modal with `DialogResult = true` once cookies are securely stored.

---

## 5. Signed-In Account Library Browser

Add separate YouTube and YouTube Music account-library views available when signed in. Each view lists playlists from its own service and feeds a selected playlist into the existing inspection and queue flow. Do not merge or deduplicate across the services.

UI and interaction requirements:
- Provide an independent refresh button, loading state, empty state, and actionable expired-session/access-denied feedback for each service.
- Keep public search available in guest mode; request sign-in only when an account library is opened.
- Reuse playlist result/track view models rather than maintaining a second playlist workflow.
- Reuse the saved Google session for both services. A YouTube Music authentication failure must remain local to that tab and provide actionable reauthentication guidance without clearing YouTube results.
- Do not expose cookie values or log private account data.

## 6. Lazy Playlist Track Expansion in Search

- Playlist search results provide an explicit **Expand tracks** action with row details for loading state, empty results, errors, and the fetched tracks.
- Fetch flat track metadata only for the playlist the user expands; do not enumerate every playlist during search.
- Allow queueing an individual track from expanded details, while preserving the existing action to queue an entire playlist.
- Use the same authenticated metadata flow for private YouTube and YouTube Music playlist URLs.

## 7. Queue Bulk Management

Add extended row selection to the download queue and provide distinct `Remove Selected` and `Clear Queue` actions.

- Row selection for deletion must be independent from the existing per-track checkbox that controls whether a row is included in a download.
- `Clear Queue` must confirm before removing a non-empty queue.
- Do not silently remove or abandon a downloading row. Protect active work and explain the remaining rows, or require the user to cancel active work first.
- Removing queue entries must not delete downloaded files or alter search results.

**Validation**: Ctrl/Shift-select multiple rows and remove them while confirming download checkboxes remain unchanged; cancel a clear confirmation and confirm the queue is preserved; confirm active downloads are not silently discarded.

## 7. Surface Actionable Download Failures

Show per-track failure detail from the process result rather than only a generic `Failed` state. Include the relevant yt-dlp/FFmpeg stage and actionable message, while keeping credentials and cookie values out of the UI and logs. Provide a useful status for dependency, extraction, transfer, conversion, tagging, and output-file failures.
