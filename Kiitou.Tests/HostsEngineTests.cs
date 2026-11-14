using System.Net;
using System.Text;
using System.Text.Json;
using Kiitou.Hosts;

namespace Kiitou.Tests;

public class HostsEngineTests
{
    private const string Original = "# sample hosts\r\n127.0.0.1 localhost\r\n10.0.0.5 intranet.local\r\n";
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HostsMapping[] Mappings =
    [
        new("93.184.216.34", "example.com"),
        new("2606:2800:220:1:248:1893:25c8:1946", "example.com")
    ];

    private sealed class Fixture : IDisposable
    {
        public TempDirectory Dir { get; } = new();
        public HostsFileService Hosts { get; }
        public HostsBackupStore Backups { get; }
        public int Flushes { get; private set; }

        public Fixture(string content = Original)
        {
            File.WriteAllBytes(Dir.File("hosts"), Encoding.Latin1.GetBytes(content));
            Hosts = new HostsFileService(Dir.File("hosts"));
            Backups = new HostsBackupStore(Path.Combine(Dir.Path, "backups"));
        }

        public HostsApplyService Service(IHostsWriter? writer = null) =>
            new(Hosts, Backups, writer ?? new DirectHostsWriter(Hosts, () => { Flushes++; return true; }), () => Now);

        public string Text => Encoding.Latin1.GetString(File.ReadAllBytes(Dir.File("hosts")));

        public void Dispose() => Dir.Dispose();
    }

    private sealed class FixedOutcomeWriter(HostsWriteOutcome outcome) : IHostsWriter
    {
        public int Calls { get; private set; }

        public HostsWriteOutcome Execute(HostsHelperRequest request)
        {
            Calls++;
            return outcome;
        }
    }

    [Fact]
    public void ApplyWritesMappingsBacksUpAndFlushesDns()
    {
        using var fx = new Fixture();
        var service = fx.Service();

        HostsApplyPreview preview = service.Preview(Mappings, null);
        Assert.True(preview.ChangeNeeded);
        Assert.False(preview.ExternalEditDetected);
        HostsOperationResult result = service.Apply(preview);

        Assert.Equal(HostsWriteOutcome.Success, result.Outcome);
        Assert.StartsWith(Original, fx.Text);
        Assert.Equal(Mappings, ManagedHostsBlock.Analyze(fx.Text).ManagedMappings);
        Assert.Equal(fx.Hosts.Read().Sha256, result.NewSha256);
        Assert.Equal(1, fx.Flushes);

        Assert.Equal(Original, Encoding.Latin1.GetString(File.ReadAllBytes(result.BackupPath!)));
        var backups = fx.Backups.List();
        Assert.Equal(2, backups.Count);
        Assert.True(backups[0].IsOriginal);
        Assert.Equal(Original, Encoding.Latin1.GetString(fx.Backups.ReadBytes(backups[0].Path)));
    }

    [Fact]
    public void ApplyingTheSameMappingsAgainChangesNothing()
    {
        using var fx = new Fixture();
        var service = fx.Service();
        service.Apply(service.Preview(Mappings, null));
        string after = fx.Text;

        HostsApplyPreview again = service.Preview(Mappings, fx.Hosts.Read().Sha256);
        Assert.False(again.ChangeNeeded);
        HostsOperationResult result = service.Apply(again);

        Assert.True(result.Succeeded);
        Assert.Null(result.BackupPath);
        Assert.Equal(after, fx.Text);
        Assert.Equal(1, fx.Flushes);
    }

    [Fact]
    public void RemovingAllMappingsReturnsTheFileToItsOriginalBytes()
    {
        using var fx = new Fixture();
        var service = fx.Service();
        service.Apply(service.Preview(Mappings, null));

        HostsOperationResult result = service.Apply(service.Preview([], null));

        Assert.True(result.Succeeded);
        Assert.Equal(Original, fx.Text);
    }

