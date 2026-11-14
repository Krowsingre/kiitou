namespace Kiitou.Services;

public sealed record StoragePaths(string ApplicationDirectory, string DataDirectory, bool Portable)
{
    public static StoragePaths Resolve() => Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static StoragePaths Resolve(string baseDirectory, string localApplicationData)
    {
        // Keeps the separator on a drive root ("E:\"), so paths never become drive-relative ("E:config.json").
        string applicationDirectory = Path.TrimEndingDirectorySeparator(baseDirectory);
        string localData = Path.Combine(localApplicationData, "Kiitou");

        // Portable-first: use the executable folder whenever it is writable,
        // otherwise fall back to Local AppData. Never require program installation.
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
