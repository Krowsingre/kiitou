using System.Diagnostics;
using System.Text.Json;

namespace Kiitou.Hosts;

public enum HostsWriteOutcome
{
    Success = 0,
    InvalidRequest = 2,
    HostsChanged = 3,
    WriteFailed = 4,
    VerifyFailed = 5,
    /// <summary>The hosts file was written and verified, but the DNS cache could not be flushed.</summary>
    SuccessFlushFailed = 6,
    /// <summary>The user declined the Windows elevation prompt; nothing was changed.</summary>
    Cancelled = 100,
    /// <summary>The elevated helper could not be started or ended abnormally.</summary>
    HelperFailed = 101
}

/// <summary>The only instruction the privileged helper accepts.</summary>
public sealed class HostsHelperRequest
{
    public const string ApplyOperation = "apply";
    public const string RestoreOperation = "restore";

    public string Operation { get; set; } = ApplyOperation;
    /// <summary>SHA-256 of the hosts file the request was planned against.</summary>
    public string ExpectedSha256 { get; set; } = "";
    /// <summary>For apply: the complete set of managed mappings to leave in the file.</summary>
    public List<HostsMapping> Mappings { get; set; } = [];
    /// <summary>For restore: the exact bytes to restore, Base64 encoded.</summary>
    public string? RestoreContentBase64 { get; set; }
}

/// <summary>
/// The privileged part of Kiitou. It performs exactly one validated change to the hosts file
/// and nothing else, whether it runs in-process or as the elevated copy of Kiitou.exe.
/// </summary>
public static class HostsHelper
{
    public const string Switch = "--hosts-helper";
    private const int MaximumMappings = 5000;
    private const int MaximumRestoreBytes = 5 * 1024 * 1024;

    /// <summary>Entry point for the elevated process: <c>Kiitou.exe --hosts-helper &lt;request file&gt;</c>.</summary>
    public static int Run(string[] args)
    {
        if (args.Length != 2 || args[0] != Switch) return (int)HostsWriteOutcome.InvalidRequest;
        HostsHelperRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<HostsHelperRequest>(File.ReadAllText(args[1]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (int)HostsWriteOutcome.InvalidRequest;
        }
        if (request is null) return (int)HostsWriteOutcome.InvalidRequest;
        return (int)Execute(request, new HostsFileService(HostsFileService.SystemHostsPath), DnsCache.Flush);
    }

    public static HostsWriteOutcome Execute(HostsHelperRequest request, HostsFileService hosts, Func<bool> flushDns)
    {
        if (!TryBuildNewText(request, hosts, out string? newText, out HostsWriteOutcome failure))
            return failure;

        try
        {
            HostsSnapshot current = hosts.Read();
            if (!string.Equals(current.Sha256, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                return HostsWriteOutcome.HostsChanged;
            if (newText == current.Text) return HostsWriteOutcome.Success;

            hosts.Write(newText!, request.ExpectedSha256);
            if (!hosts.Verify(newText!)) return HostsWriteOutcome.VerifyFailed;
        }
        catch (HostsChangedException)
        {
            return HostsWriteOutcome.HostsChanged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HostsWriteOutcome.WriteFailed;
        }

        return flushDns() ? HostsWriteOutcome.Success : HostsWriteOutcome.SuccessFlushFailed;
    }

    private static bool TryBuildNewText(
        HostsHelperRequest request, HostsFileService hosts, out string? newText, out HostsWriteOutcome failure)
    {
        newText = null;
        failure = HostsWriteOutcome.InvalidRequest;
        if (request.ExpectedSha256 is not { Length: 64 }) return false;

        switch (request.Operation)
        {
            case HostsHelperRequest.ApplyOperation:
                if (request.Mappings is null || request.Mappings.Count > MaximumMappings) return false;
                if (request.Mappings.Any(m => m is null ||
                        !IpAddressRules.IsNormalized(m.IpAddress) || !HostnameRules.IsNormalized(m.Hostname)))
                    return false;
                try
                {
                    newText = ManagedHostsBlock.Apply(hosts.Read().Text, request.Mappings);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failure = HostsWriteOutcome.WriteFailed;
                    return false;
                }
                return true;

            case HostsHelperRequest.RestoreOperation:
                if (string.IsNullOrEmpty(request.RestoreContentBase64)) return false;
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(request.RestoreContentBase64);
                }
                catch (FormatException)
                {
                    return false;
                }
                if (bytes.Length > MaximumRestoreBytes) return false;
                newText = HostsFileService.Decode(bytes);
                return true;

            default:
                return false;
        }
    }
}

/// <summary>Clears the Windows DNS resolver cache so changed mappings take effect immediately.</summary>
public static class DnsCache
{
    public static bool Flush()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "ipconfig.exe"),
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
