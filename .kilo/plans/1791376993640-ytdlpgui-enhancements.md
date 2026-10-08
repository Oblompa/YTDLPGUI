# YTDLPGUI Enhancements Plan

**Created**: 2026-10-07  
**Updated**: 2026-10-08
**Status**: Open - reported download blocker and requested UX improvements
**Based on**: Comprehensive code review and architectural critique of specialist plans (01-06)

---

## Summary

The core architecture, CLI process runner, dependency management, optional authentication, and MVVM scaffolding are structured. This plan captures necessary UI data-binding corrections, DI lifecycle alignment, production hardening, and essential UX improvements for a robust release. The latest user validation confirms search and sign-in work, but reports that every download fails. Treat successful authorized audio downloads as the release-blocking priority before adding the account-library and queue-management improvements below.

---

## Newly Reported Release Priorities

### P0. Diagnose and Fix All Audio Downloads Failing
**Status**: Download pipeline verified end-to-end with authorized Creative Commons media; YouTube JavaScript challenge runtime fix implemented and published for retest (2026-10-08)
**Files**: `src/YtDlpAudio.Infrastructure/Process/YtDlpProcessRunner.cs`, `AudioDownloadService.cs`, `YtDlpArgumentBuilder.cs`, `src/YtDlpAudio.UI/ViewModels/MainViewModel.cs`, `tests/`

**Issue**: The user reports that searches and sign-in work, but every download fails. Do not assume the cause. Reproduce with a source the tester is authorized to download, then retain enough diagnostics to identify whether the failure occurs during source extraction, format selection, media transfer, FFmpeg conversion, metadata/thumbnail embedding, or output-file creation.

**Implementation and verification (2026-10-08)**: The process runner now drains and retains both output streams, logs sanitized failure details and exit codes, and the queue displays per-track diagnostics. Downloads report the final output path and reject success unless a non-empty file with the requested audio extension exists. Deterministic regression tests cover process stderr/exit-code capture and output validation. The published Windows application passed `tests/e2e/Run-ApplicationE2E.ps1` in guest mode using a Big Buck Bunny result from the Blender Foundation under its Creative Commons license; search, queueing, download, completion status, and non-empty MP3 verification all succeeded. The original failure did not reproduce in this authorized end-to-end run.

**Follow-up diagnosis (2026-10-08)**: A subsequent report showed YouTube signature/`n` challenge solving failures followed by “Only images are available”. The provisioned Windows bundle had no JavaScript runtime. Deno 2.3.0+ is now provisioned beside yt-dlp, checked as a required dependency, and passed explicitly via `--js-runtimes` for metadata queries and audio downloads. Official yt-dlp Windows executables bundle the EJS challenge scripts, so a separate script package download is not required. A smoke test using the official Deno 2.9.7 release successfully retrieved 53 formats for Blender Foundation's Big Buck Bunny video without downloading media. Regression tests pass and the refreshed portable executable is published; an end-to-end MP3 download retest remains pending.

**Implementation and investigation**:
1. Reproduce in guest mode and, where relevant, signed-in mode using a known authorized source. Record yt-dlp exit code, sanitized arguments, stderr, final process state, and whether the expected output exists; never record cookie contents or authentication tokens.
2. Verify the provisioned yt-dlp and FFmpeg executables and versions, FFmpeg path passed to yt-dlp, output-folder existence and write access, generated output template, stream-selection options, and MP3/metadata post-processing arguments.
3. Preserve useful yt-dlp stderr and postprocessor diagnostics through `YtDlpResult` into the queue row and status UI. Report a concrete failure stage/message rather than only marking the row failed.
4. Fix the demonstrated root cause and add regression coverage for its command construction, result propagation, or file handling. Keep authentication optional; public downloads must work without sign-in.

