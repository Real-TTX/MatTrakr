using System.Text.Json;
using System.Text.Json.Nodes;

namespace MatTrakr.Services;

/// <summary>
/// Reads and writes the admin-editable config file in the data volume
/// (data/config/settings.json). The file is registered with reloadOnChange,
/// so saved values become effective without a restart.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private readonly string _settingsFile;
    private readonly IConfiguration _config;

    public SettingsService(string dataPath, IConfiguration config)
    {
        _settingsFile = Path.Combine(dataPath, "config", "settings.json");
        _config = config;
    }

    /// <summary>Effective value as the app sees it (file, env var or appsettings).</summary>
    public string? GetEffective(string key) => _config[key];

    /// <summary>
    /// Merges the given keys (e.g. "Tmdb:ApiKey") into settings.json,
    /// preserving everything else in the file.
    /// </summary>
    public async Task SaveAsync(IDictionary<string, string?> values)
    {
        JsonObject root;
        if (File.Exists(_settingsFile))
        {
            var text = await File.ReadAllTextAsync(_settingsFile);
            root = string.IsNullOrWhiteSpace(text)
                ? new JsonObject()
                : JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        foreach (var (key, value) in values)
        {
            // "Tmdb:ApiKey" -> nested { "Tmdb": { "ApiKey": ... } }
            var parts = key.Split(':');
            var node = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (node[parts[i]] is not JsonObject child)
                {
                    child = new JsonObject();
                    node[parts[i]] = child;
                }
                node = child;
            }
            node[parts[^1]] = value ?? "";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        await File.WriteAllTextAsync(_settingsFile, root.ToJsonString(WriteOpts));
    }
}
