using System.Text;

namespace Kiitou.Hosts;

/// <summary>One address-to-hostname mapping written by Kiitou.</summary>
public sealed record HostsMapping(string IpAddress, string Hostname);

/// <summary>An active entry in the hosts file that Kiitou does not manage.</summary>
public sealed record HostsEntry(string IpAddress, string Hostname, int LineNumber);

/// <summary>An unmanaged entry that maps the same hostname and address family to a different address.</summary>
public sealed record HostsConflict(HostsMapping Desired, HostsEntry Existing);

public sealed record HostsAnalysis(
    IReadOnlyList<HostsMapping> ManagedMappings,
    IReadOnlyList<HostsEntry> UnmanagedEntries,
    bool HasManagedBlock,
    bool BlockDamaged);

/// <summary>
/// Reads and rewrites the Kiitou-managed section of hosts-file text.
/// Only marker lines and tagged lines inside the managed section are ever removed;
/// every other line is preserved exactly, including its line ending.
/// </summary>
public static class ManagedHostsBlock
{
    public const string BeginMarker = "# >>> Kiitou managed entries (do not edit inside this block) >>>";
    public const string EndMarker = "# <<< Kiitou managed entries <<<";
    public const string LineTag = "# Kiitou";

    private readonly record struct Line(string Content, string Terminator);

    public static HostsAnalysis Analyze(string text)
    {
        var managed = new List<HostsMapping>();
        var unmanaged = new List<HostsEntry>();
        bool inBlock = false, hasBlock = false, damaged = false;
        int number = 0;

        foreach (Line line in Split(text))
        {
            number++;
            string trimmed = line.Content.Trim();
            if (trimmed == BeginMarker)
            {
                if (inBlock) damaged = true;
                inBlock = true;
                hasBlock = true;
                continue;
            }
            if (trimmed == EndMarker)
            {
                if (!inBlock) damaged = true;
                inBlock = false;
                continue;
            }
            if (inBlock && TryParseManagedLine(trimmed, out HostsMapping? mapping))
            {
                managed.Add(mapping!);
                continue;
            }
            unmanaged.AddRange(ParseEntries(line.Content, number));
        }

        if (inBlock) damaged = true;
        return new HostsAnalysis(managed, unmanaged, hasBlock, damaged);
    }

    /// <summary>
    /// Returns hosts text whose managed section contains exactly <paramref name="mappings"/>.
    /// With no mappings the managed section is removed entirely.
    /// </summary>
    public static string Apply(string text, IReadOnlyList<HostsMapping> mappings)
    {
        string newline = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";
        var output = new StringBuilder(text.Length + 256);
        bool inBlock = false;

        foreach (Line line in Split(text))
        {
            string trimmed = line.Content.Trim();
            if (trimmed == BeginMarker) { inBlock = true; continue; }
            if (trimmed == EndMarker) { inBlock = false; continue; }
            if (inBlock && TryParseManagedLine(trimmed, out _)) continue;
            output.Append(line.Content).Append(line.Terminator);
        }

        if (mappings.Count == 0) return output.ToString();

        if (output.Length > 0 && output[^1] != '\n') output.Append(newline);
        output.Append(BeginMarker).Append(newline);
        foreach (HostsMapping mapping in mappings)
            output.Append(mapping.IpAddress).Append(' ').Append(mapping.Hostname).Append(' ').Append(LineTag).Append(newline);
        output.Append(EndMarker).Append(newline);
        return output.ToString();
    }

    public static IReadOnlyList<HostsConflict> FindConflicts(HostsAnalysis analysis, IReadOnlyList<HostsMapping> desired)
    {
        var conflicts = new List<HostsConflict>();
        foreach (HostsMapping mapping in desired)
        {
            IpFamily? family = IpAddressRules.FamilyOf(mapping.IpAddress);
            foreach (HostsEntry entry in analysis.UnmanagedEntries)
            {
                if (!string.Equals(entry.Hostname, mapping.Hostname, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IpAddressRules.TryNormalize(entry.IpAddress, out string existingIp, out IpFamily existingFamily, out _)) continue;
                if (existingFamily == family && existingIp != mapping.IpAddress)
                    conflicts.Add(new HostsConflict(mapping, entry));
            }
        }
        return conflicts;
    }

    private static bool TryParseManagedLine(string trimmed, out HostsMapping? mapping)
    {
        mapping = null;
        if (!trimmed.EndsWith(LineTag, StringComparison.Ordinal)) return false;
        string[] parts = trimmed[..^LineTag.Length].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        mapping = new HostsMapping(parts[0], parts[1]);
        return true;
    }

    private static IEnumerable<HostsEntry> ParseEntries(string content, int lineNumber)
    {
        int comment = content.IndexOf('#');
        string active = comment >= 0 ? content[..comment] : content;
        string[] parts = active.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) yield break;
        for (int i = 1; i < parts.Length; i++)
            yield return new HostsEntry(parts[0], parts[i], lineNumber);
    }

    private static IEnumerable<Line> Split(string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int lf = text.IndexOf('\n', start);
            if (lf < 0)
            {
                yield return new Line(text[start..], "");
                yield break;
            }
            bool crlf = lf > start && text[lf - 1] == '\r';
            yield return new Line(text[start..(crlf ? lf - 1 : lf)], crlf ? "\r\n" : "\n");
            start = lf + 1;
        }
    }
}
