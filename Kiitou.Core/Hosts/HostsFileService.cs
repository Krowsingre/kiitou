using System.Security.Cryptography;
using System.Text;

namespace Kiitou.Hosts;

/// <summary>The hosts file as read at one moment.</summary>
public sealed record HostsSnapshot(byte[] Bytes, string Text, string Sha256);

/// <summary>Thrown when the hosts file no longer matches the state a write was planned against.</summary>
public sealed class HostsChangedException() : IOException("The hosts file was changed by something else after it was read.");

/// <summary>
/// Byte-exact reading and safe replacement of a hosts file.
/// Text is decoded as Latin-1 so that every byte of unrelated lines round-trips unchanged.
/// </summary>
public sealed class HostsFileService
{
    private const int MaximumSizeBytes = 5 * 1024 * 1024;
    private static readonly Encoding ByteExact = Encoding.Latin1;

    public string HostsPath { get; }

    public HostsFileService(string hostsPath) => HostsPath = hostsPath;

    public static string SystemHostsPath =>
        Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    public static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static byte[] Encode(string text) => ByteExact.GetBytes(text);

    public static string Decode(byte[] bytes) => ByteExact.GetString(bytes);

    public HostsSnapshot Read()
    {
        byte[] bytes = File.Exists(HostsPath) ? File.ReadAllBytes(HostsPath) : [];
        if (bytes.Length > MaximumSizeBytes)
            throw new IOException("The hosts file is larger than 5 MB and will not be modified.");
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            throw new IOException("The hosts file is stored as UTF-16, which Windows does not read reliably. It will not be modified.");
        return new HostsSnapshot(bytes, Decode(bytes), ComputeSha256(bytes));
    }

    /// <summary>
    /// Replaces the hosts file with <paramref name="newText"/>, but only if the file still has
    /// the hash it had when the change was planned.
    /// </summary>
    public void Write(string newText, string expectedSha256)
    {
        HostsSnapshot current = Read();
        if (!string.Equals(current.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new HostsChangedException();

        byte[] bytes = Encode(newText);
        string directory = Path.GetDirectoryName(HostsPath)
            ?? throw new IOException("The hosts file path has no directory.");
        string temp = Path.Combine(directory, $".kiitou-hosts-{Guid.NewGuid():N}.tmp");

        bool exists = File.Exists(HostsPath);
        FileAttributes attributes = exists ? File.GetAttributes(HostsPath) : FileAttributes.Normal;
        bool readOnly = exists && attributes.HasFlag(FileAttributes.ReadOnly);
        if (readOnly) File.SetAttributes(HostsPath, attributes & ~FileAttributes.ReadOnly);

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            try
            {
                if (exists) File.Replace(temp, HostsPath, null, ignoreMetadataErrors: true);
                else File.Move(temp, HostsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Some security products block replacing the hosts file but allow rewriting it in place.
                using var stream = new FileStream(HostsPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(bytes);
                stream.Flush(true);
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (readOnly && File.Exists(HostsPath))
                File.SetAttributes(HostsPath, File.GetAttributes(HostsPath) | FileAttributes.ReadOnly);
        }
    }

    /// <summary>True when the hosts file now holds exactly <paramref name="expectedText"/>.</summary>
    public bool Verify(string expectedText) =>
        string.Equals(Read().Sha256, ComputeSha256(Encode(expectedText)), StringComparison.OrdinalIgnoreCase);
}
