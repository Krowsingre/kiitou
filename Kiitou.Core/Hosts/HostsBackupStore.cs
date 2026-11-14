using System.Globalization;

namespace Kiitou.Hosts;

public sealed record HostsBackup(string Path, DateTime CreatedUtc, bool IsOriginal, long SizeBytes);

/// <summary>
/// Keeps copies of the hosts file inside Kiitou's data folder. The first copy ever taken is kept
/// permanently as the original; later copies are rotated.
/// </summary>
public sealed class HostsBackupStore
{
    public const string OriginalFileName = "hosts-original.bak";
    private const string Prefix = "hosts-";
    private const string Extension = ".bak";
    private const string StampFormat = "yyyyMMdd-HHmmss-fff";
    private const int MaximumRotatingBackups = 20;

    public string Directory { get; }

    public HostsBackupStore(string directory) => Directory = directory;

    /// <summary>Saves the snapshot, also keeping it as the permanent original if none exists yet.</summary>
    public HostsBackup Create(HostsSnapshot snapshot, DateTime utcNow)
    {
        System.IO.Directory.CreateDirectory(Directory);

        string original = System.IO.Path.Combine(Directory, OriginalFileName);
        if (!File.Exists(original)) WriteNew(original, snapshot.Bytes);

        string path = System.IO.Path.Combine(
            Directory, Prefix + utcNow.ToString(StampFormat, CultureInfo.InvariantCulture) + Extension);
        for (int attempt = 1; File.Exists(path); attempt++)
        {
            path = System.IO.Path.Combine(
                Directory,
                Prefix + utcNow.ToString(StampFormat, CultureInfo.InvariantCulture) + "-" + attempt + Extension);
        }
        WriteNew(path, snapshot.Bytes);
        Prune();
        return new HostsBackup(path, utcNow, false, snapshot.Bytes.LongLength);
    }

    /// <summary>All backups, the permanent original first, then newest to oldest.</summary>
    public IReadOnlyList<HostsBackup> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var backups = new List<HostsBackup>();
        foreach (string path in System.IO.Directory.GetFiles(Directory, Prefix + "*" + Extension))
        {
            var info = new FileInfo(path);
            bool isOriginal = string.Equals(info.Name, OriginalFileName, StringComparison.OrdinalIgnoreCase);
            backups.Add(new HostsBackup(path, info.LastWriteTimeUtc, isOriginal, info.Length));
        }
        return backups
            .OrderByDescending(b => b.IsOriginal)
            .ThenByDescending(b => System.IO.Path.GetFileName(b.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public byte[] ReadBytes(string backupPath)
    {
        string full = System.IO.Path.GetFullPath(backupPath);
        string root = System.IO.Path.GetFullPath(Directory) + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The backup is not inside Kiitou's backup folder.");
        return File.ReadAllBytes(full);
    }

    private void Prune()
    {
        var rotating = List().Where(b => !b.IsOriginal).Skip(MaximumRotatingBackups);
        foreach (HostsBackup backup in rotating)
        {
            try { File.Delete(backup.Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }
}
