using System.Text.Json;
using System.Net;
using System.Text;
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
        public string DenoPath => @"C:\Fake\bin\deno.exe";

        public Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new DependencyStatus(true, "2026.01.01", true, "7.0", true, "deno 2.9.7"));

        public Task<bool> ProvisionAllAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> UpdateYtDlpAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> EnsureDenoAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>("2026.01.01");
    }

    private sealed class FakeAuthManager(AuthStatus status) : IAuthManager
    {
        public string? ExportedCookiesPath { get; private set; }
        public string CookieContents { get; set; } = "temporary test cookie";

        public Task<AuthStatus> GetCurrentAuthStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(status);

        public async Task<string> ExportCookiesToTempFileAsync(CancellationToken ct = default)
        {
            ExportedCookiesPath = Path.Combine(Path.GetTempPath(), $"ytdlp_auth_test_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(ExportedCookiesPath, CookieContents, ct);
            return ExportedCookiesPath;
        }

        public Task<bool> ValidatePremiumStatusAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task ClearSessionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public string GetWebView2UserDataFolder() => Path.GetTempPath();
    }

    private sealed class FakeYouTubeMusicPlaylistClient(
        Func<string, CancellationToken, Task<IReadOnlyList<SearchResultItem>>> handler)
        : IYouTubeMusicPlaylistClient
    {
        public Task<IReadOnlyList<SearchResultItem>> GetLibraryPlaylistsAsync(
            string netscapeCookies,
            CancellationToken ct = default) =>
            handler(netscapeCookies, ct);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    [Fact]
    public async Task BrowseAccountPlaylists_UsesAuthenticatedLibraryAndCleansTemporaryCookies()
    {
        var authManager = new FakeAuthManager(new AuthStatus(true, false, "Test account", DateTimeOffset.UtcNow));
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (url, cookiesPath, isFlatPlaylist, _) =>
            {
                Assert.Equal("https://www.youtube.com/feed/playlists", url);
                Assert.True(isFlatPlaylist);
                Assert.NotNull(cookiesPath);
                Assert.True(File.Exists(cookiesPath));

                using var doc = JsonDocument.Parse("""
                {
                    "entries": [
                        {
                            "id": "PL_test_private",
                            "title": "Private test playlist",
                            "uploader": "Test account",
                            "playlist_count": 4,
                            "url": "https://www.youtube.com/playlist?list=PL_test_private"
                        }
                    ]
                }
                """);
                return Task.FromResult(doc.RootElement.Clone());
            }
        };
        var searchService = new SearchService(runner, authManager);

        var playlists = await searchService.BrowseAccountPlaylistsAsync();

        var playlist = Assert.Single(playlists);
        Assert.Equal("PL_test_private", playlist.Id);
        Assert.Equal("Private test playlist", playlist.Title);
        Assert.Equal("https://www.youtube.com/playlist?list=PL_test_private", playlist.Url);
        Assert.Equal(SearchResultType.Playlist, playlist.ResultType);
        Assert.Equal("YouTube", playlist.Source);
        Assert.NotNull(authManager.ExportedCookiesPath);
        Assert.False(File.Exists(authManager.ExportedCookiesPath));
    }

    [Fact]
    public async Task BrowseYouTubeMusicPlaylists_LoadsMusicLibraryWithoutQueryingYouTubeFeed()
    {
        var authManager = new FakeAuthManager(new AuthStatus(true, false, "Test account", DateTimeOffset.UtcNow));
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) =>
                throw new InvalidOperationException("YouTube Music browsing should not use yt-dlp.")
        };
        const string cookies = "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t1999999999\t__Secure-3PAPISID\ttest-sapisid";
        authManager.CookieContents = cookies;
        var musicPlaylist = new SearchResultItem(
            "PL_music",
            "Music playlist",
            "YouTube Music",
            null,
            7,
            null,
            "https://www.youtube.com/playlist?list=PL_music",
            SearchResultType.Playlist);
        string? exportedCookies = null;
        var musicClient = new FakeYouTubeMusicPlaylistClient((cookieContent, _) =>
        {
            exportedCookies = cookieContent;
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(new[] { musicPlaylist });
        });
        var searchService = new SearchService(runner, authManager, musicClient);

        var playlists = await searchService.BrowseYouTubeMusicPlaylistsAsync();

        var playlist = Assert.Single(playlists);
        Assert.Equal("PL_music", playlist.Id);
        Assert.Equal("YouTube Music", playlist.Source);
        Assert.Equal(cookies, exportedCookies);
        Assert.NotNull(authManager.ExportedCookiesPath);
        Assert.False(File.Exists(authManager.ExportedCookiesPath));
    }

    [Fact]
    public async Task BrowseYouTubeMusicPlaylists_ReportsFailureAndCleansTemporaryCookies()
    {
        var authManager = new FakeAuthManager(new AuthStatus(true, false, "Test account", DateTimeOffset.UtcNow));
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) =>
                throw new InvalidOperationException("YouTube Music browsing should not use yt-dlp.")
        };
        var musicClient = new FakeYouTubeMusicPlaylistClient((_, _) =>
            Task.FromException<IReadOnlyList<SearchResultItem>>(
                new InvalidOperationException("Music API is temporarily unavailable.")));
        var searchService = new SearchService(runner, authManager, musicClient);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => searchService.BrowseYouTubeMusicPlaylistsAsync());

        Assert.Contains("YouTube Music playlist library", error.Message);
        Assert.Contains("temporarily unavailable", error.Message);
        Assert.NotNull(authManager.ExportedCookiesPath);
        Assert.False(File.Exists(authManager.ExportedCookiesPath));
    }

    [Fact]
    public async Task YouTubeMusicPlaylistClient_AuthenticatesAndParsesLibraryPlaylists()
    {
        const string cookies = """
            # Netscape HTTP Cookie File
            .youtube.com	TRUE	/	TRUE	1999999999	__Secure-3PAPISID	test-sapisid
            .youtube.com	TRUE	/	TRUE	1999999999	LOGIN_INFO	test-login
            .google.com	TRUE	/	TRUE	1999999999	OTHER_COOKIE	not-for-youtube
            """;
        const string responseJson = """
            {
              "contents": {
                "twoColumnBrowseResultsRenderer": {
                  "secondaryContents": {
                    "sectionListRenderer": {
                      "contents": [
                        {
                          "gridRenderer": {
                            "items": [
                              {
                                "musicTwoRowItemRenderer": {
                                  "title": {
                                    "runs": [{
                                      "text": "Saved Music Playlist",
                                      "navigationEndpoint": {
                                        "browseEndpoint": { "browseId": "VLPL_music_saved" }
                                      }
                                    }]
                                  },
                                  "subtitle": {
                                    "runs": [
                                      { "text": "Playlist" },
                                      { "text": " · " },
                                      { "text": "7 songs" }
                                    ]
                                  },
                                  "thumbnailRenderer": {
                                    "musicThumbnailRenderer": {
                                      "thumbnail": {
                                        "thumbnails": [
                                          { "url": "https://example.test/cover.jpg" }
                                        ]
                                      }
                                    }
                                  }
                                }
                              }
                            ],
                            "continuations": [
                              { "nextContinuationData": { "continuation": "next-token" } }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
        const string continuationJson = """
            {
              "continuationContents": {
                "gridContinuation": {
                  "items": [
                    {
                      "musicTwoRowItemRenderer": {
                        "title": {
                          "runs": [{
                            "text": "Later Music Playlist",
                            "navigationEndpoint": {
                              "browseEndpoint": { "browseId": "VLPL_music_later" }
                            }
                          }]
                        },
                        "subtitle": { "runs": [{ "text": "2 tracks" }] }
                      }
                    }
                  ]
                }
              }
            }
            """;
        var capturedRequests = new List<(HttpMethod Method, Uri? Uri, string? Authorization, string? Cookie, string? Body)>();
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            capturedRequests.Add((
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.ToString() ??
                    (request.Headers.TryGetValues("Authorization", out var authValues) ? authValues.Single() : null),
                request.Headers.TryGetValues("Cookie", out var cookieValues) ? cookieValues.Single() : null,
                body));

            string content = request.Method == HttpMethod.Get
                ? """<script>ytcfg.set({"INNERTUBE_API_KEY":"unit-test-api-key","VISITOR_DATA":"unit-test-visitor"});</script>"""
                : request.RequestUri!.Query.Contains("ctoken=", StringComparison.Ordinal)
                    ? continuationJson
                    : responseJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        });
        var client = new YouTubeMusicPlaylistClient(() => handler);

        var playlists = await client.GetLibraryPlaylistsAsync(cookies);

        Assert.Equal(2, playlists.Count);
        var playlist = playlists.Single(item => item.Id == "PL_music_saved");
        Assert.Equal("PL_music_saved", playlist.Id);
        Assert.Equal("Saved Music Playlist", playlist.Title);
        Assert.Equal(7, playlist.TrackCount);
        Assert.Equal("YouTube Music", playlist.Source);
        Assert.Equal("https://www.youtube.com/playlist?list=PL_music_saved", playlist.Url);
        Assert.Equal("https://example.test/cover.jpg", playlist.ThumbnailUrl);
        Assert.Equal(2, playlists.Single(item => item.Id == "PL_music_later").TrackCount);

        Assert.Equal(3, capturedRequests.Count);
        Assert.Equal(HttpMethod.Get, capturedRequests[0].Method);
        Assert.Equal(HttpMethod.Post, capturedRequests[1].Method);
        Assert.Contains("key=unit-test-api-key", capturedRequests[1].Uri!.Query);
        Assert.Contains("ctoken=next-token", capturedRequests[2].Uri!.Query);
        Assert.Contains("continuation=next-token", capturedRequests[2].Uri!.Query);
        Assert.StartsWith("SAPISIDHASH ", capturedRequests[1].Authorization);
        Assert.Contains("__Secure-3PAPISID=test-sapisid", capturedRequests[1].Cookie);
        Assert.DoesNotContain("not-for-youtube", capturedRequests[1].Cookie);
        using var requestBody = JsonDocument.Parse(capturedRequests[1].Body!);
        Assert.Equal(
            "FEmusic_liked_playlists",
            requestBody.RootElement.GetProperty("browseId").GetString());
        Assert.Equal(
            "WEB_REMIX",
            requestBody.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientName").GetString());
    }

    [Fact]
    public async Task BrowseAccountPlaylists_RejectsGuestWithoutQueryingRunner()
    {
        bool queryCalled = false;
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) =>
            {
                queryCalled = true;
                using var doc = JsonDocument.Parse("""{"entries":[]}""");
                return Task.FromResult(doc.RootElement.Clone());
            }
        };
        var searchService = new SearchService(
            runner,
            new FakeAuthManager(new AuthStatus(false, false, null, DateTimeOffset.UtcNow)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => searchService.BrowseAccountPlaylistsAsync());

        Assert.Contains("Sign in", error.Message);
        Assert.False(queryCalled);
    }

    [Fact]
    public async Task BrowseAccountPlaylists_ReportsAuthenticationFailureAndCleansCookies()
    {
        var authManager = new FakeAuthManager(new AuthStatus(true, false, "Test account", DateTimeOffset.UtcNow));
        var runner = new MockYtDlpRunner
        {
            QueryMetadataHandler = (_, _, _, _) => Task.FromException<JsonElement>(
                new InvalidOperationException("ERROR: HTTP Error 401: Unauthorized"))
        };
        var searchService = new SearchService(runner, authManager);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => searchService.BrowseAccountPlaylistsAsync());

        Assert.Contains("session may have expired", error.Message);
        Assert.Contains("401", error.Message);
        Assert.NotNull(authManager.ExportedCookiesPath);
        Assert.False(File.Exists(authManager.ExportedCookiesPath));
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
            string outputPath = Assert.Single(
                downloadResult.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
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
    public async Task DownloadReturnsFailureWhenYtDlpExitsWithError()
    {
        const string expectedError = "ERROR: unable to download media";
        var mockRunner = new MockYtDlpRunner
        {
            ExecuteHandler = (_, _, _) => Task.FromResult(new YtDlpResult(
                Success: false,
                ExitCode: 1,
                Output: string.Empty,
                Error: $"yt-dlp exited with code 1.{Environment.NewLine}{expectedError}"))
        };
        var downloadService = new AudioDownloadService(
            mockRunner,
            new FakeDependencyManager(),
            authManager: null);
        string testOutput = Path.Combine(Path.GetTempPath(), $"ytdlp_failure_{Guid.NewGuid():N}");

        try
        {
            var config = new AudioPipelineConfig(OutputDirectory: testOutput);
            var result = await downloadService.DownloadAudioAsync(
                "https://example.com/authorized-audio",
                config);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(expectedError, result.Error);
        }
        finally
        {
            if (Directory.Exists(testOutput))
            {
                Directory.Delete(testOutput, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadRejectsSuccessfulProcessWithoutReportedMp3()
    {
        var mockRunner = new MockYtDlpRunner
        {
            ExecuteHandler = (_, _, _) => Task.FromResult(new YtDlpResult(
                Success: true,
                ExitCode: 0,
                Output: string.Empty,
                Error: null))
        };
        var downloadService = new AudioDownloadService(
            mockRunner,
            new FakeDependencyManager(),
            authManager: null);
        string testOutput = Path.Combine(Path.GetTempPath(), $"ytdlp_missing_output_{Guid.NewGuid():N}");

        try
        {
            var config = new AudioPipelineConfig(OutputDirectory: testOutput);
            var result = await downloadService.DownloadAudioAsync(
                "https://example.com/authorized-audio",
                config);

            Assert.False(result.Success);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("did not report an output file", result.Error);
        }
        finally
        {
            if (Directory.Exists(testOutput))
            {
                Directory.Delete(testOutput, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PlaylistSearch_UsesYouTubePlaylistFilterAndLimitsResults()
    {
        var mockRunner = new MockYtDlpRunner();
        mockRunner.QueryMetadataHandler = (url, _, _, _) =>
        {
            Assert.StartsWith("https://www.youtube.com/results?", url);
            Assert.Contains("search_query=daft%20punk", url);
            Assert.Contains("sp=EgIQAw%3D%3D", url);
            Assert.DoesNotContain("ytsearchplaylist", url);

            using var doc = JsonDocument.Parse("""
            {
                "entries": [
                    {
                        "id": "PL123",
                        "title": "Daft Punk Mix",
                        "uploader": "Music Channel",
                        "url": "https://www.youtube.com/playlist?list=PL123"
                    }
                ]
            }
            """);
            return Task.FromResult(doc.RootElement.Clone());
        };

        var searchService = new SearchService(mockRunner, authManager: null);
        var results = await searchService.SearchAsync("daft punk", SearchFilterType.Playlists, maxResults: 25);

        var result = Assert.Single(results);
        Assert.Equal("PL123", result.Id);
        Assert.Equal("Daft Punk Mix", result.Title);
        Assert.Equal("https://www.youtube.com/playlist?list=PL123", result.Url);
        Assert.Equal(SearchResultType.Playlist, result.ResultType);
        Assert.Equal(25, mockRunner.LastQueryPlaylistEnd);
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