**Acceptance criteria**:
- A first selected authorized track downloads in guest mode, exits successfully, and creates a non-empty MP3 in the configured output directory.
- An authorized playlist's selected tracks download successfully; a failure on one item does not hide diagnostics or corrupt the status of other items.
- When yt-dlp or FFmpeg fails, the user can see a useful, sanitized error and the log records enough details to diagnose it.
- Automated test coverage validates the previously failing behavior and confirms expected output-file verification.

### P1. Browse Playlists in the Signed-In Account Library
**Status**: Implemented; automated tests and a read-only live YouTube Music library request passed (2026-10-08).
**Files**: `src/YtDlpAudio.Infrastructure/Auth/`, `src/YtDlpAudio.Infrastructure/Process/`, `src/YtDlpAudio.UI/Views/MainWindow.xaml`, `src/YtDlpAudio.UI/ViewModels/MainViewModel.cs`

**Feature**: When authenticated, provide separate YouTube and YouTube Music library views. Each lists playlists visible to that service account, including supported private or saved playlists, and lets users inspect/select playlists for the existing download queue. Keep the two library flows separate from each other and from public search; sign-in remains optional.

**Implementation and investigation**:
1. Implemented account enumeration through yt-dlp's `https://www.youtube.com/feed/playlists` endpoint, using a temporary export of the authenticated cookies. The response must contain an `entries` array; authentication/query errors are surfaced as actionable failures rather than treated as a complete listing.
2. Added YouTube Music library enumeration through its authenticated `FEmusic_liked_playlists` browse API, SAPISIDHASH authorization, and continuation paging. The request uses the current Music web-client API key/version and the protected session's temporary cookie export.
3. Added separate YouTube and YouTube Music tabs with independent result lists, refresh commands, loading/empty states, and source-specific error messages. Results are never merged; failure in one tab does not clear the other.
4. Selecting a listed playlist reuses the existing URL metadata expansion and queue path.
5. Temporary cookie exports are deleted after use; error messages redact URLs and credential-like values. Public search remains available when signed out.
6. Service tests verify authenticated enumeration, guest rejection, YouTube Music request authentication/parsing/pagination, and temporary-cookie cleanup. A read-only live request parsed multiple pages without logging playlist names or credentials.

**Acceptance criteria**:
- A signed-in user can independently refresh and see playlists available from YouTube and YouTube Music.
- The two services remain separate, and failure in one does not prevent browsing the other.
- Selecting a listed playlist loads its entries into the existing queue workflow.
- A guest user still searches public content and receives a clear sign-in prompt only when requesting the private library.
- Expired or insufficient authentication produces a visible actionable error in the affected library without exposing credentials.

### P1. Bulk Clear and Multi-Row Queue Removal
**Status**: Implemented; automated tests pass (2026-10-08)
**Files**: `src/YtDlpAudio.UI/Views/MainWindow.xaml`, `src/YtDlpAudio.UI/ViewModels/MainViewModel.cs`, `src/YtDlpAudio.UI/ViewModels/TrackItemViewModel.cs`

**Feature**: Allow selecting multiple queue rows and removing them together, plus a separate action to clear the queue.

**Implementation and UX rules**:
1. Implemented extended full-row selection and “Remove Selected”; row selection is independent of the download checkbox.
2. Implemented “Clear Queue” with a default-No confirmation for non-empty queues.
3. Queue mutations are blocked during an active download batch with a cancel-first message; active-state rows are also protected.
4. Queue operations only remove view-model rows; they do not delete downloaded files or alter search results. Unit tests cover checkbox preservation, canceling/confirming clear, and active-batch protection.

**Acceptance criteria**:
- Ctrl/Shift selection can remove several queue rows in one action without changing their individual download-selection state.
- Clear Queue empties removable rows only after confirmation; canceling the confirmation leaves the queue unchanged.
- In-progress items are not silently abandoned, while queued, completed, or failed items can be removed according to the agreed UI rule.

---

## Tasks

### High Priority

