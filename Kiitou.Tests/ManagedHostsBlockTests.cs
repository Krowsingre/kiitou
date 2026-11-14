using Kiitou.Hosts;

namespace Kiitou.Tests;

public class ManagedHostsBlockTests
{
    private const string Original =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
        "#\r\n" +
        "127.0.0.1       localhost\r\n" +
        "10.0.0.5\tintranet.local  printer.local # office\r\n";

    private static readonly HostsMapping[] Two =
    [
        new("93.184.216.34", "example.com"),
        new("2606:2800:220:1:248:1893:25c8:1946", "example.com")
    ];

    [Fact]
    public void ApplyAppendsManagedBlockAndKeepsOriginalBytes()
    {
        string result = ManagedHostsBlock.Apply(Original, Two);

        Assert.StartsWith(Original, result);
        Assert.Equal(
            Original +
            ManagedHostsBlock.BeginMarker + "\r\n" +
            "93.184.216.34 example.com # Kiitou\r\n" +
            "2606:2800:220:1:248:1893:25c8:1946 example.com # Kiitou\r\n" +
            ManagedHostsBlock.EndMarker + "\r\n",
            result);
    }

    [Fact]
    public void ApplyIsIdempotent()
    {
        string once = ManagedHostsBlock.Apply(Original, Two);
        Assert.Equal(once, ManagedHostsBlock.Apply(once, Two));
    }

    [Fact]
    public void ApplyingNoMappingsRestoresOriginalText()
    {
        string applied = ManagedHostsBlock.Apply(Original, Two);
        Assert.Equal(Original, ManagedHostsBlock.Apply(applied, []));
    }

    [Fact]
    public void ApplyReplacesOnlyManagedLines()
    {
        string applied = ManagedHostsBlock.Apply(Original, Two);
        string updated = ManagedHostsBlock.Apply(applied, [new HostsMapping("203.0.113.10", "media.example.com")]);

        Assert.StartsWith(Original, updated);
        Assert.DoesNotContain("93.184.216.34", updated);
        Assert.Contains("203.0.113.10 media.example.com # Kiitou", updated);
        Assert.Single(ManagedHostsBlock.Analyze(updated).ManagedMappings);
    }

    [Fact]
    public void LinesAddedAfterTheBlockArePreserved()
    {
        string applied = ManagedHostsBlock.Apply(Original, Two) + "192.168.1.9 nas.local\r\n";
        string removed = ManagedHostsBlock.Apply(applied, []);
        Assert.Equal(Original + "192.168.1.9 nas.local\r\n", removed);
    }

    [Fact]
    public void ForeignLinesInsideTheBlockArePreserved()
    {
        string text =
            Original +
            ManagedHostsBlock.BeginMarker + "\r\n" +
            "93.184.216.34 example.com # Kiitou\r\n" +
            "192.168.1.50 added-by-hand.local\r\n" +
            ManagedHostsBlock.EndMarker + "\r\n";

        string removed = ManagedHostsBlock.Apply(text, []);
        Assert.Equal(Original + "192.168.1.50 added-by-hand.local\r\n", removed);
    }

    [Fact]
    public void MissingEndMarkerDoesNotDeleteUnrelatedLines()
    {
        string text =
            Original +
            ManagedHostsBlock.BeginMarker + "\r\n" +
            "93.184.216.34 example.com # Kiitou\r\n" +
            "192.168.1.9 nas.local\r\n" +
            "# a comment the user wrote\r\n";

        HostsAnalysis analysis = ManagedHostsBlock.Analyze(text);
        Assert.True(analysis.BlockDamaged);

        string removed = ManagedHostsBlock.Apply(text, []);
        Assert.Equal(Original + "192.168.1.9 nas.local\r\n# a comment the user wrote\r\n", removed);
    }

    [Fact]
    public void TaggedLinesOutsideTheBlockAreNotTouched()
    {
        string text = Original + "198.51.100.7 copied.example.com # Kiitou\r\n";
        Assert.Equal(text, ManagedHostsBlock.Apply(text, []));
        Assert.Empty(ManagedHostsBlock.Analyze(text).ManagedMappings);
    }

    [Fact]
    public void MixedAndMissingLineEndingsArePreserved()
    {
        string text = "127.0.0.1 localhost\n10.0.0.1 a.local\r\n10.0.0.2 b.local";
        string applied = ManagedHostsBlock.Apply(text, [new HostsMapping("203.0.113.10", "c.example.com")]);

        Assert.StartsWith(text + "\r\n" + ManagedHostsBlock.BeginMarker, applied);
        Assert.StartsWith(text, ManagedHostsBlock.Apply(applied, []));
    }

    [Fact]
    public void UnixLineEndingsAreUsedWhenTheFileUsesThem()
    {
        string applied = ManagedHostsBlock.Apply("127.0.0.1 localhost\n", [new HostsMapping("203.0.113.10", "c.example.com")]);
        Assert.DoesNotContain("\r", applied);
    }

    [Fact]
    public void EmptyFileGetsOnlyTheBlock()
    {
        string applied = ManagedHostsBlock.Apply("", [new HostsMapping("203.0.113.10", "c.example.com")]);
        Assert.StartsWith(ManagedHostsBlock.BeginMarker, applied);
        Assert.Equal("", ManagedHostsBlock.Apply(applied, []));
    }

    [Fact]
    public void AnalyzeSeparatesManagedAndUnmanagedEntries()
    {
        HostsAnalysis analysis = ManagedHostsBlock.Analyze(ManagedHostsBlock.Apply(Original, Two));

        Assert.True(analysis.HasManagedBlock);
        Assert.False(analysis.BlockDamaged);
        Assert.Equal(Two, analysis.ManagedMappings);
        Assert.Equal(
            new[] { "localhost", "intranet.local", "printer.local" },
            analysis.UnmanagedEntries.Select(e => e.Hostname).ToArray());
        Assert.Equal(4, analysis.UnmanagedEntries[1].LineNumber);
    }

    [Fact]
    public void ConflictsAreReportedForSameHostAndFamilyOnly()
    {
        string text =
            "198.51.100.1 example.com\r\n" +          // conflicting IPv4
            "93.184.216.34 EXAMPLE.com\r\n" +        // same address: harmless
            "2001:db8::1 other.example.com\r\n" +     // different host
            "# 198.51.100.2 example.com\r\n";         // commented out
        HostsAnalysis analysis = ManagedHostsBlock.Analyze(text);

        var conflicts = ManagedHostsBlock.FindConflicts(analysis, Two);

        HostsConflict conflict = Assert.Single(conflicts);
        Assert.Equal("198.51.100.1", conflict.Existing.IpAddress);
        Assert.Equal(1, conflict.Existing.LineNumber);
        Assert.Equal("93.184.216.34", conflict.Desired.IpAddress);
    }
}
