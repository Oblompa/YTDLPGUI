using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public class YtDlpProcessRunner : IYtDlpRunner
{
    private readonly IDependencyManager _dependencyManager;
    private readonly ILogger<YtDlpProcessRunner> _logger;

    public YtDlpProcessRunner(
        IDependencyManager dependencyManager,
        ILogger<YtDlpProcessRunner>? logger = null)
    {
        _dependencyManager = dependencyManager;
        _logger = logger ?? NullLogger<YtDlpProcessRunner>.Instance;
    }

    public async Task<YtDlpResult> ExecuteAsync(
        string arguments,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyManager.YtDlpPath))
        {
            return new YtDlpResult(
                Success: false,
                ExitCode: -1,
                Output: string.Empty,
                Error: $"yt-dlp binary not found at '{_dependencyManager.YtDlpPath}'. Please run dependency provisioning."
            );
        }

        var psi = new ProcessStartInfo
        {
            FileName = _dependencyManager.YtDlpPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        Task outputReadTask = Task.CompletedTask;
        Task errorReadTask = Task.CompletedTask;
        var stopwatch = Stopwatch.StartNew();
        using var process = new System.Diagnostics.Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The yt-dlp process could not be started.");
            }

            outputReadTask = CaptureOutputAsync(process.StandardOutput, outputBuilder, progress);
            errorReadTask = CaptureOutputAsync(process.StandardError, errorBuilder, progress);

            using var cancellationRegistration = ct.Register(() =>
            {
                TryKillProcessTree(process);
            });

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await Task.WhenAll(outputReadTask, errorReadTask);
                return new YtDlpResult(
                    Success: false,
                    ExitCode: -2,
                    Output: outputBuilder.ToString(),
                    Error: "Process cancelled by user."
                );
            }

            await Task.WhenAll(outputReadTask, errorReadTask);

            bool success = process.ExitCode == 0;
            stopwatch.Stop();
            string output = outputBuilder.ToString();
            string error = errorBuilder.ToString();

            if (!success)
            {
                string diagnostic = GetFailureDiagnostic(process.ExitCode, error, output);
                _logger.LogError(
                    "yt-dlp failed with exit code {ExitCode} after {ElapsedMilliseconds} ms. Diagnostic: {Diagnostic}",
                    process.ExitCode,
                    stopwatch.ElapsedMilliseconds,
                    SanitizeDiagnostic(diagnostic));

                return new YtDlpResult(
                    Success: false,
                    ExitCode: process.ExitCode,
                    Output: output,
                    Error: diagnostic
                );
            }

            _logger.LogInformation("yt-dlp completed successfully in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return new YtDlpResult(
                Success: true,
                ExitCode: process.ExitCode,
                Output: output,
                Error: null
            );
        }
        catch (OperationCanceledException)
        {
            return new YtDlpResult(
                Success: false,
                ExitCode: -2,
                Output: outputBuilder.ToString(),
                Error: "Process cancelled by user."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute yt-dlp.");
            return new YtDlpResult(
                Success: false,
                ExitCode: -3,
                Output: outputBuilder.ToString(),
                Error: $"Failed to execute yt-dlp: {ex.Message}"
            );
        }
    }

    private static async Task CaptureOutputAsync(
        StreamReader reader,
        StringBuilder destination,
        IProgress<DownloadProgressUpdate>? progress)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            destination.AppendLine(line);
            if (RegexPatterns.TryParseProgressLine(line, "active_task", out var update) && update is not null)
            {
                progress?.Report(update);
            }
        }
    }

    private static void TryKillProcessTree(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static string GetFailureDiagnostic(int exitCode, string standardError, string standardOutput)
    {
        string diagnostic = string.Join(
            Environment.NewLine,
            new[] { standardError, standardOutput }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text.Trim()));

        return string.IsNullOrWhiteSpace(diagnostic)
            ? $"yt-dlp exited with code {exitCode} and did not return diagnostic output."
            : $"yt-dlp exited with code {exitCode}.{Environment.NewLine}{diagnostic}";
    }

    private static string SanitizeDiagnostic(string diagnostic)
    {
        string sanitized = Regex.Replace(diagnostic, @"https?://[^\s""'<>]+", "[URL]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            @"(?im)\b(authorization|proxy-authorization|cookie|cookies|set-cookie)\b\s*[:=][^\r\n]*",
            "$1=[REDACTED]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)\b(access[_-]?token|refresh[_-]?token|token|sapisid|apisid|sid|hsid|ssid|login_info|visitor_info1_live|ysc|__secure-[\w-]+)\b\s*=\s*[^\s,;""}]+",
            "$1=[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"(?i)\b[A-Z]:\\[^\r\n]+", "[PATH]");
        return sanitized;
    }

    public async Task<JsonElement> QueryMetadataAsync(
        string url,
        string? cookiesFilePath = null,
        bool isFlatPlaylist = true,
        CancellationToken ct = default,
        int? playlistEnd = null)
    {
        var builder = new YtDlpArgumentBuilder()
            .WithDumpJson()
            .WithNoWarnings()
            .WithIgnoreErrors()
            .WithFfmpegLocation(_dependencyManager.BinDirectory)
            .WithJavaScriptRuntime(_dependencyManager.DenoPath)
            .WithCookies(cookiesFilePath);

        if (playlistEnd.HasValue)
        {
            builder.WithPlaylistEnd(playlistEnd.Value);
        }

        if (isFlatPlaylist)
        {
            builder.WithFlatPlaylist();
        }

        string args = builder.Build(url);
        var result = await ExecuteAsync(args, null, ct);

        if (string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException(
                $"Failed to query metadata for '{url}': {result.Error ?? "Empty output"}");
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Output.Trim());
            if (!result.Success)
            {
                _logger.LogWarning(
                    "yt-dlp returned exit code {ExitCode} with partial playlist metadata.",
                    result.ExitCode);
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to query metadata for '{url}': yt-dlp returned invalid JSON. {result.Error}",
                ex);
        }
    }
}