#### 1. Fix ComboBox SelectedValue Bindings (SearchFilter & QualityPreset)
**File**: `src/YtDlpAudio.UI/Views/MainWindow.xaml` (lines 104-108 and 118-123)
**Issue**: Both the `SearchFilter` and `QualityPreset` ComboBoxes bind `SelectedValue` to their respective enum properties and define `Tag="{x:Static ...}"`, but omit `SelectedValuePath="Tag"`. Without `SelectedValuePath`, WPF does not evaluate the `Tag` property when mapping the selection back to the bound enum property, causing selections to fail or reset.
**Fix**: Add `SelectedValuePath="Tag"` directly to both ComboBox definitions:

```xml
<!-- SearchFilter ComboBox -->
<ComboBox Grid.Column="1" Margin="8,0" SelectedValuePath="Tag" SelectedValue="{Binding SearchFilter}" Width="110"
          VerticalContentAlignment="Center" Background="#18181B" Foreground="#FAFAFA">
    <ComboBoxItem Content="Tracks" Tag="{x:Static services:SearchFilterType.Tracks}" IsSelected="True" />
    <ComboBoxItem Content="Playlists" Tag="{x:Static services:SearchFilterType.Playlists}" />
</ComboBox>

<!-- QualityPreset ComboBox -->
<ComboBox Width="130" SelectedValuePath="Tag" SelectedValue="{Binding QualityPreset}" Margin="0,0,18,0">
    <ComboBoxItem Content="320k CBR (Max)" IsSelected="True" Tag="{x:Static models:AudioQualityPreset.Cbr320k}" />
    <ComboBoxItem Content="VBR V0 (Optimal)" Tag="{x:Static models:AudioQualityPreset.VbrV0}" />
    <ComboBoxItem Content="192k Standard" Tag="{x:Static models:AudioQualityPreset.Cbr192k}" />
</ComboBox>
```
**Validation**: Toggling the dropdowns accurately updates `SearchFilter` and `QualityPreset` in `MainViewModel`.

---

#### 2. Fix LoginWindow DI Resolution & Singleton Sharing
**Files**: `src/YtDlpAudio.UI/App.xaml.cs`, `src/YtDlpAudio.UI/Views/MainWindow.xaml.cs`, `src/YtDlpAudio.UI/Views/LoginWindow.xaml.cs`
**Issue**: 
In `MainWindow.xaml.cs`, `ShowLoginDialogAsync` calls `new LoginWindow()`, which bypasses DI and instantiates a second, unmanaged `WebView2AuthManager` rather than the singleton registered in `App.xaml.cs`.
**Fix**:
1. In `App.xaml.cs`: Register `WebView2AuthManager` explicitly as a singleton implementation of `IAuthManager`:
   ```csharp
   services.AddSingleton<WebView2AuthManager>();
   services.AddSingleton<IAuthManager>(sp => sp.GetRequiredService<WebView2AuthManager>());
   services.AddTransient<LoginWindow>();
   ```
2. In `MainWindow.xaml.cs`: Inject `IServiceProvider` (or a `Func<LoginWindow>` factory):
   ```csharp
   private readonly Func<LoginWindow> _loginWindowFactory;

   public MainWindow(MainViewModel viewModel, Func<LoginWindow> loginWindowFactory)
   {
       InitializeComponent();
       _viewModel = viewModel;
       _loginWindowFactory = loginWindowFactory;
       DataContext = _viewModel;
       _viewModel.RequestLoginDialog += ShowLoginDialogAsync;
       Loaded += async (_, _) => await _viewModel.InitializeCommand.ExecuteAsync(null);
   }

   private Task ShowLoginDialogAsync()
   {
       var loginWin = _loginWindowFactory();
       loginWin.Owner = this;
       loginWin.ShowDialog();
       return Task.CompletedTask;
   }
   ```
