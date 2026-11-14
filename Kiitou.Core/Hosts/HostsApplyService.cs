namespace Kiitou.Hosts;

/// <summary>What applying a set of mappings would do, computed without changing anything.</summary>
public sealed record HostsApplyPreview(
    HostsSnapshot Snapshot,
    HostsAnalysis Analysis,
    IReadOnlyList<HostsMapping> Desired,
    string NewText,
    IReadOnlyList<HostsConflict> Conflicts,
    bool ExternalEditDetected)
{
    public bool ChangeNeeded => NewText != Snapshot.Text;
}

public sealed record HostsOperationResult(
    HostsWriteOutcome Outcome,
    string? BackupPath,
    string? NewSha256)
{
    public bool Succeeded => Outcome is HostsWriteOutcome.Success or HostsWriteOutcome.SuccessFlushFailed;
}

/// <summary>
/// Plans, backs up, performs and verifies changes to the hosts file. Everything except the
/// single write runs without elevation.
/// </summary>
public sealed class HostsApplyService
{
    private readonly HostsFileService _hosts;
    private readonly HostsBackupStore _backups;
    private readonly IHostsWriter _writer;
    private readonly Func<DateTime> _utcNow;

    public HostsApplyService(
        HostsFileService hosts, HostsBackupStore backups, IHostsWriter writer, Func<DateTime>? utcNow = null)
    {
        _hosts = hosts;
        _backups = backups;
        _writer = writer;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public HostsBackupStore Backups => _backups;

    /// <summary>Reads the hosts file and reports its managed and unmanaged content.</summary>
    public (HostsSnapshot Snapshot, HostsAnalysis Analysis) Inspect()
    {
        HostsSnapshot snapshot = _hosts.Read();
        return (snapshot, ManagedHostsBlock.Analyze(snapshot.Text));
    }

    /// <param name="desired">The complete set of mappings Kiitou should manage.</param>
    /// <param name="lastWrittenSha256">Hash recorded after Kiitou's previous write, if any.</param>
    public HostsApplyPreview Preview(IReadOnlyList<HostsMapping> desired, string? lastWrittenSha256)
    {
        HostsSnapshot snapshot = _hosts.Read();
        HostsAnalysis analysis = ManagedHostsBlock.Analyze(snapshot.Text);
        string newText = ManagedHostsBlock.Apply(snapshot.Text, desired);
        bool externalEdit = !string.IsNullOrEmpty(lastWrittenSha256) &&
            !string.Equals(lastWrittenSha256, snapshot.Sha256, StringComparison.OrdinalIgnoreCase);
        return new HostsApplyPreview(
            snapshot, analysis, desired, newText,
            ManagedHostsBlock.FindConflicts(analysis, desired), externalEdit);
    }

    /// <summary>Backs up the hosts file, then writes the previewed change.</summary>
    public HostsOperationResult Apply(HostsApplyPreview preview)
    {
        if (!preview.ChangeNeeded)
            return new HostsOperationResult(HostsWriteOutcome.Success, null, preview.Snapshot.Sha256);

        HostsBackup backup = _backups.Create(preview.Snapshot, _utcNow());
        HostsWriteOutcome outcome = _writer.Execute(new HostsHelperRequest
        {
            Operation = HostsHelperRequest.ApplyOperation,
            ExpectedSha256 = preview.Snapshot.Sha256,
            Mappings = preview.Desired.ToList()
        });
        return Finish(outcome, backup.Path, preview.NewText);
    }

    /// <summary>Puts a previously saved copy of the hosts file back, after backing up the current one.</summary>
    public HostsOperationResult Restore(string backupPath)
    {
        byte[] bytes = _backups.ReadBytes(backupPath);
        HostsSnapshot current = _hosts.Read();
        string restoredText = HostsFileService.Decode(bytes);
        if (restoredText == current.Text)
            return new HostsOperationResult(HostsWriteOutcome.Success, null, current.Sha256);

        HostsBackup backup = _backups.Create(current, _utcNow());
        HostsWriteOutcome outcome = _writer.Execute(new HostsHelperRequest
        {
            Operation = HostsHelperRequest.RestoreOperation,
            ExpectedSha256 = current.Sha256,
            RestoreContentBase64 = Convert.ToBase64String(bytes)
        });
        return Finish(outcome, backup.Path, restoredText);
    }

    private HostsOperationResult Finish(HostsWriteOutcome outcome, string backupPath, string expectedText)
    {
        var result = new HostsOperationResult(outcome, backupPath, null);
        if (!result.Succeeded) return result;

        // Confirm from the unelevated side that the file really holds what was requested.
        HostsSnapshot written = _hosts.Read();
        return written.Text == expectedText
            ? result with { NewSha256 = written.Sha256 }
            : result with { Outcome = HostsWriteOutcome.VerifyFailed };
    }

    /// <summary>Chooses in-process writing when already elevated, otherwise the elevated helper.</summary>
    public static IHostsWriter CreateSystemWriter(HostsFileService hosts, string requestDirectory) =>
        Environment.IsPrivilegedProcess ? new DirectHostsWriter(hosts) : new ElevatedHostsWriter(requestDirectory);
}
