using System.IO;

namespace Kiitou.Services;

public sealed record StoragePaths(string ApplicationDirectory, string DataDirectory, bool Portable)
{
    public static StoragePaths Resolve()
    {
        string applicationDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string localData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kiitou");

        // Use an already-existing writable portable config directory, or the
        // executable folder when writable. Never require program installation.
        if (CanWrite(applicationDirectory))
            return new StoragePaths(applicationDirectory, applicationDirectory, true);

        Directory.CreateDirectory(localData);
        if (!CanWrite(localData))
            throw new IOException("No writable configuration directory is available.");

        return new StoragePaths(applicationDirectory, localData, false);
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, $".kiitou-write-test-{Guid.NewGuid():N}");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.WriteByte(0);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