    [Fact]
    public void NonAsciiBytesInUnrelatedLinesSurviveUnchanged()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.Latin1.GetBytes("# café üñ\r\n10.0.0.5 intranet.local\r\n")];
        using var fx = new Fixture();
        File.WriteAllBytes(fx.Dir.File("hosts"), original);
        var service = fx.Service();

        service.Apply(service.Preview(Mappings, null));
        byte[] applied = File.ReadAllBytes(fx.Dir.File("hosts"));
        Assert.Equal(original, applied[..original.Length]);

        service.Apply(service.Preview([], null));
        Assert.Equal(original, File.ReadAllBytes(fx.Dir.File("hosts")));
    }

    [Fact]
    public void ExternalEditSinceLastWriteIsDetectedAndPreserved()
    {
        using var fx = new Fixture();
        var service = fx.Service();
        HostsOperationResult first = service.Apply(service.Preview(Mappings, null));

        File.AppendAllText(fx.Dir.File("hosts"), "192.168.1.9 nas.local\r\n");
        HostsApplyPreview preview = service.Preview([Mappings[0]], first.NewSha256);

        Assert.True(preview.ExternalEditDetected);
        Assert.True(service.Apply(preview).Succeeded);
        Assert.Contains("192.168.1.9 nas.local", fx.Text);
        Assert.Equal(new[] { Mappings[0] }, ManagedHostsBlock.Analyze(fx.Text).ManagedMappings);
    }

    [Fact]
    public void ChangeBetweenPreviewAndWriteIsRefused()
    {
        using var fx = new Fixture();
        var service = fx.Service();
        HostsApplyPreview preview = service.Preview(Mappings, null);

        string edited = Original + "192.168.1.9 nas.local\r\n";
        File.WriteAllBytes(fx.Dir.File("hosts"), Encoding.Latin1.GetBytes(edited));
        HostsOperationResult result = service.Apply(preview);

        Assert.Equal(HostsWriteOutcome.HostsChanged, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(edited, fx.Text);
        Assert.Equal(0, fx.Flushes);
    }

    [Fact]
    public void CancelledElevationLeavesTheHostsFileUntouched()
    {
        using var fx = new Fixture();
        var writer = new FixedOutcomeWriter(HostsWriteOutcome.Cancelled);
        var service = fx.Service(writer);

        HostsOperationResult result = service.Apply(service.Preview(Mappings, null));

        Assert.Equal(1, writer.Calls);
        Assert.Equal(HostsWriteOutcome.Cancelled, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Null(result.NewSha256);
        Assert.Equal(Original, fx.Text);
    }

    [Fact]
    public void WriterClaimingSuccessWithoutWritingIsReportedAsVerifyFailed()
    {
        using var fx = new Fixture();
        var service = fx.Service(new FixedOutcomeWriter(HostsWriteOutcome.Success));

        HostsOperationResult result = service.Apply(service.Preview(Mappings, null));

        Assert.Equal(HostsWriteOutcome.VerifyFailed, result.Outcome);
        Assert.Equal(Original, fx.Text);
    }

    [Fact]
    public void ReadOnlyHostsFileIsWrittenAndStaysReadOnly()
    {
        using var fx = new Fixture();
        string path = fx.Dir.File("hosts");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            var service = fx.Service();
            Assert.True(service.Apply(service.Preview(Mappings, null)).Succeeded);
            Assert.Equal(2, ManagedHostsBlock.Analyze(fx.Text).ManagedMappings.Count);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void WriteLeavesNoTemporaryFiles()
    {
        using var fx = new Fixture();
        var service = fx.Service();
        service.Apply(service.Preview(Mappings, null));
        Assert.Equal(new[] { "hosts" }, Directory.GetFiles(fx.Dir.Path).Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public void RestoreReturnsBackedUpContentAndBacksUpCurrentState()
    {
        using var fx = new Fixture();
        var service = fx.Service();
        service.Apply(service.Preview(Mappings, null));
        string applied = fx.Text;
        string original = fx.Backups.List().Single(b => b.IsOriginal).Path;

        HostsOperationResult result = service.Restore(original);

        Assert.True(result.Succeeded);
        Assert.Equal(Original, fx.Text);
        Assert.Equal(applied, Encoding.Latin1.GetString(File.ReadAllBytes(result.BackupPath!)));
    }

    [Fact]
    public void RestoreRefusesFilesOutsideTheBackupFolder()
    {
        using var fx = new Fixture();
        string outside = fx.Dir.File("elsewhere.bak");
        File.WriteAllText(outside, "1.2.3.4 evil.example\r\n");

        Assert.Throws<IOException>(() => fx.Service().Restore(outside));
        Assert.Equal(Original, fx.Text);
    }

    [Fact]
    public void OriginalBackupIsNeverOverwrittenAndRotatingBackupsAreCapped()
    {
        using var fx = new Fixture();
        HostsSnapshot first = fx.Hosts.Read();
        fx.Backups.Create(first, Now);
        for (int i = 1; i <= 30; i++)
        {
            byte[] bytes = Encoding.Latin1.GetBytes($"# version {i}\r\n");
            fx.Backups.Create(new HostsSnapshot(bytes, "", HostsFileService.ComputeSha256(bytes)), Now.AddMinutes(i));
        }

        var backups = fx.Backups.List();
        Assert.Equal(21, backups.Count);
        Assert.True(backups[0].IsOriginal);
        Assert.Equal(first.Bytes, fx.Backups.ReadBytes(backups[0].Path));
        Assert.Equal("# version 30\r\n", Encoding.Latin1.GetString(fx.Backups.ReadBytes(backups[1].Path)));
    }

    [Theory]
    [InlineData("93.184.216.034", "example.com")]
    [InlineData("93.184.216.34", "Example.com")]
    [InlineData("93.184.216.34", "example.com # injected\r\n1.2.3.4 other.example")]
    [InlineData("not-an-ip", "example.com")]
    [InlineData("93.184.216.34", "localhost")]
    public void HelperRejectsMappingsThatAreNotStrictlyValid(string ip, string hostname)
    {
        using var fx = new Fixture();
        var request = new HostsHelperRequest
        {
            ExpectedSha256 = fx.Hosts.Read().Sha256,
            Mappings = [new HostsMapping(ip, hostname)]
        };

        Assert.Equal(HostsWriteOutcome.InvalidRequest, HostsHelper.Execute(request, fx.Hosts, () => true));
        Assert.Equal(Original, fx.Text);
    }

    [Fact]
    public void HelperRejectsUnknownOperationsAndBadHashes()
    {
        using var fx = new Fixture();
        string sha = fx.Hosts.Read().Sha256;

        Assert.Equal(HostsWriteOutcome.InvalidRequest,
            HostsHelper.Execute(new HostsHelperRequest { Operation = "delete", ExpectedSha256 = sha }, fx.Hosts, () => true));
        Assert.Equal(HostsWriteOutcome.InvalidRequest,
            HostsHelper.Execute(new HostsHelperRequest { ExpectedSha256 = "abc" }, fx.Hosts, () => true));
        Assert.Equal(HostsWriteOutcome.InvalidRequest,
            HostsHelper.Execute(new HostsHelperRequest { Operation = "restore", ExpectedSha256 = sha, RestoreContentBase64 = "%%%" }, fx.Hosts, () => true));
        Assert.Equal(HostsWriteOutcome.HostsChanged,
            HostsHelper.Execute(new HostsHelperRequest { ExpectedSha256 = new string('0', 64), Mappings = Mappings.ToList() }, fx.Hosts, () => true));
        Assert.Equal(Original, fx.Text);
    }

    [Fact]
    public void HelperReportsFlushFailureSeparatelyFromWriteSuccess()
    {
        using var fx = new Fixture();
        var request = new HostsHelperRequest { ExpectedSha256 = fx.Hosts.Read().Sha256, Mappings = Mappings.ToList() };

        Assert.Equal(HostsWriteOutcome.SuccessFlushFailed, HostsHelper.Execute(request, fx.Hosts, () => false));
        Assert.Equal(2, ManagedHostsBlock.Analyze(fx.Text).ManagedMappings.Count);
    }

    [Fact]
    public void HelperRequestSurvivesJsonRoundTrip()
    {
        var request = new HostsHelperRequest { ExpectedSha256 = new string('a', 64), Mappings = Mappings.ToList() };
        var copy = JsonSerializer.Deserialize<HostsHelperRequest>(JsonSerializer.Serialize(request))!;

        Assert.Equal(request.Operation, copy.Operation);
        Assert.Equal(request.ExpectedSha256, copy.ExpectedSha256);
        Assert.Equal(request.Mappings, copy.Mappings);
    }

    [Fact]
    public void HelperCommandLineRejectsMalformedInvocations()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("bad.json"), "{ not json");

        Assert.Equal((int)HostsWriteOutcome.InvalidRequest, HostsHelper.Run([]));
        Assert.Equal((int)HostsWriteOutcome.InvalidRequest, HostsHelper.Run([HostsHelper.Switch]));
        Assert.Equal((int)HostsWriteOutcome.InvalidRequest, HostsHelper.Run(["--other", dir.File("bad.json")]));
        Assert.Equal((int)HostsWriteOutcome.InvalidRequest, HostsHelper.Run([HostsHelper.Switch, dir.File("missing.json")]));
        Assert.Equal((int)HostsWriteOutcome.InvalidRequest, HostsHelper.Run([HostsHelper.Switch, dir.File("bad.json")]));
    }

    [Fact]
    public void Utf16HostsFileIsRefused()
    {
        using var fx = new Fixture();
        File.WriteAllBytes(fx.Dir.File("hosts"), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("127.0.0.1 localhost")).ToArray());
        Assert.Throws<IOException>(() => fx.Hosts.Read());
    }

    /// <summary>
    /// End-to-end check against the real Windows hosts file. Runs only on the build runner
    /// (a disposable machine), never on a developer's or user's computer.
    /// </summary>
    [Fact]
    public void RealSystemHostsFileRoundTripOnBuildRunner()
    {
        if (Environment.GetEnvironmentVariable("KIITOU_CI") != "1" || !Environment.IsPrivilegedProcess) return;

        using var dir = new TempDirectory();
        var hosts = new HostsFileService(HostsFileService.SystemHostsPath);
        var service = new HostsApplyService(hosts, new HostsBackupStore(dir.Path), new DirectHostsWriter(hosts));
        byte[] before = hosts.Read().Bytes;
        const string name = "kiitou-ci-check.example";

        try
        {
            HostsOperationResult applied = service.Apply(service.Preview([new HostsMapping("203.0.113.77", name)], null));
            Assert.True(applied.Succeeded, applied.Outcome.ToString());
            Assert.Contains(Dns.GetHostAddresses(name), a => a.ToString() == "203.0.113.77");
        }
        finally
        {
            HostsOperationResult removed = service.Apply(service.Preview([], null));
            Assert.True(removed.Succeeded, removed.Outcome.ToString());
        }

        byte[] after = hosts.Read().Bytes;
        // If the file did not end with a line break, Kiitou adds exactly one and leaves it.
        Assert.True(after.SequenceEqual(before) || after.SequenceEqual(before.Concat("\r\n"u8.ToArray())) ||
                    after.SequenceEqual(before.Concat("\n"u8.ToArray())));
    }
}
