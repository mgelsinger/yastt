using System.Text.Json;

namespace DictateTray.Core.Configuration;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();

    public AppSettings LoadOrCreate()
    {
        lock (_sync)
        {
            AppPaths.EnsureDirectories();

            if (!File.Exists(AppPaths.SettingsFilePath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            if (settings is null)
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(AppPaths.SettingsFilePath, json);
        }
    }
}
