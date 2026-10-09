using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public class AudioDownloadService
{
    private readonly IYtDlpRunner _runner;
    private readonly IDependencyManager _dependencyManager;
    private readonly IAuthManager? _authManager;
    private readonly ILogger<AudioDownloadService> _logger;

    public AudioDownloadService(
        IYtDlpRunner runner,
        IDependencyManager dependencyManager,
        IAuthManager? authManager = null,
        ILogger<AudioDownloadService>? logger = null)
    {
        _runner = runner;
        _dependencyManager = dependencyManager;
        _authManager = authManager;
        _logger = logger ?? NullLogger<AudioDownloadService>.Instance;
    }

    public async Task<YtDlpResult> DownloadAudioAsync(
        string targetUrl,
        AudioPipelineConfig config,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default,
        string? outputSubdirectory = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var preparationStopwatch = Stopwatch.StartNew();
        string? outputDirectory = null;
        string? tempCookiesPath = null;
        try
        {
            outputDirectory = string.IsNullOrWhiteSpace(outputSubdirectory)
                ? config.OutputDirectory
                : Path.Combine(config.OutputDirectory, SanitizeDirectoryName(outputSubdirectory));
            var effectiveConfig = config with { OutputDirectory = outputDirectory };
            Directory.CreateDirectory(outputDirectory);

            bool isPremium = false;
            if (_authManager != null)
            {
                var authStatus = await _authManager.GetCurrentAuthStatusAsync(ct);
                if (authStatus.IsLoggedIn)
                {
                    tempCookiesPath = await _authManager.ExportCookiesToTempFileAsync(ct);
                    isPremium = authStatus.HasPremium;
                }
            }

            string outputTemplate = Path.Combine(outputDirectory, config.FilenameTemplate);
            var builder = new YtDlpArgumentBuilder()
                .WithFfmpegLocation(_dependencyManager.BinDirectory)
                .WithJavaScriptRuntime(_dependencyManager.DenoPath)
                .WithCookies(tempCookiesPath)
                .WithAudioOnly(config.AudioFormat, config.QualityPreset)
                .WithStreamSelection(preferPremium: isPremium)
                .WithOutputTemplate(outputTemplate)
                .WithEmbeddings(config.EmbedMetadata, config.EmbedAlbumArt)
                .WithNewlineProgress()
                .WithPrintAfterMoveFilepath();

            string commandArgs = builder.Build(targetUrl);
            var outputSearchOption = UsesNestedOutputTemplate(config.FilenameTemplate)
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            var existingOutputs = CaptureOutputSnapshot(outputDirectory, config.AudioFormat, outputSearchOption);
            preparationStopwatch.Stop();
            var result = await _runner.ExecuteAsync(commandArgs, progress, ct);

            var validationStopwatch = Stopwatch.StartNew();
            if (result.Success)
            {
                result = VerifyOutputFile(result, effectiveConfig, existingOutputs, outputSearchOption);
            }

            if (!result.Success && ct.IsCancellationRequested)
            {
                CleanupPartialFiles(outputDirectory);
            }

            validationStopwatch.Stop();
            totalStopwatch.Stop();
            var processTiming = result.Timing;
            var timing = new DownloadTiming(
                Preparation: preparationStopwatch.Elapsed,
                Process: processTiming?.Process ?? TimeSpan.Zero,
                Transfer: processTiming?.Transfer,
                PostProcessing: processTiming?.PostProcessing,
                Validation: validationStopwatch.Elapsed,
                Total: totalStopwatch.Elapsed);
            _logger.LogInformation(
                "Download stage timing: total={TotalMilliseconds} ms, preparation={PreparationMilliseconds} ms, process={ProcessMilliseconds} ms, transfer={TransferMilliseconds} ms, post-processing={PostProcessingMilliseconds} ms, validation={ValidationMilliseconds} ms.",
                timing.Total.TotalMilliseconds,
                timing.Preparation.TotalMilliseconds,
                timing.Process.TotalMilliseconds,
                timing.Transfer?.TotalMilliseconds,
                timing.PostProcessing?.TotalMilliseconds,
                timing.Validation.TotalMilliseconds);
            return result with { Timing = timing };
        }
        catch (OperationCanceledException)
        {
            if (outputDirectory is not null)
            {
                CleanupPartialFiles(outputDirectory);
            }
            throw;
        }
        finally
        {
            if (tempCookiesPath != null && File.Exists(tempCookiesPath))
            {
                try { File.Delete(tempCookiesPath); } catch { /* best effort cleanup */ }
            }
        }
    }

    private static string SanitizeDirectoryName(string name)
    {
        const int maxLength = 120;
        char[] invalidCharacters = Path.GetInvalidFileNameChars()
            .Concat("<>:\"/\\|?*".ToCharArray())
            .Distinct()
            .ToArray();
        string sanitized = new(name.Select(character =>
            char.IsControl(character) || invalidCharacters.Contains(character) ? '_' : character).ToArray());
        sanitized = sanitized.Trim().TrimEnd('.');

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
        {
            return "Unknown Artist";
        }

        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength].TrimEnd(' ', '.');
        }

        string deviceName = sanitized.Split('.')[0];
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (deviceName.Length == 4 &&
             (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             deviceName[3] is >= '1' and <= '9'))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }

    private static bool UsesNestedOutputTemplate(string filenameTemplate) =>
        filenameTemplate.Contains(Path.DirectorySeparatorChar) ||
        filenameTemplate.Contains(Path.AltDirectorySeparatorChar) ||
        filenameTemplate.Contains('/') ||
        filenameTemplate.Contains('\\');

    private static Dictionary<string, (long Length, DateTime LastWriteTimeUtc)> CaptureOutputSnapshot(
        string outputDirectory,
        string audioFormat,
        SearchOption searchOption)
    {
        var snapshot = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.EnumerateFiles(
                     outputDirectory,
                     "*",
                     searchOption)
                     .Where(path => Path.GetExtension(path).Equals(
                         $".{audioFormat}",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var file = new FileInfo(filePath);
            snapshot[file.FullName] = (file.Length, file.LastWriteTimeUtc);
        }

        return snapshot;
    }

    private static YtDlpResult VerifyOutputFile(
        YtDlpResult result,
        AudioPipelineConfig config,
        IReadOnlyDictionary<string, (long Length, DateTime LastWriteTimeUtc)> existingOutputs,
        SearchOption searchOption)
    {
        string? outputPath = result.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Trim('"'))
            .LastOrDefault(path => IsValidOutputFile(path, config.AudioFormat));

        outputPath ??= FindNewOutputFile(
            config.OutputDirectory,
            config.AudioFormat,
            existingOutputs,
            searchOption);

        if (outputPath is null)
        {
            return result with
            {
                Success = false,
                Error = $"yt-dlp exited successfully, but no new or updated non-empty {config.AudioFormat.ToUpperInvariant()} file was found in '{config.OutputDirectory}'."
            };
        }

        return result with { Output = outputPath };
    }

    private static string? FindNewOutputFile(
        string outputDirectory,
        string audioFormat,
        IReadOnlyDictionary<string, (long Length, DateTime LastWriteTimeUtc)> existingOutputs,
        SearchOption searchOption)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(outputDirectory, "*", searchOption)
            .Where(path => IsValidOutputFile(path, audioFormat))
            .Select(path => new FileInfo(path))
            .Where(file =>
                !existingOutputs.TryGetValue(file.FullName, out var prior) ||
                prior.Length != file.Length ||
                prior.LastWriteTimeUtc != file.LastWriteTimeUtc)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static bool IsValidOutputFile(string path, string audioFormat)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var file = new FileInfo(path);
        return file.Extension.Equals($".{audioFormat}", StringComparison.OrdinalIgnoreCase) &&
               file.Length > 0;
    }

    private static void CleanupPartialFiles(string outputDirectory)
    {
        try
        {
            if (!Directory.Exists(outputDirectory)) return;

            var dirInfo = new DirectoryInfo(outputDirectory);
            var partialFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                .Where(f => f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                            f.Extension.Equals(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                            f.Name.Contains(".temp.", StringComparison.OrdinalIgnoreCase));

            foreach (var file in partialFiles)
            {
                try
                {
                    file.Delete();
                }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort directory scan */ }
    }
}
