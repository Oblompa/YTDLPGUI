using System.Diagnostics;
using System.Text;
using System.Text.Json;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public class YtDlpProcessRunner : IYtDlpRunner
{
    private readonly IDependencyManager _dependencyManager;

    public YtDlpProcessRunner(IDependencyManager dependencyManager)
    {
        _dependencyManager = dependencyManager;
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

        using var process = new System.Diagnostics.Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            outputBuilder.AppendLine(e.Data);

            if (RegexPatterns.TryParseProgressLine(e.Data, "active_task", out var update) && update is not null)
            {
                progress?.Report(update);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            errorBuilder.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await using (ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { /* ignore cleanup race conditions */ }
            }))
            {
                await process.WaitForExitAsync(ct);
            }

            bool success = process.ExitCode == 0;
            return new YtDlpResult(
                Success: success,
                ExitCode: process.ExitCode,
                Output: outputBuilder.ToString(),
                Error: success ? null : errorBuilder.ToString()
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
            return new YtDlpResult(
                Success: false,
                ExitCode: -3,
                Output: outputBuilder.ToString(),
                Error: $"Failed to execute yt-dlp: {ex.Message}"
            );
        }
    }

    public async Task<JsonElement> QueryMetadataAsync(
        string url,
        string? cookiesFilePath = null,
        bool isFlatPlaylist = true,
        CancellationToken ct = default)
    {
        var builder = new YtDlpArgumentBuilder()
            .WithDumpJson()
            .WithNoWarnings()
            .WithFfmpegLocation(_dependencyManager.BinDirectory)
            .WithCookies(cookiesFilePath);

        if (isFlatPlaylist)
        {
            builder.WithFlatPlaylist();
        }

        string args = builder.Build(url);
        var result = await ExecuteAsync(args, null, ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException($"Failed to query metadata for '{url}': {result.Error ?? "Empty output"}");
        }

        using var doc = JsonDocument.Parse(result.Output.Trim());
        return doc.RootElement.Clone();
    }
}
