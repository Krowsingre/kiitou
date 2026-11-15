using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kiitou.Hosts;
using Kiitou.Services;
using Kiitou.Session;
using Kiitou.Theming;
using Kiitou.Verification;

namespace Kiitou;

// Hosts File and Settings views.
public partial class MainWindow
{
    private void RefreshHosts(HostsStatus status)
    {
        var lines = new List<string>();
        if (status.ReadError is not null)
        {
            lines.Add("The Windows hosts file could not be read: " + status.ReadError);
        }
        else
        {
            lines.Add(status.ManagedMappingCount == 0
                ? "Kiitou currently manages no mappings in the Windows hosts file."
                : $"Kiitou currently manages {status.ManagedMappingCount} mapping{(status.ManagedMappingCount == 1 ? "" : "s")} in the Windows hosts file.");
            lines.Add(status.MatchesSelectedProfile
                ? $"This matches the saved profile \"{_s.Profiles.Active.Name}\"."
                : $"This differs from the saved profile \"{_s.Profiles.Active.Name}\". Apply the profile to update it.");
            if (status.ExternalEditDetected)
                lines.Add("The hosts file has been changed outside Kiitou since Kiitou last wrote it. Those changes are kept when you apply.");
            if (status.BlockDamaged)
                lines.Add("Kiitou's section markers have been altered. Only lines tagged \"# Kiitou\" inside the section will be replaced.");
            foreach (HostsConflict conflict in status.Conflicts)
                lines.Add($"Conflict: line {conflict.Existing.LineNumber} maps {conflict.Existing.Hostname} to {conflict.Existing.IpAddress} and takes precedence over Kiitou's {conflict.Desired.IpAddress}.");
        }
        HostsSummary.Text = string.Join("\n", lines);

        try
        {
            HostsText.Text = _s.ReadHostsText();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HostsText.Text = "";
        }

        string? keep = (BackupList.SelectedItem as NamedItem)?.Path;
        var backups = new List<NamedItem>();
        try
        {
            backups = _s.Backups.List().Select(b => new NamedItem
            {
                Path = b.Path,
                Name = (b.IsOriginal ? "Original hosts file (kept permanently)" : "Backup " + Local(b.CreatedUtc)) +
                       $"  ·  {b.SizeBytes:N0} bytes"
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Shown as "no backups"; reading them is retried on the next refresh.
        }
        BackupList.ItemsSource = backups;
        BackupList.SelectedItem = backups.FirstOrDefault(b => b.Path == keep) ?? backups.FirstOrDefault();
    }

    private void HostsRefresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private async Task RunHostsOperationAsync(string description, Func<HostsOperationResult> operation, string successText)
    {
        if (_busy)
        {
            Info("Please wait for the current operation to finish, or cancel it.");
            return;
        }
        _busy = true;
        SetMessage(description);
        try
        {
            HostsOperationResult result = await Task.Run(operation);
            _busy = false;
            ReportHostsOutcome(result, successText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Warn("The hosts file could not be changed.\n\n" + ex.Message);
        }
        finally
        {
            _busy = false;
            _message = null;
            RefreshAll();
        }
    }

    private async void HostsRemove_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Remove every mapping Kiitou manages from the Windows hosts file?\n\n" +
                     "Your saved websites, addresses and profiles are kept, and lines Kiitou does not manage are left alone. " +
                     "A backup is made first. Windows will ask for administrator permission."))
            return;
        await RunHostsOperationAsync("Removing Kiitou mappings from the hosts file...", _s.RemoveAllMappings,
            "Kiitou's mappings were removed from the hosts file.");
    }

    private async void HostsRestore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not NamedItem { Path: { } path } item)
        {
            Info("There is no backup to restore yet. Kiitou makes one each time it changes the hosts file.");
            return;
        }
        if (!Confirm($"Replace the Windows hosts file with this copy?\n\n{item.Name}\n\n" +
                     "The current hosts file is backed up first, so this can be undone. Windows will ask for administrator permission."))
            return;
        await RunHostsOperationAsync("Restoring the hosts file...", () => _s.Restore(path), "The hosts file was restored from the backup.");
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_s.Backups.Directory);
            Process.Start(new ProcessStartInfo(_s.Backups.Directory) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Warn("The backup folder could not be opened.\n\n" + ex.Message);
        }
    }

    // ---- Settings ----------------------------------------------------------------------

    private void BuildColorChoices()
    {
        foreach (string color in ThemeCatalog.Colors)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = ThemeManager.AccentPreview(color),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            label.Children.Add(new TextBlock { Text = char.ToUpperInvariant(color[0]) + color[1..] });
            var choice = new RadioButton { GroupName = "Color", Tag = color, Content = label };
            choice.Click += Color_Click;
            ColorChoices.Children.Add(choice);
        }
    }

    private void RefreshSettings()
    {
        foreach (RadioButton choice in ColorChoices.Children.OfType<RadioButton>())
            choice.IsChecked = string.Equals((string)choice.Tag, _s.Config.Theme.Color, StringComparison.OrdinalIgnoreCase);
        bool light = string.Equals(_s.Config.Theme.Mode, "light", StringComparison.OrdinalIgnoreCase);
        ModeDark.IsChecked = !light;
        ModeLight.IsChecked = light;
        TrayOption.IsChecked = _s.Config.Settings.MinimizeToTray;

        StorageText.Text =
            (_paths.Portable
                ? "Portable: settings are stored beside Kiitou.exe."
                : "Kiitou.exe is in a folder that cannot be written to, so settings are stored in your local application data.") +
            "\nSettings folder: " + _paths.DataDirectory +
            "\nHosts file backups: " + _s.Backups.Directory;

        string version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        AboutText.Text =
            $"Kiitou {version}\n\n" +
            "Kiitou edits the Windows hosts file so that chosen hostnames resolve to addresses you select. " +
            "It changes the hosts file only when you apply a profile, remove its mappings or restore a backup, and only after Windows asks for administrator permission.\n\n" +
            LegitimacyAssessor.Caveat + "\n\n" +
            "Kiitou is provided as is, without warranty of any kind. You use it at your own risk.";
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string color }) return;
        SetTheme(color, _s.Config.Theme.Mode);
    }

    private void Mode_Click(object sender, RoutedEventArgs e) =>
        SetTheme(_s.Config.Theme.Color, ModeLight.IsChecked == true ? "light" : "dark");

    private void SetTheme(string color, string mode) => Try(() =>
    {
        _s.SetTheme(color, mode);
        ApplyTheme();
    });

    private void TrayOption_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = TrayOption.IsChecked == true;
        Try(() =>
        {
            _s.SetMinimizeToTray(enabled);
            _tray.Update(_s.Config.Theme.Color, enabled);
        });
    }
}
