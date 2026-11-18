using System.Text.Json.Nodes;

namespace Kiitou.Services;

/// <summary>
/// Converts settings saved by older versions of Kiitou to the current format, one version at a
/// time. Every format ever released must stay convertible: add a step here whenever
/// <c>AppConfiguration.CurrentSchemaVersion</c> goes up, and a test that loads the old format.
/// </summary>
public static class ConfigurationMigrations
{
    /// <summary>The format version of a settings document. Files from before versioning count as 1.</summary>
    public static int ReadVersion(JsonObject root)
    {
        foreach (var (name, value) in root)
        {
            if (!string.Equals(name, "SchemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
            if (value is JsonValue number && number.TryGetValue(out int version) && version >= 1) return version;
            throw new InvalidDataException("Unsupported configuration schema version.");
        }
        return 1;
    }

    /// <summary>Upgrades the document in place from <paramref name="fromVersion"/> to the current format.</summary>
    public static void Upgrade(JsonObject root, int fromVersion)
    {
        int version = fromVersion;
        while (version < Models.AppConfiguration.CurrentSchemaVersion)
        {
            switch (version)
            {
                case 1:
                    // 1 to 2 (Kiitou 0.9.2): added the menu order, the test proxy and the window
                    // position. They take their defaults, so no existing value changes.
                    break;
                default:
                    throw new InvalidDataException($"Kiitou does not know how to convert settings format {version}.");
            }
            version++;
        }

        // Replace any existing spelling of the version property with the current one.
        foreach (string name in root.Select(pair => pair.Key)
                     .Where(key => string.Equals(key, "SchemaVersion", StringComparison.OrdinalIgnoreCase)).ToList())
            root.Remove(name);
        root["SchemaVersion"] = Models.AppConfiguration.CurrentSchemaVersion;
    }
}
