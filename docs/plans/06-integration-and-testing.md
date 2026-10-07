# Specialist Plan 06: Test Suite, Mocks & Integration

## Objective
Provide a robust automated testing strategy that isolates the business logic from physical CLI process executions, tests regular expression parsers against authentic stdout streams, verifies DPAPI cookie roundtrips, and provides integration harnesses.

---

## 1. Test Architecture

```
tests/YtDlpAudio.Core.Tests/
├── RegexTests/
│   ├── DownloadProgressRegexTests.cs   <-- Tests yt-dlp stdout matching
│   └── TitleCleanerTests.cs            <-- Tests noise stripping from track titles
├── AuthTests/
│   ├── NetscapeCookieFormatterTests.cs <-- Validates tab-separated cookie export
│   └── DpapiStorageTests.cs            <-- Tests round-trip encryption/decryption
├── ProcessTests/
│   ├── ArgumentBuilderTests.cs         <-- Ensures CLI parameters are safely escaped
│   └── MockProcessRunnerTests.cs       <-- Simulates stdout line streaming
└── Fixtures/
    ├── SampleYtDlpStdout.txt
    └── SampleFlatPlaylist.json
```

---

## 2. Sample Unit Tests

### 2.1 Stdout Regex Progress Verification
```csharp
[Theory]
[InlineData("[download]  45.2% of ~120.50MiB at 12.4MiB/s ETA 00:05", 45.2, "120.50MiB", "12.4MiB/s", "00:05")]
[InlineData("[download]   2.0% of 45.00MiB at 1.2MiB/s ETA 01:23", 2.0, "45.00MiB", "1.2MiB/s", "01:23")]
[InlineData("[download] 100% of 10.00MiB at 5.0MiB/s ETA 00:00", 100.0, "10.00MiB", "5.0MiB/s", "00:00")]
public void ParseDownloadProgress_ValidLines_ExtractsCorrectMetrics(
    string line, double expectedPercent, string expectedSize, string expectedSpeed, string expectedEta)
{
    var match = RegexPatterns.DownloadProgressRegex.Match(line);
    Assert.True(match.Success);
    Assert.Equal(expectedPercent, double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    Assert.Equal(expectedSize, match.Groups[2].Value);
    Assert.Equal(expectedSpeed, match.Groups[3].Value);
    Assert.Equal(expectedEta, match.Groups[4].Value);
}
```

### 2.2 Title Cleansing Verification
```csharp
[Theory]
[InlineData("Queen - Bohemian Rhapsody (Official Video)", "Queen - Bohemian Rhapsody")]
[InlineData("Daft Punk - Get Lucky (Official Audio)", "Daft Punk - Get Lucky")]
[InlineData("Nirvana - Smells Like Teen Spirit [Remastered 2021]", "Nirvana - Smells Like Teen Spirit")]
[InlineData("The Weeknd - Blinding Lights (Lyric Video) [4K UHD]", "The Weeknd - Blinding Lights")]
public void CleanTrackTitle_RemovesYouTubeNoise(string input, string expected)
{
    var actual = TitleCleaner.CleanTrackTitle(input);
    Assert.Equal(expected, actual);
}
```

---

## 3. Mock Process Runner for UI & Integration Testing

Instead of running real 100MB downloads during automated testing, a `MockYtDlpRunner` replays recorded stream events:

```csharp
public class MockYtDlpRunner : IYtDlpRunner
{
    public async Task<YtDlpResult> ExecuteAsync(
        string arguments,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default)
    {
        for (int p = 0; p <= 100; p += 20)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct);
            progress?.Report(new DownloadProgressUpdate(
                VideoId: "test_vid",
                Percent: p,
                DownloadSpeed: "15.0MiB/s",
                Eta: "00:02",
                TotalSize: "8.5MiB",
                State: p == 100 ? DownloadState.Completed : DownloadState.Downloading
            ));
        }
        return new YtDlpResult(Success: true, ExitCode: 0, Output: "Done", Error: null);
    }
}
```
