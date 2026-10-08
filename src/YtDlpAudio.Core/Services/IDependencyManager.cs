namespace YtDlpAudio.Core.Services;

public record DependencyStatus(
    bool YtDlpInstalled,
    string? YtDlpVersion,
    bool FFmpegInstalled,
    string? FFmpegVersion,
    bool DenoInstalled,
    string? DenoVersion
)
{
    public bool AllReady => YtDlpInstalled && FFmpegInstalled && DenoInstalled;
}

public record ProvisioningProgress(
    string CurrentStep,
    double PercentComplete,
    long BytesReceived,
    long? TotalBytes
);

public interface IDependencyManager
{
    string BinDirectory { get; }
    string YtDlpPath { get; }
    string FFmpegPath { get; }
    string FFprobePath { get; }
    string DenoPath { get; }

    Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default);
    Task<bool> ProvisionAllAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default);
    Task<bool> UpdateYtDlpAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default);
    Task<bool> EnsureDenoAsync(IProgress<ProvisioningProgress>? progress = null, CancellationToken ct = default);
    Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default);
}
