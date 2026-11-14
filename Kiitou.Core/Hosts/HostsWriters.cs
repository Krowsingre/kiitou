using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Kiitou.Hosts;

/// <summary>Carries out one validated hosts-file change.</summary>
public interface IHostsWriter
{
    HostsWriteOutcome Execute(HostsHelperRequest request);
}

/// <summary>Writes in the current process. Used when Kiitou is already elevated, and by tests.</summary>
public sealed class DirectHostsWriter : IHostsWriter
{
    private readonly HostsFileService _hosts;
    private readonly Func<bool> _flushDns;

    public DirectHostsWriter(HostsFileService hosts, Func<bool>? flushDns = null)
    {
        _hosts = hosts;
        _flushDns = flushDns ?? DnsCache.Flush;
    }

    public HostsWriteOutcome Execute(HostsHelperRequest request) => HostsHelper.Execute(request, _hosts, _flushDns);
}

/// <summary>
/// Starts an elevated copy of Kiitou.exe for the single hosts-file write. Windows shows its
/// elevation prompt; declining it leaves the hosts file untouched.
/// </summary>
public sealed class ElevatedHostsWriter : IHostsWriter
{
    private const int ErrorCancelled = 1223;
    private readonly string _requestDirectory;

    public ElevatedHostsWriter(string requestDirectory) => _requestDirectory = requestDirectory;

    public HostsWriteOutcome Execute(HostsHelperRequest request)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return HostsWriteOutcome.HelperFailed;

        Directory.CreateDirectory(_requestDirectory);
        string requestFile = Path.Combine(_requestDirectory, $".kiitou-request-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(requestFile, JsonSerializer.Serialize(request));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"{HostsHelper.Switch} \"{requestFile}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return HostsWriteOutcome.HelperFailed;
            process.WaitForExit();
            return Enum.IsDefined(typeof(HostsWriteOutcome), process.ExitCode)
                ? (HostsWriteOutcome)process.ExitCode
                : HostsWriteOutcome.HelperFailed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return HostsWriteOutcome.Cancelled;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return HostsWriteOutcome.HelperFailed;
        }
        finally
        {
            try { if (File.Exists(requestFile)) File.Delete(requestFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
