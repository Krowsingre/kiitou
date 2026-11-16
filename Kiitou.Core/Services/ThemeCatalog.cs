namespace Kiitou.Services;

public static class ThemeCatalog
{
    public static readonly IReadOnlyList<string> Colors =
        ["purple", "blue", "green", "gold", "red", "silver", "teal"];

    /// <summary>"auto" follows the Windows light/dark setting.</summary>
    public static readonly IReadOnlyList<string> Modes = ["auto", "dark", "light"];

    public static bool IsValid(string? color, string? mode) =>
        color is not null && mode is not null &&
        Colors.Contains(color, StringComparer.OrdinalIgnoreCase) &&
        Modes.Contains(mode, StringComparer.OrdinalIgnoreCase);

    public static string IconFileName(string color)
    {
        if (!Colors.Contains(color, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(color));
        return $"kiitou_{color.ToLowerInvariant()}.ico";
    }
}
