using System.Text.Json;
using System.Text.Json.Serialization;
using Kiitou.Models;

namespace Kiitou.Network;

/// <summary>One address test, with where it was made from.</summary>
public sealed class TestLogEntry
{
    public DateTime TestedUtc { get; set; }
    public string Hostname { get; set; } = "";
    public string IpAddress { get; set; } = "";
    /// <summary>The public address the test came from; empty when it could not be found out.</summary>
    public string FromIp { get; set; } = "";
    public string FromCountryCode { get; set; } = "";
    public string FromCountry { get; set; } = "";
    public bool ThroughProxy { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<VerificationStatus>))]
    public VerificationStatus Status { get; set; }
}

/// <summary>
/// The history of address tests, kept in its own file beside the settings so it survives updates
/// and never makes the settings file large. The newest entries are kept; the oldest are dropped.
/// </summary>
public sealed class TestLogStore
{
    public const int MaximumEntries = 5000;
    private const string FileName = "test-log.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private readonly string _path;
    private readonly object _gate = new();
    private List<TestLogEntry>? _entries;

    public TestLogStore(string dataDirectory) => _path = Path.Combine(dataDirectory, FileName);

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<TestLogEntry> Entries { get; set; } = [];
    }

    /// <summary>All entries, newest first.</summary>
    public IReadOnlyList<TestLogEntry> Entries
    {
        get
        {
            lock (_gate) return Load().OrderByDescending(e => e.TestedUtc).ToList();
        }
    }

    public void Add(TestLogEntry entry)
    {
        lock (_gate)
        {
            List<TestLogEntry> entries = Load();
            entries.Add(entry);
            if (entries.Count > MaximumEntries)
                entries.RemoveRange(0, entries.Count - MaximumEntries);
            Write(entries);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries = [];
            Write(_entries);
        }
    }

    private List<TestLogEntry> Load()
    {
        if (_entries is not null) return _entries;
        _entries = [];
        try
        {
            if (File.Exists(_path))
            {
                Document? document = JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), Options);
                if (document?.Entries is { } entries)
                    _entries = entries.Where(e => e is not null && e.Hostname is not null && e.IpAddress is not null)
                                      .OrderBy(e => e.TestedUtc).ToList();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged log is set aside rather than lost, and a new one is started.
            try { File.Move(_path, _path + ".damaged", overwrite: true); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
        }
        return _entries;
    }

    private void Write(List<TestLogEntry> entries)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, $".kiitou-log-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document { Entries = entries }, Options));
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
