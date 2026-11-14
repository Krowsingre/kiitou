using Kiitou.Services;

namespace Kiitou.Tests;

public class StoragePathsTests
{
    [Fact]
    public void WritableApplicationFolderIsUsedAsPortableStorage()
    {
        using var app = new TempDirectory();
        using var local = new TempDirectory();

        var paths = StoragePaths.Resolve(app.Path + Path.DirectorySeparatorChar, local.Path);

        Assert.True(paths.Portable);
        Assert.Equal(app.Path, paths.DataDirectory);
        Assert.Empty(Directory.GetFileSystemEntries(app.Path));
        Assert.False(Directory.Exists(Path.Combine(local.Path, "Kiitou")));
    }

    [Fact]
    public void UnwritableApplicationFolderFallsBackToLocalAppData()
    {
        using var local = new TempDirectory();
        string missing = Path.Combine(local.Path, "does-not-exist");

        var paths = StoragePaths.Resolve(missing, local.Path);

        Assert.False(paths.Portable);
        Assert.Equal(Path.Combine(local.Path, "Kiitou"), paths.DataDirectory);
        Assert.True(Directory.Exists(paths.DataDirectory));
    }

    [Fact]
    public void DriveRootKeepsItsSeparator()
    {
        using var local = new TempDirectory();
        string root = Path.GetPathRoot(local.Path)!;

        var paths = StoragePaths.Resolve(root, local.Path);

        Assert.Equal(root, paths.ApplicationDirectory);
        Assert.True(Path.IsPathFullyQualified(Path.Combine(paths.ApplicationDirectory, "config.json")));
    }
}
