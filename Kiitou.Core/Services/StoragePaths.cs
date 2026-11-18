namespace Kiitou.Services;

/// <summary>
/// Where Kiitou is installed and where it keeps its settings and hosts-file backups.
/// Settings always live in the signed-in user's local application data, never beside the program.
/// </summary>
public sealed record StoragePaths(string ApplicationDirectory, string DataDirectory, bool Portable)
{
    /// <summary>Environment variable that redirects the data folder. Used by automated tests.</summary>
    public const string DataDirectoryVariable = "KIITOU_DATA_DIR";

    public static StoragePaths Resolve() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable(DataDirectoryVariable));

    public static StoragePaths Resolve(string baseDirectory, string localApplicationData, string? overrideDirectory = null)
    {
        // Keeps the separator on a drive root ("E:\"), so paths never become drive-relative.
        string applicationDirectory = Path.TrimEndingDirectorySeparator(baseDirectory);
        string dataDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(localApplicationData, "Kiitou")
            : Path.GetFullPath(overrideDirectory);

        Directory.CreateDirectory(dataDirectory);
        if (!CanWrite(dataDirectory))
            throw new IOException("Kiitou cannot write to its settings folder: " + dataDirectory);

        return new StoragePaths(applicationDirectory, dataDirectory, false);
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