**Validation**: Clicking "Sign In" opens the modal using the exact singleton `WebView2AuthManager`, preserving in-memory state and cookie caches.

---

#### 3. Add Native Folder Browser Picker for Destination Directory
**Files**: `src/YtDlpAudio.UI/ViewModels/MainViewModel.cs`, `src/YtDlpAudio.UI/Views/MainWindow.xaml`
**Issue**: Users must currently type or paste the output path manually.
**Fix**:
1. Add `BrowseOutputFolderCommand` to `MainViewModel`:
   ```csharp
   [RelayCommand]
   private void BrowseOutputFolder()
   {
       var dialog = new Microsoft.Win32.OpenFolderDialog
       {
           Title = "Select Destination Folder for Audio Files",
           InitialDirectory = OutputDirectory
       };

       if (dialog.ShowDialog() == true)
       {
           OutputDirectory = dialog.FolderName;
       }
   }
   ```
2. In `MainWindow.xaml`, add a `Browse...` button next to the `Save To:` TextBox:
   ```xml
   <TextBox Text="{Binding OutputDirectory}" Width="220" FontSize="12" VerticalAlignment="Center" />
   <Button Content="Browse..." Command="{Binding BrowseOutputFolderCommand}" Margin="6,0,0,0" Padding="8,4" FontSize="12" Background="#374151" />
   ```
**Validation**: Clicking "Browse..." launches the modern Windows folder picker and updates `OutputDirectory`.

---

### Medium Priority

#### 4. Add User Settings Persistence
**Files**: `src/YtDlpAudio.Core/Services/ISettingsService.cs`, `src/YtDlpAudio.Infrastructure/Services/JsonSettingsService.cs`, `MainViewModel.cs`, `App.xaml.cs`
**Scope**: Automatically persist preferences across restarts in `%LOCALAPPDATA%\YtDlpAudio\settings.json`:
- `OutputDirectory` (string)
- `QualityPreset` (AudioQualityPreset)
- `EmbedAlbumArt` (bool)
- `EmbedMetadata` (bool)
- `CleanTitle` (bool)
- `SearchFilter` (SearchFilterType)

**Implementation**:
1. Define `UserSettings` record and `ISettingsService` in `YtDlpAudio.Core`.
2. Implement `JsonSettingsService` in `YtDlpAudio.Infrastructure` using `System.Text.Json` (indented, atomic write via temp file).
3. Load settings during `MainViewModel.InitializeAsync`.
4. Save settings upon changes or window close.

**Validation**: Modify settings, restart the application, and verify all UI controls restore the saved state.

---

#### 5. Add Structured Logging (Serilog)
**Files**: `App.xaml.cs`, Infrastructure services, ViewModels
**Scope**: Provide diagnostics for toolchain provisioning, auth events, search queries, and CLI process execution.
**Implementation**:
1. Add `Serilog.Sinks.File` and `Serilog.Sinks.Console` packages to the UI project.
2. Initialize rolling file logger in `%LOCALAPPDATA%\YtDlpAudio\logs\log-.txt` with 7-day retention.
3. Ensure sensitive values (like cookie tokens or DPAPI raw bytes) are excluded from log outputs.

**Validation**: Log files capture timestamped entries with process execution times and error details.

---

#### 6. Portable / No-Install Mode (`portable.dat` & Relative Data Root)
**Files**: `src/YtDlpAudio.Core/Services/IAppPathsService.cs`, `src/YtDlpAudio.Infrastructure/Services/AppPathsService.cs`, `DependencyManager.cs`, `WebView2AuthManager.cs`, `JsonSettingsService.cs`, `App.xaml.cs`, `YtDlpAudio.UI.csproj`
**Scope**: Allow running the application from a portable folder, USB stick, or uninstalled directory without writing to `%LOCALAPPDATA%` or leaving system traces.

