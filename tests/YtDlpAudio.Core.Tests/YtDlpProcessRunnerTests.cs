using Xunit;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;
using YtDlpAudio.Infrastructure.Process;

namespace YtDlpAudio.Core.Tests;

public class YtDlpProcessRunnerTests
{
    private sealed class CommandDependencyManager(string executablePath) : IDependencyManager
    {
        public string BinDirectory => Path.GetDirectoryName(executablePath)!;
        public string YtDlpPath => executablePath;
        public string FFmpegPath => Path.Combine(BinDirectory, "ffmpeg.exe");
        public string FFprobePath => Path.Combine(BinDirectory, "ffprobe.exe");
        public string DenoPath => Path.Combine(BinDirectory, "deno.exe");

        public Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new DependencyStatus(true, "test", true, "test", true, "deno 2.9.7"));

        public Task<bool> ProvisionAllAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> UpdateYtDlpAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> EnsureDenoAsync(
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default) =>
            Task.FromResult<string?>("test");
    }

    [Fact]
    public async Task ExecuteAsync_CapturesOutputAndErrorBeforeReturningExitCode()
    {
        string commandProcessor = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        var runner = new YtDlpProcessRunner(new CommandDependencyManager(commandProcessor));

        var result = await runner.ExecuteAsync(
            "/c echo standard-output&echo ERROR: simulated failure 1>&2&exit /b 7");

        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("standard-output", result.Output);
        Assert.Contains("ERROR: simulated failure", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsTransferAndPostProcessingTimings()
    {
        string commandProcessor = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        var runner = new YtDlpProcessRunner(new CommandDependencyManager(commandProcessor));

        var result = await runner.ExecuteAsync(
            "/c echo [download] 50% of 10MiB at 1MiB/s ETA 00:05&echo [download] 100% of 10MiB&echo [ExtractAudio] Destination: simulated.mp3");

        Assert.True(result.Success);
        Assert.NotNull(result.Timing);
        Assert.True(result.Timing.Process >= TimeSpan.Zero);
        Assert.NotNull(result.Timing.Transfer);
        Assert.NotNull(result.Timing.PostProcessing);
    }
}
