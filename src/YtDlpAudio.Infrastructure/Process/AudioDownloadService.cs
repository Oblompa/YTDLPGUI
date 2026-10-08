using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public class AudioDownloadService
{
    private readonly IYtDlpRunner _runner;
    private readonly IDependencyManager _dependencyManager;
    private readonly IAuthManager? _authManager;

    public AudioDownloadService(
        IYtDlpRunner runner,
        IDependencyManager dependencyManager,
        IAuthManager? authManager = null)
    {
        _runner = runner;
        _dependencyManager = dependencyManager;
        _authManager = authManager;
    }

    public async Task<YtDlpResult> DownloadAudioAsync(
        string targetUrl,
        AudioPipelineConfig config,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(config.OutputDirectory);

        string outputTemplate = Path.Combine(config.OutputDirectory, config.FilenameTemplate);

        string? tempCookiesPath = null;
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

        try
        {
            var builder = new YtDlpArgumentBuilder()
                .WithFfmpegLocation(_dependencyManager.BinDirectory)
                .WithJavaScriptRuntime(_dependencyManager.DenoPath)
                .WithCookies(tempCookiesPath)
                .WithAudioOnly(config.AudioFormat, config.QualityPreset)
                .WithStreamSelection(preferPremium: isPremium)
                .WithOutputTemplate(outputTemplate)
                .WithEmbeddings(config.EmbedMetadata, config.EmbedAlbumArt)
                .WithPrintAfterMoveFilepath();

            string commandArgs = builder.Build(targetUrl);
            var result = await _runner.ExecuteAsync(commandArgs, progress, ct);

            if (result.Success)
            {
                return VerifyOutputFile(result, config);
            }

            if (!result.Success && ct.IsCancellationRequested)
            {
                CleanupPartialFiles(config.OutputDirectory);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            CleanupPartialFiles(config.OutputDirectory);
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

    private static YtDlpResult VerifyOutputFile(YtDlpResult result, AudioPipelineConfig config)
    {
        string? outputPath = result.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Trim('"'))
            .LastOrDefault(path => File.Exists(path));

        if (outputPath is null)
        {
            return result with
            {
                Success = false,
                Error = $"yt-dlp exited successfully but did not report an output file. Expected a non-empty {config.AudioFormat.ToUpperInvariant()} file in '{config.OutputDirectory}'."
            };
        }

        var outputFile = new FileInfo(outputPath);
        string expectedExtension = $".{config.AudioFormat}";
        if (!outputFile.Extension.Equals(expectedExtension, StringComparison.OrdinalIgnoreCase) ||
            outputFile.Length == 0)
        {
            return result with
            {
                Success = false,
                Error = $"yt-dlp reported output '{outputPath}', but it is not a non-empty {config.AudioFormat.ToUpperInvariant()} file."
            };
        }

        return result;
    }

    private static void CleanupPartialFiles(string outputDirectory)
    {
        try
        {
            if (!Directory.Exists(outputDirectory)) return;

            var dirInfo = new DirectoryInfo(outputDirectory);
            var partialFiles = dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly)
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