**Design & Mechanics**:
1. **Portable Trigger Detection**:
   On startup, check for the presence of a `portable.dat` or `portable.mode` marker file in the application directory (where `YtDlpAudio.UI.exe` resides), OR check if a `--portable` CLI argument is passed:
   ```csharp
   public interface IAppPathsService
   {
       bool IsPortable { get; }
       string BaseDataDirectory { get; } // %LOCALAPPDATA%\YtDlpAudio OR <AppDir>\data
       string BinDirectory { get; }      // BaseDataDirectory\bin
       string AuthDirectory { get; }     // BaseDataDirectory\auth
       string LogsDirectory { get; }     // BaseDataDirectory\logs
       string SettingsFilePath { get; }  // BaseDataDirectory\settings.json
       string DefaultDownloadsDirectory { get; } // BaseDataDirectory\downloads OR %USERPROFILE%\Music\YtDlpDownloads
   }
   ```
2. **Path Centralization**:
   Refactor `DependencyManager`, `WebView2AuthManager`, `JsonSettingsService`, and Serilog configuration to receive directory paths from `IAppPathsService` instead of hardcoding `Environment.SpecialFolder.LocalApplicationData`.
3. **WebView2 Portability & Sandboxing**:
   - WebView2 user data is directed to `<AppDir>\data\webview2_data` in portable mode.
   - Note on DPAPI Cookies in Portable Mode: Windows DPAPI (`DataProtectionScope.CurrentUser`) is tied to the current Windows user login. When moving a USB stick to another computer or user account, DPAPI decryption safely fails; `WebView2AuthManager` detects this, ignores the un-decryptable session file, and boots gracefully in Guest Mode without crashing. An optional password-based or machine-independent AES credential option can be provided for portable account persistence.
4. **Self-Contained Single-File Publish**:
   Configure portable build target in `YtDlpAudio.UI.csproj`:
   ```xml
   <PropertyGroup Condition="'$(Configuration)' == 'Portable'">
       <PublishSingleFile>true</PublishSingleFile>
       <SelfContained>true</SelfContained>
       <RuntimeIdentifier>win-x64</RuntimeIdentifier>
       <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
       <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
   </PropertyGroup>
   ```
   A ready-to-run release script/command will publish a portable bundle containing:
   ```
   YtDlpAudio-Portable/
   ├── YtDlpAudio.exe
   ├── portable.dat
   └── data/
       ├── bin/         (yt-dlp.exe, ffmpeg.exe, ffprobe.exe)
       ├── auth/
       ├── logs/
       └── settings.json
   ```

**Validation**:
- Place `portable.dat` next to executable → app stores binaries, settings, and logs in `./data/` instead of `%LOCALAPPDATA%`.
- Delete `portable.dat` → app falls back cleanly to standard `%LOCALAPPDATA%` behavior.

---

### Low Priority

#### 7. Improve Extraction Progress Reporting in Dependency Manager
**File**: `src/YtDlpAudio.Infrastructure/Dependencies/DependencyManager.cs`
**Issue**: HTTP streaming already calculates fine-grained progress (0-50% for yt-dlp, 50-90% for FFmpeg zip), but the ZIP extraction step jumps from 90% directly to 95%.
**Fix**: Update `DownloadAndExtractFFmpegInternalAsync` to enumerate archive entries and report progress per extracted file during the 90%-95% range.

**Validation**: Smooth progress feedback throughout the entire provisioning cycle.

---

#### 8. Partial/Temp File Cleanup on Cancelled Downloads
**Files**: `src/YtDlpAudio.Infrastructure/Process/AudioDownloadService.cs`
**Issue**: If a download or audio transcoding process is cancelled midway, yt-dlp/ffmpeg may leave `.part`, `.temp.*`, or incomplete `.mp3` files in the output directory.
**Fix**: In the cancellation handler of `AudioDownloadService`, inspect the output directory for `.part` or `.temp` files matching the target item and perform a best-effort deletion.

