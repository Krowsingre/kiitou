using Kiitou.Services;

namespace Kiitou.Tests;

public class StoragePathsTests
{
    [Fact]
    public void SettingsLiveInLocalApplicationDataNeverBesideTheProgram()
    {
        using var app = new TempDirectory();
        using var local = new TempDirectory();

        var paths = StoragePaths.Resolve(app.Path + Path.DirectorySeparatorChar, local.Path);

        Assert.False(paths.Portable);
        Assert.Equal(app.Path, paths.ApplicationDirectory);
        Assert.Equal(Path.Combine(local.Path, "Kiitou"), paths.DataDirectory);
        Assert.True(Directory.Exists(paths.DataDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(app.Path));
        Assert.Empty(Directory.GetFileSystemEntries(paths.DataDirectory));
    }

    [Fact]
    public void TheDataFolderCanBeRedirectedForTests()
    {
        using var app = new TempDirectory();
        using var local = new TempDirectory();
        using var elsewhere = new TempDirectory();
        string target = Path.Combine(elsewhere.Path, "data");

        var paths = StoragePaths.Resolve(app.Path, local.Path, target);

        Assert.Equal(target, paths.DataDirectory);
        Assert.True(Directory.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(local.Path, "Kiitou")));
    }

    [Fact]
    public void DriveRootKeepsItsSeparator()
    {
        using var local = new TempDirectory();
        string root = Path.GetPathRoot(local.Path)!;

        var paths = StoragePaths.Resolve(root, local.Path);

        Assert.Equal(root, paths.ApplicationDirectory);
    }

    [Fact]
    public void AnUnusableSettingsFolderIsReported()
    {
        using var local = new TempDirectory();
        string blocked = Path.Combine(local.Path, "Kiitou");
        File.WriteAllText(blocked, "a file where the folder should be");

        Assert.ThrowsAny<IOException>(() => StoragePaths.Resolve(local.Path, local.Path));
    }
}
