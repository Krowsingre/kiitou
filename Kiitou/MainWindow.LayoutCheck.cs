using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Kiitou.Models;

namespace Kiitou;

// Design verification. Started with "Kiitou.exe --layout-check <report file>", the application
// lays out every view and measures it against the spacing rules, then exits. The build runs it
// so that a control pushed out of its row, cut off, overlapping, or off the grid fails the build.
public partial class MainWindow
{
    /// <summary>The one spacing value used between cards, around the page and between header groups.</summary>
    private const double Gap = 12;
    private const double Tolerance = 1.0;

    private static readonly (string Id, string Name)[] ViewNames =
    [
        (Navigation.Home, "Home"), (Navigation.Websites, "Websites"), (Navigation.Discovery, "Inspector"),
        (Navigation.Profiles, "Profiles"), (Navigation.Network, "Network"), (Navigation.Hosts, "Hosts File"),
        (Navigation.Settings, "Settings")
    ];

    public List<string> RunLayoutCheck()
    {
        var problems = new List<string>();
        foreach (var (id, name) in ViewNames)
        {
            ShowView(id);
            UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            UpdateLayout();
            CheckView(name, problems);
        }
        CheckHeaderAndSettings(problems);
        ShowView(HomeView);
        return problems;
    }

    private Rect BoundsOf(FrameworkElement element) =>
        element.TransformToAncestor(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static string Describe(FrameworkElement element)
    {
        string own = element is TextBlock text && text.Text.Length > 0
            ? $"text \"{(text.Text.Length > 40 ? text.Text[..40] + "..." : text.Text)}\""
            : element.GetType().Name;
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name.Length: > 0 } named)
                return ReferenceEquals(named, element) ? named.Name : $"{own} in {named.Name}";
        }
        return own;
    }

    /// <summary>Everything currently visible, except the insides of controls that scroll or trim by design.</summary>
    private static void Collect(DependencyObject parent, List<FrameworkElement> found)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is UIElement { Visibility: not Visibility.Visible }) continue;
            if (child is FrameworkElement element)
            {
                found.Add(element);
                // Tables, lists and text boxes scroll their content; drop-downs trim long choices.
                if (element is DataGrid or TextBoxBase or PasswordBox or ComboBox or ScrollBar) continue;
                if (element is ListBox && element.Name != "Nav") continue;
            }
            Collect(child, found);
        }
    }

    private void CheckView(string view, List<string> problems)
    {
        var elements = new List<FrameworkElement>();
        Collect(this, elements);
        var window = new Rect(0, 0, ((FrameworkElement)Content).ActualWidth, ((FrameworkElement)Content).ActualHeight);

        foreach (FrameworkElement element in elements)
        {
            if (element.ActualWidth < 1 || element.ActualHeight < 1) continue;

            // 1. Nothing is cut off because its row or column is too small for it.
            bool trimsByDesign = element is TextBlock { TextTrimming: not TextTrimming.None };
            if (!trimsByDesign && LayoutInformation.GetLayoutClip(element) is { } clip &&
                (element.RenderSize.Width - clip.Bounds.Width > Tolerance || element.RenderSize.Height - clip.Bounds.Height > Tolerance))
            {
                problems.Add($"{view}: {Describe(element)} is cut off; it needs " +
                             $"{element.RenderSize.Width:0} x {element.RenderSize.Height:0} but has {clip.Bounds.Width:0} x {clip.Bounds.Height:0}.");
            }

            // 2. Nothing sits outside the window.
            if (element is Button or ComboBox or TextBoxBase or PasswordBox or CheckBox or TextBlock or DataGrid)
            {
                Rect bounds = BoundsOf(element);
                if (bounds.Left < -Tolerance || bounds.Top < -Tolerance ||
                    bounds.Right > window.Right + Tolerance || bounds.Bottom > window.Bottom + Tolerance)
                    problems.Add($"{view}: {Describe(element)} extends outside the window.");
            }
        }

        // 3. Buttons that belong to one row stay on one line.
        var buttons = elements.OfType<ButtonBase>().Where(b => b.ActualWidth >= 1).ToList();
        foreach (var row in buttons.Where(b => BarName().IsMatch(b.Name)).GroupBy(b => BarName().Match(b.Name).Value))
        {
            var tops = row.Select(b => Math.Round(BoundsOf(b).Top)).Distinct().ToList();
            if (tops.Count > 1)
                problems.Add($"{view}: the {row.Key} row is not on one line ({string.Join(", ", row.Select(b => b.Name))}).");
        }

        // 4. No two buttons overlap.
        var named = buttons.Where(b => b is Button or ToggleButton).ToList();
        for (int i = 0; i < named.Count; i++)
        {
            for (int j = i + 1; j < named.Count; j++)
            {
                if (named[i].IsAncestorOf(named[j]) || named[j].IsAncestorOf(named[i])) continue;
                Rect overlap = Rect.Intersect(BoundsOf(named[i]), BoundsOf(named[j]));
                if (!overlap.IsEmpty && overlap.Width > Tolerance && overlap.Height > Tolerance)
                    problems.Add($"{view}: {Describe(named[i])} overlaps {Describe(named[j])}.");
            }
        }
    }

    /// <summary>The spacing rules for the header and the Settings cards.</summary>
    private void CheckHeaderAndSettings(List<string> problems)
    {
        ShowView(SettingsView);
        UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        UpdateLayout();

        Rect content = BoundsOf(ContentHost);
        Rect appearance = BoundsOf(AppearanceCard), system = BoundsOf(SystemCard), reputation = BoundsOf(ReputationCard);
        Rect storage = BoundsOf(UpdatesCard), about = BoundsOf(AboutCard);
        Rect label = BoundsOf(ProfileLabel), picker = BoundsOf(ProfilePicker), apply = BoundsOf(ApplyButton), badge = BoundsOf(StatePill);
        Rect header = BoundsOf(HeaderBar);

        void Expect(string what, double actual, double expected)
        {
            if (Math.Abs(actual - expected) > Tolerance)
                problems.Add($"Spacing: {what} is {actual:0.#}, expected {expected:0.#}.");
        }

        // Around and between the Settings cards: the same gap everywhere.
        Expect("space above the Settings cards", appearance.Top - header.Bottom, Gap);
        Expect("page margin on the left", ContentHost.Margin.Left, Gap);
        Expect("page margin on the right", ContentHost.Margin.Right, Gap);
        Expect("page margin at the bottom", ContentHost.Margin.Bottom, Gap);
        Expect("left edge of Appearance against the page edge", appearance.Left, content.Left);
        Expect("left edge of Updates against the page edge", storage.Left, content.Left);
        Expect("space between Appearance and System", system.Left - appearance.Right, Gap);
        Expect("space between the first and second card rows", reputation.Top - appearance.Bottom, Gap);
        Expect("space between the second and third card rows", storage.Top - reputation.Bottom, Gap);
        Expect("space between Updates and About", about.Left - storage.Right, Gap);
        Expect("right edge of System against the page edge", system.Right, content.Right);
        Expect("right edge of About against the page edge", about.Right, content.Right);
        Expect("bottom of Updates against the page edge", storage.Bottom, content.Bottom);
        Expect("bottom of About against the page edge", about.Bottom, content.Bottom);
        Expect("space below the Settings cards", BoundsOf(FooterBar).Top - about.Bottom, Gap);
        Expect("height of Appearance against System", appearance.Height, system.Height);

        // Header: the drop-down ends where the left card ends, the badge where the cards end.
        Expect("gap between \"Profile:\" and its drop-down", picker.Left - label.Right, 8);
        Expect("right edge of the profile drop-down against the Appearance card", picker.Right, appearance.Right);
        Expect("gap between the header button and the badge", badge.Left - apply.Right, Gap);
        Expect("right edge of the badge against the cards", badge.Right, system.Right);
    }

    [GeneratedRegex("^[A-Za-z]+?Bar")]
    private static partial Regex BarName();
}