**Validation**: Cancelling a download cleanly purges partially downloaded or transcoded files.

---

#### 9. Add Playlist Expansion in Queue (UX)
**Files**: `MainWindow.xaml`, `MainViewModel.cs`
**Feature**: Context menu item on playlist queue rows: "Expand Playlist" → queries flat-playlist metadata and expands the playlist into individual track items.

**Validation**: Right-clicking a playlist item expands it into selectable tracks with durations and metadata.

---

#### 10. Add End-to-End Audio Download Integration Test
**File**: `tests/YtDlpAudio.Core.Tests/AudioDownloadIntegrationTests.cs`
**Scope**: Validate the end-to-end pipeline using `MockYtDlpRunner`:
- Simulating search → enqueue → download execution with progress reporting → verification of completion states.

**Validation**: `dotnet test` executes and passes all integration workflows offline.

---

## Task Dependency Matrix

```
1 (ComboBox Bindings) ──┐
2 (LoginWindow DI)   ──┼─→ UI & Process Integrity (Ready to implement)
3 (Folder Browser)   ──┘

4 (Settings) ──────────┐
5 (Structured Logs)  ──┼─→ Production Quality & Diagnostics
6 (Portable Mode)    ──┤
8 (Temp Cleanup)     ──┘

7 (Extraction Progress) ─ Independent
9 (Playlist Expand)     ─ Requires Task 1
10 (Integration Tests)  ─ Independent (Offline mocks)

Newly reported priorities:
P0 (All downloads failing) ─ Release blocker; diagnose first and add regression/E2E coverage
P1 (Account library browser) ─ Requires authenticated playlist enumeration and reuses playlist queue flow
P1 (Bulk queue management) ─ Independent of auth and download-engine changes
```

---

## Validation Checklist

| Check | Target / Command |
|---|---|
| Search Filter ComboBox | Toggle between Tracks and Playlists; verify bound property updates |
| Audio Quality ComboBox | Toggle between 320k, V0, 192k; verify bound property updates |
| Folder Browser Picker | Click "Browse..."; verify native folder dialog opens and updates path |
| Login Dialog Lifecycle | Click "Sign In"; verify modal opens with shared singleton instance |
| Settings Persistence | Change settings, close app, reopen; verify settings are restored |
| Portable Mode Detection | Place `portable.dat`; verify data routes to `./data/` instead of `%LOCALAPPDATA%` |
| Clean Cancellation | Cancel an active download; verify no `.part` files remain on disk |
| Authorized Guest Download | Download an authorized public track without login; verify a non-empty MP3 and useful failure diagnostics |
| Playlist Download | Download selected items from an authorized playlist; verify per-item status and errors |
| Account Library | Signed-in user can browse accessible private playlists; guest mode still supports public search |
| Bulk Queue Removal | Remove Ctrl/Shift-selected rows without changing download checkboxes; clear requires confirmation and protects active work |
| Offline Test Suite | Run `dotnet test tests/YtDlpAudio.Core.Tests` |

---

## File References

- Architecture Overview: `docs/architecture/overview.md`
- Specialist Plans: `docs/plans/01-*.md` through `06-*.md`
- Solution File: `YtDlpAudio.sln`
- Main UI View: `src/YtDlpAudio.UI/Views/MainWindow.xaml`
- Main ViewModel: `src/YtDlpAudio.UI/ViewModels/MainViewModel.cs`
- App Composition: `src/YtDlpAudio.UI/App.xaml.cs`
- Authentication and account library plan: `docs/plans/02-auth-and-cookies.md`
- Download diagnostics plan: `docs/plans/03-ytdlp-process-engine.md`
- Audio conversion and output verification plan: `docs/plans/04-audio-metadata-pipeline.md`
- Queue and library UI plan: `docs/plans/05-gui-and-viewmodels.md`
- Download and UI automation test plan: `docs/plans/06-integration-and-testing.md`