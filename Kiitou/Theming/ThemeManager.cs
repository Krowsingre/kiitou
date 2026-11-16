using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kiitou.Services;

namespace Kiitou.Theming;

/// <summary>
/// Builds the brush set for one of the seven color families in dark or light mode and
/// supplies the matching approved icon.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, (string Accent, string Hover, string OnAccent)> Families = new()
    {
        ["purple"] = ("#7B5CFA", "#8E73FF", "#FFFFFF"),
        ["blue"] = ("#2F7DF6", "#4A90FF", "#FFFFFF"),
        ["green"] = ("#2A9D63", "#35B373", "#FFFFFF"),
        ["gold"] = ("#D4A21E", "#E2B236", "#1A1E29"),
        ["red"] = ("#DC4A5A", "#E9616F", "#FFFFFF"),
        ["silver"] = ("#8490A3", "#97A2B4", "#11141C"),
        ["teal"] = ("#1C9E98", "#27B3AC", "#FFFFFF")
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Bg"] = "#141722", ["Panel"] = "#1B1F2C", ["PanelAlt"] = "#222738", ["Border"] = "#343B50",
        ["Text"] = "#F1F2F6", ["Muted"] = "#A7AEC0", ["Control"] = "#262B3B", ["ControlHover"] = "#323950",
        ["Success"] = "#3CCB7F", ["Warning"] = "#E8B33B", ["Danger"] = "#F0616D"
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Bg"] = "#F3F4F8", ["Panel"] = "#FFFFFF", ["PanelAlt"] = "#ECEEF4", ["Border"] = "#C9CFDC",
        ["Text"] = "#1A1E29", ["Muted"] = "#566074", ["Control"] = "#FFFFFF", ["ControlHover"] = "#E6EAF2",
        ["Success"] = "#1B8F51", ["Warning"] = "#9A6B05", ["Danger"] = "#C93543"
    };

    public static bool IsDark { get; private set; } = true;

    /// <summary>Replaces the application's theme brushes. Controls pick up the change immediately.</summary>
    public static void Apply(string color, string mode)
    {
        string family = Families.ContainsKey(color.ToLowerInvariant()) ? color.ToLowerInvariant() : "purple";
        IsDark = string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase)
            ? !WindowsUsesLightApps()
            : !string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase);
        var (accent, hover, onAccent) = Families[family];

        var theme = new ResourceDictionary();
        foreach (var (key, hex) in IsDark ? Dark : Light) theme[key] = Brush(hex);
        theme["Accent"] = Brush(accent);
        theme["AccentHover"] = Brush(hover);
        theme["AccentText"] = Brush(onAccent);
        theme["AccentSoft"] = Brush(accent, IsDark ? (byte)0x59 : (byte)0x38);

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = theme;
        else merged.Add(theme);
    }

    /// <summary>The Windows "app mode" setting; dark when it cannot be read.</summary>
    public static bool WindowsUsesLightApps()
    {
        try
        {
            object? value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
            return value is int number && number != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static Uri IconUri(string color) =>
        new($"pack://application:,,,/Icons/{ThemeCatalog.IconFileName(color)}", UriKind.Absolute);

    /// <summary>The icon as a window/taskbar icon containing all of its sizes.</summary>
    public static ImageSource WindowIcon(string color) => BitmapFrame.Create(IconUri(color));

    /// <summary>The largest image in the icon, for display inside the window.</summary>
    public static ImageSource LargeIcon(string color)
    {
        var decoder = new IconBitmapDecoder(IconUri(color), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
    }

    public static SolidColorBrush AccentPreview(string color) =>
        Brush(Families.TryGetValue(color.ToLowerInvariant(), out var family) ? family.Accent : "#7B5CFA");

    private static SolidColorBrush Brush(string hex, byte? alpha = null)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (alpha is byte a) color.A = a;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
