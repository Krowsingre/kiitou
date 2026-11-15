using System.Windows.Media;

namespace Kiitou;

/// <summary>One choice in an address drop-down. A null <see cref="Id"/> means "Not Assigned".</summary>
public sealed class AddressOption
{
    public Guid? Id { get; init; }
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}

/// <summary>A website row on the Home table.</summary>
public sealed class WebsiteRow
{
    public Guid Id { get; init; }
    public string Hostname { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool Enabled { get; init; }
    public List<AddressOption> Options { get; init; } = [];
    public AddressOption? Selected { get; init; }
    public int AddressCount { get; init; }
    public string StatusText { get; init; } = "";
    public string StatusGlyph { get; init; } = "";
    public Brush? StatusBrush { get; init; }
    public override string ToString() => Hostname;
}

/// <summary>A candidate address with its latest verification outcome.</summary>
public sealed class AddressRow
{
    public Guid WebsiteId { get; init; }
    public Guid Id { get; init; }
    public string Hostname { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string Family { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string StatusGlyph { get; init; } = "";
    public Brush? StatusBrush { get; init; }
    public string Checked { get; init; } = "";
    public string Timing { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed class ProfileRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Enabled { get; init; } = "";
    public string LastSaved { get; init; } = "";
    public string State { get; init; } = "";
}

/// <summary>A newly discovered address the user may tick to add as a candidate.</summary>
public sealed class DiscoveredRow
{
    public string IpAddress { get; init; } = "";
    public bool Checked { get; set; } = true;
}

public sealed class NamedItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public override string ToString() => Name;
}
