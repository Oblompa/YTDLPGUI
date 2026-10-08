# Specialist Plan 04: Audio & Metadata Processing Pipeline

## Objective
Design the audio extraction, conversion, tagging, and cover art pipeline to produce studio-grade MP3 files with clean ID3 tags and high-resolution embedded album art from YouTube and YouTube Music.

---

## 1. Technical Audio Pipeline Specification

### 1.1 Source Audio Formats on YouTube
| Format ID | Codec | Container | Nominal Bitrate | Availability |
|---|---|---|---|---|
| `251` | Opus | WebM | ~160 kbps | All users |
| `140` | AAC-LC | M4A | ~128 kbps | All users |
| `141` | AAC-LC | M4A | **256 kbps** | **YouTube Premium / YT Music Only** |

### 1.2 Transcoding to MP3 (`libmp3lame`)
Because YouTube delivers Opus or AAC, transcoding to MP3 is handled by FFmpeg's `libmp3lame`.
* **CBR 320 kbps Preset:** `--audio-quality 320k` (ideal for hardware car players, older MP3 players).
* **VBR V0 Preset:** `--audio-quality 0` (optimal acoustic transparency with variable bitrate ~245 kbps).
* **Balanced Preset:** `--audio-quality 192k` (moderate file size).

---

## 2. Cover Art / Thumbnail Pipeline

### 2.1 The WebP Challenge
* YouTube serves video/track thumbnails primarily as **WebP**.
* Standard MP3 ID3v2.3/2.4 specifications require **JPEG** or **PNG** for the `APIC` (Attached Picture) frame. Storing WebP in ID3 causes Windows Explorer, iTunes, and automotive head units to fail to display album art.

### 2.2 Solution Flags
```text
--write-thumbnail
--convert-thumbnails jpg
--embed-thumbnail
```
yt-dlp uses `ffmpeg` to automatically transcode the downloaded WebP thumbnail into a standard baseline JPEG and write it into the ID3 APIC frame.

---

## 3. Metadata Cleansing Engine (`IMetadataCleaner`)

YouTube track titles often contain YouTube-specific clutter (e.g., `Queen - Bohemian Rhapsody (Official Video) [Remastered 2011]`).

### 3.1 Cleansing Filter Rules
```csharp
public static class TitleCleaner
{
    private static readonly Regex[] ClutterPatterns = new[]
    {
        new Regex(@"\s*[\(\[]\s*official\s*(?:music\s*)?video\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*official\s*audio\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*lyric\s*video\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*4k\s*(?:remaster|uhd)?\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*hd\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*audio\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*visualizer\s*[\)\]]", RegexOptions.IgnoreCase),
        new Regex(@"\s*[\(\[]\s*remaster(?:ed)?(?:\s+\d{4})?\s*[\)\]]", RegexOptions.IgnoreCase)
    };

    public static string CleanTrackTitle(string title)
    {
        string cleaned = title;
        foreach (var pattern in ClutterPatterns)
        {
            cleaned = pattern.Replace(cleaned, "");
        }
        return cleaned.Trim();
    }
}
```

---

## 4. Directory & File Naming Templates

The user can choose from standard presets or define a custom naming pattern:

| Preset Name | Template Pattern | Example Result |
|---|---|---|
| **Flat** | `%(title)s.%(ext)s` | `Bohemian Rhapsody.mp3` |
| **Artist - Title** | `%(artist,uploader)s - %(title)s.%(ext)s` | `Queen - Bohemian Rhapsody.mp3` |
| **Organized by Artist** | `%(artist,uploader)s/%(album,title)s/%(playlist_index&{:02d} - \|)s%(title)s.%(ext)s` | `Queen/A Night at the Opera/04 - Bohemian Rhapsody.mp3` |

Safe Windows filename sanitization is enforced by yt-dlp using `--windows-filenames` to strip invalid characters (`< > : " / \ | ? *`).

---

## 5. Diagnose and Verify MP3 Download Output

The user reports that all downloads fail although search works. Treat download success as a release gate and trace errors across source resolution, stream selection, transfer, FFmpeg conversion, metadata/thumbnail post-processing, and final output.

**Requirements**:
- Preserve yt-dlp's failure exit code and useful stderr/postprocessor diagnostics through `AudioDownloadService` to the queue item; do not replace a concrete tool error with a generic failure status.
- Verify the destination exists, is writable, and receives the expected output; a reported success requires a non-empty MP3, not just a zero exit code.
- Test the configured `--ffmpeg-location`, MP3 preset arguments, thumbnail conversion/embedding, metadata flags, and filename template against the provisioned binaries.
- Keep public downloads functional without authentication. Do not include secrets or cookie contents in diagnostics.
- Add unit/process regression tests for the discovered failure and an opt-in real Windows E2E check using only user-authorized media.

**Acceptance criteria**: A selected authorized public track downloads as a non-empty MP3 without login; conversion or output failures identify the failing stage and present sanitized actionable diagnostics.
