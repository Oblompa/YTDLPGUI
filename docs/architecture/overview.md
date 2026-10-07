# YtDlpAudio - System Architecture Overview

## 1. Executive Summary
`YtDlpAudio` is a native Windows desktop application built on **.NET 9 + WPF (or WinUI 3)** designed to provide a GUI for downloading audio tracks, albums, and playlists from YouTube and YouTube Music. It automatically manages all CLI prerequisites (`yt-dlp`, `ffmpeg`, `ffprobe`) in an isolated directory and features integrated YouTube authentication (via Microsoft Edge WebView2) to support **YouTube Premium** high-bitrate streams (itag 141 - 256kbps AAC) and private library playlists (Liked Songs, Watch Later, user-curated lists).

---

## 2. Solution Structure & Layering

The codebase is organized into clean, decoupled layers following Clean Architecture and MVVM patterns:

```
YtDlpAudio/
├── docs/
│   ├── architecture/
│   │   └── overview.md                  <-- This document
│   └── plans/
│       ├── 01-dependency-provisioner.md  <-- Specialist 1: Toolchain & Prereqs
│       ├── 02-auth-and-cookies.md       <-- Specialist 2: WebView2 & YouTube Auth
│       ├── 03-ytdlp-process-engine.md   <-- Specialist 3: CLI Runner & Stream Parser
│       ├── 04-audio-metadata-pipeline.md<-- Specialist 4: MP3/ID3 & Cover Art
│       ├── 05-gui-and-viewmodels.md     <-- Specialist 5: MVVM GUI & Interaction
│       └── 06-integration-and-testing.md<-- Specialist 6: Test Suite & Mocks
├── src/
│   ├── YtDlpAudio.Core/                  <-- Pure domain models, abstractions, events
│   │   ├── Models/
│   │   └── Services/
│   ├── YtDlpAudio.Infrastructure/        <-- CLI process wrappers, HTTP, Auth, IO
│   │   ├── Process/
│   │   ├── Dependencies/
│   │   └── Auth/
│   └── YtDlpAudio.UI/                    <-- Views, ViewModels, Converters, Controls
│       ├── ViewModels/
│       └── Views/
└── tests/
    └── YtDlpAudio.Core.Tests/            <-- Unit tests & Process stream mocks
```

---

## 3. Core Component Diagram & Information Flow

```
┌────────────────────────────────────────────────────────────────────────┐
│                               UI LAYER                                 │
│  [MainView] ◄──► [MainViewModel] ◄──► [DownloadQueueViewModel]         │
│  [LoginModal] ◄──► [AuthViewModel]                                     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Commands / Notifications
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                              CORE LAYER                                │
│  Contracts:                                                            │
│   - IDependencyManager    - IAuthManager         - IYtDlpRunner        │
│   - IAudioPipelineBuilder - IMetadataCleaner     - IPlaylistService    │
│  Models:                                                               │
│   - DownloadTask          - TrackMetadata        - AudioQualityPreset  │
│   - DownloadProgress      - PlaylistInfo         - AuthSession         │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Implementations
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                         INFRASTRUCTURE LAYER                           │
│  ┌─────────────────────────┐  ┌─────────────────────────────────────┐  │
│  │   DependencyManager     │  │          WebView2AuthManager        │  │
│  │ (GitHub Releases API,   │  │ (CoreWebView2CookieManager, DPAPI,  │  │
│  │  SHA-256 verification,  │  │  Netscape Cookies.txt serializer)   │  │
│  │  Zip/Tar extraction)    │  └─────────────────────────────────────┘  │
│  └─────────────────────────┘  ┌─────────────────────────────────────┐  │
│  ┌─────────────────────────┐  │         AudioPipelineBuilder        │  │
│  │     YtDlpProcessRunner  │  │ (Flag generation, ID3 embedding,    │  │
│  │ (Process stdout/stderr  │  │  libmp3lame quality arguments,      │  │
│  │  stream regex parser)   │  │  WebP-to-JPEG cover art flags)      │  │
│  └─────────────────────────┘  └─────────────────────────────────────┘  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Spawns & Controls
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                    LOCAL TOOLCHAIN (Isolated %LOCALAPPDATA%)           │
│     [yt-dlp.exe] ◄───────────────► [ffmpeg.exe] & [ffprobe.exe]        │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Key Cross-Cutting Technical Decisions

### 4.1 Dependency Isolation
* All CLI binaries are placed in `%LOCALAPPDATA%\YtDlpAudio\bin\`.
* The system `PATH` is never modified.
* All CLI invocations explicitly pass `--ffmpeg-location "%LOCALAPPDATA%\YtDlpAudio\bin\"`.

### 4.2 Security & Credential Protection
* YouTube session cookies are stored in memory during active runs and exported to a temporary Netscape file only for the duration of the command, or stored encrypted on disk using **Windows DPAPI** (`ProtectedData.Protect` scoped to `DataProtectionScope.CurrentUser`).
* Logging sanitizes any cookie or auth token strings.

### 4.3 YouTube Premium Audio Pipeline
* YouTube standard audio streams default to 128kbps AAC or 160kbps Opus.
* When authenticated with a YouTube Premium account, the tool requests `ba[format_id=141]/ba` (format 141 = 256kbps AAC).
* FFmpeg transcodes using `libmp3lame` to user-selected presets (320kbps CBR or VBR V0).
* WebP thumbnails are converted to JPEG via `--convert-thumbnails jpg` prior to embedding as ID3v2 APIC frames.

---

## 5. Specialist Roles for Implementation

| Plan File | Specialist Domain | Responsibility |
|---|---|---|
| `01-dependency-provisioner.md` | Prerequisite Specialist | Auto-downloading, verifying, and updating `yt-dlp` and `ffmpeg`. |
| `02-auth-and-cookies.md` | Auth Specialist | In-app WebView2 Google login, cookie harvesting, DPAPI storage, Premium check. |
| `03-ytdlp-process-engine.md` | Process & CLI Specialist | Non-blocking process execution, regex stdout parser, pause/cancel mechanics. |
| `04-audio-metadata-pipeline.md` | Audio & Tagging Specialist | Audio format CLI generation, WebP->JPEG cover embedding, ID3 tagging, title cleanup. |
| `05-gui-and-viewmodels.md` | MVVM & UI Specialist | Modern Windows desktop UI, playlist track inspector, real-time download queue. |
| `06-integration-and-testing.md` | QA & Testing Specialist | Simulated CLI streams, test harnesses, end-to-end verification. |
