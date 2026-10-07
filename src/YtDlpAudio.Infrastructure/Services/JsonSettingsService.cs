using System.Text.Json;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Services;

public class JsonSettingsService : ISettingsService
{
    private readonly IAppPathsService _appPaths;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonSettingsService(IAppPathsService appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task<UserSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        string path = _appPaths.SettingsFilePath;
        if (!File.Exists(path))
        {
            return new UserSettings(OutputDirectory: _appPaths.DefaultDownloadsDirectory);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<UserSettings>(stream, JsonOptions, ct);
            if (settings == null)
            {
                return new UserSettings(OutputDirectory: _appPaths.DefaultDownloadsDirectory);
            }

            if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
            {
                settings = settings with { OutputDirectory = _appPaths.DefaultDownloadsDirectory };
            }

            return settings;
        }
        catch
        {
            return new UserSettings(OutputDirectory: _appPaths.DefaultDownloadsDirectory);
        }
    }

    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken ct = default)
    {
        string path = _appPaths.SettingsFilePath;
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        string tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
