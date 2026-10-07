using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Dependencies;

public class DependencyManager : IDependencyManager
{
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string YtDlpChecksumUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";
    private const string FFmpegDownloadUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    private readonly HttpClient _httpClient;

    public string BinDirectory { get; }
    public string YtDlpPath => Path.Combine(BinDirectory, "yt-dlp.exe");
    public string FFmpegPath => Path.Combine(BinDirectory, "ffmpeg.exe");
    public string FFprobePath => Path.Combine(BinDirectory, "ffprobe.exe");

    public DependencyManager(string? customBinDir = null, HttpClient? httpClient = null)
    {
        BinDirectory = customBinDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpAudio",
            "bin"
        );
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default)
    {
        bool ytDlpExists = File.Exists(YtDlpPath);
        bool ffmpegExists = File.Exists(FFmpegPath);

        string? ytDlpVersion = null;
        if (ytDlpExists)
        {
            ytDlpVersion = await GetYtDlpVersionAsync(ct);
            if (string.IsNullOrWhiteSpace(ytDlpVersion))
            {
                ytDlpExists = false;
            }
        }

        string? ffmpegVersion = null;
        if (ffmpegExists)
        {
            ffmpegVersion = await GetFFmpegVersionAsync(ct);
            if (string.IsNullOrWhiteSpace(ffmpegVersion))
            {
                ffmpegExists = false;
            }
        }

        return new DependencyStatus(ytDlpExists, ytDlpVersion, ffmpegExists, ffmpegVersion);
    }

    public async Task<bool> ProvisionAllAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(BinDirectory);

        // 1. Download yt-dlp
        progress?.Report(new ProvisioningProgress("Downloading yt-dlp...", 0, 0, null));
        await DownloadYtDlpInternalAsync(progress, ct);

        // 2. Download & Extract FFmpeg
        progress?.Report(new ProvisioningProgress("Downloading FFmpeg package...", 50, 0, null));
        await DownloadAndExtractFFmpegInternalAsync(progress, ct);

        progress?.Report(new ProvisioningProgress("Verifying installations...", 95, 0, null));
        var status = await CheckStatusAsync(ct);
        progress?.Report(new ProvisioningProgress(status.AllReady ? "Ready" : "Verification Failed", 100, 0, null));

        return status.AllReady;
    }

    public async Task<bool> UpdateYtDlpAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(BinDirectory);
        progress?.Report(new ProvisioningProgress("Checking yt-dlp updates...", 0, 0, null));
        await DownloadYtDlpInternalAsync(progress, ct);
        string? version = await GetYtDlpVersionAsync(ct);
        return !string.IsNullOrWhiteSpace(version);
    }

    public async Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default)
    {
        if (!File.Exists(YtDlpPath)) return null;
        return await ExecuteVersionCommandAsync(YtDlpPath, "--version", ct);
    }

    public async Task<string?> GetFFmpegVersionAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FFmpegPath)) return null;
        return await ExecuteVersionCommandAsync(FFmpegPath, "-version", ct);
    }

    private async Task DownloadYtDlpInternalAsync(IProgress<ProvisioningProgress>? progress, CancellationToken ct)
    {
        string tempYtDlp = Path.Combine(BinDirectory, "yt-dlp.exe.tmp");

        try
        {
            using (var response = await _httpClient.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempYtDlp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    double percent = totalBytes.HasValue && totalBytes.Value > 0
                        ? (double)totalRead / totalBytes.Value * 50.0
                        : 25.0;

                    progress?.Report(new ProvisioningProgress("Downloading yt-dlp.exe", percent, totalRead, totalBytes));
                }
            }

            // Atomic replacement
            if (File.Exists(YtDlpPath))
            {
                File.Delete(YtDlpPath);
            }
            File.Move(tempYtDlp, YtDlpPath, true);
        }
        finally
        {
            if (File.Exists(tempYtDlp))
            {
                try { File.Delete(tempYtDlp); } catch { /* best effort cleanup */ }
            }
        }
    }

    private async Task DownloadAndExtractFFmpegInternalAsync(IProgress<ProvisioningProgress>? progress, CancellationToken ct)
    {
        string tempZip = Path.Combine(BinDirectory, "ffmpeg-download.zip.tmp");

        try
        {
            using (var response = await _httpClient.GetAsync(FFmpegDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    double percent = totalBytes.HasValue && totalBytes.Value > 0
                        ? 50.0 + ((double)totalRead / totalBytes.Value * 40.0)
                        : 70.0;

                    progress?.Report(new ProvisioningProgress("Downloading FFmpeg archive", percent, totalRead, totalBytes));
                }
            }

            progress?.Report(new ProvisioningProgress("Extracting ffmpeg.exe and ffprobe.exe...", 90, 0, null));

            using (var archive = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(FFmpegPath, overwrite: true);
                    }
                    else if (entry.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(FFprobePath, overwrite: true);
                    }
                }
            }
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                try { File.Delete(tempZip); } catch { /* best effort cleanup */ }
            }
        }
    }

    private static async Task<string?> ExecuteVersionCommandAsync(string executablePath, string argument, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = argument,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                string firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                return firstLine.Trim();
            }
        }
        catch
        {
            // Process launch failed
        }

        return null;
    }
}
