
using System.Text.Json;
using Kiitou.Models;

namespace Kiitou.Services;

public sealed class ConfigurationStore
{
    private const string ConfigFileName = "config.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public StoragePaths Paths { get; }
    private string ConfigFilePath => Path.Combine(Paths.DataDirectory, ConfigFileName);

    public ConfigurationStore(StoragePaths paths) => Paths = paths;

    public AppConfiguration Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var initial = AppConfiguration.CreateDefault();
            Save(initial);
            return initial;
        }

        try
        {
            using var stream = File.OpenRead(ConfigFilePath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(stream, JsonOptions)
                ?? throw new InvalidDataException("Configuration JSON has no root object.");
            Validate(config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Configuration JSON is invalid; the existing file was not overwritten.", ex);
        }
    }

    public void Save(AppConfiguration config)
    {
        Validate(config);
        Directory.CreateDirectory(Paths.DataDirectory);
        var temp = Path.Combine(Paths.DataDirectory, $".kiitou-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, config, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temp, ConfigFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void Validate(AppConfiguration config)
    {
        if (config.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported configuration schema version.");
        if (config.Theme is null || !ThemeCatalog.IsValid(config.Theme.Color, config.Theme.Mode))
            throw new InvalidDataException("Unrecognized theme selection.");
        config.Hosts ??= new HostsState();
        if (config.Websites is null || config.Profiles is null || config.Profiles.Count == 0)
            throw new InvalidDataException("Configuration is missing required collections or profiles.");
        if (config.Websites.Any(w => w is null || w.Hostname is null || w.Addresses is null ||
                w.Addresses.Any(a => a is null || a.IpAddress is null)))
            throw new InvalidDataException("Configuration contains an empty or incomplete website entry.");
        if (config.Profiles.Any(p => p is null || p.Name is null || p.Selections is null ||
                p.Selections.Any(s => s is null)))
            throw new InvalidDataException("Configuration contains an empty or incomplete profile entry.");
        if (HasDuplicates(config.Websites.Select(w => w.Id)) ||
            HasDuplicates(config.Websites.SelectMany(w => w.Addresses).Select(a => a.Id)) ||
            HasDuplicates(config.Profiles.Select(p => p.Id)))
            throw new InvalidDataException("Configuration contains duplicate identifiers.");

        foreach (var selection in config.Profiles.SelectMany(p => p.Selections))
        {
            var website = config.Websites.FirstOrDefault(w => w.Id == selection.WebsiteId)
                ?? throw new InvalidDataException("A profile refers to a website that does not exist.");
            foreach (Guid? selected in new[] { selection.SelectedIpv4Id, selection.SelectedIpv6Id })
            {
                if (selected is Guid id && !website.Addresses.Any(a => a.Id == id))
                    throw new InvalidDataException("A profile selects an address that does not exist.");
            }
        }

        if (!config.Profiles.Any(x => x.Id == config.ActiveProfileId))
            throw new InvalidDataException("Active profile does not exist.");
    }

    private static bool HasDuplicates(IEnumerable<Guid> ids)
    {
        var seen = new HashSet<Guid>();
        return ids.Any(id => !seen.Add(id));
    }
}
