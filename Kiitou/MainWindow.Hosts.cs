using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Services;
using Kiitou.Session;
using Kiitou.Theming;
using Kiitou.Verification;

namespace Kiitou;

// Hosts File and Settings views.
public partial class MainWindow
{
    private string? _newestBackupPath;

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
            lines.Add(status.State switch
            {
                ApplyState.Active => $"They are exactly the profile \"{_s.Profiles.Active.Name}\".",
                ApplyState.ChangesPending => $"\"{_s.Profiles.Active.Name}\" is applied, but your changes to it are not in the hosts file yet. Use Apply Changes.",
                ApplyState.UnknownMappings => "They match no saved profile. Use Disable Profile to remove them.",
                _ => "No profile is applied."
            });
            if (status.ExternalEditDetected)
                lines.Add("The hosts file has been changed outside Kiitou since Kiitou last wrote it. Those changes are kept.");
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
        // The permanent original is listed first; the most recent backup is the usual one to restore.
        NamedItem? newest = backups.Count > 1 ? backups[1] : backups.FirstOrDefault();
        bool newBackupAppeared = newest?.Path != _newestBackupPath;
        _newestBackupPath = newest?.Path;
        BackupList.SelectedItem = newBackupAppeared
            ? newest
            : backups.FirstOrDefault(b => b.Path == keep) ?? newest;
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
        string? done = null;
        try
        {
            HostsOperationResult result = await Task.Run(operation);
            _busy = false;
            done = ReportHostsOutcome(result, successText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Warn("The hosts file could not be changed.\n\n" + ex.Message);
        }
        finally
        {
            _busy = false;
            _message = done;
            RefreshAll();
        }
    }

    private async void HostsRestore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not NamedItem { Path: { } path } item)
        {
            Info("There is no backup to restore yet. Kiitou makes one each time it changes the hosts file.");
            return;
        }
        if (!Confirm($"Replace the Windows hosts file with this copy?\n\n{item.Name}\n\n" +
                     "The current hosts file is backed up first, so this can be undone."))
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

    // ---- Settings: appearance and system -------------------------------------------------

    private static readonly KeyedChoice[] ModeChoices =
    [
        new() { Key = "auto", Name = "Auto (Follow System)" },
        new() { Key = "dark", Name = "Dark" },
        new() { Key = "light", Name = "Light" }
    ];

    private ComboBox[] _slotService = [];
    private TextBlock[] _slotHint = [];
    private PasswordBox[] _slotKey = [];
    private TextBox[] _slotKeyPlain = [];
    private TextBlock[] _slotKeyHint = [];
    private ToggleButton[] _slotEye = [];
    private Button[] _slotLink = [];
    private TextBlock[] _slotStatus = [];
    private Button[] _slotTest = [];
    private CheckBox[] _slotToggle = [];
    private readonly (string Text, string BrushKey)?[] _slotResult = new (string, string)?[ReputationSettings.KeySlotCount];
    private (string Text, string BrushKey)? _dnsResult;

    private void BuildSettingsChoices()
    {
        ThemeColorBox.ItemsSource = ThemeCatalog.Colors.Select(color => new KeyedChoice
        {
            Key = color,
            Name = char.ToUpperInvariant(color[0]) + color[1..],
            Swatch = ThemeManager.AccentPreview(color)
        }).ToList();
        ModeBox.ItemsSource = ModeChoices;

        _slotService = [SlotService0, SlotService1];
        _slotHint = [SlotHint0, SlotHint1];
        _slotKey = [SlotKey0, SlotKey1];
        _slotKeyPlain = [SlotKeyPlain0, SlotKeyPlain1];
        _slotKeyHint = [SlotKeyHint0, SlotKeyHint1];
        _slotEye = [SlotEye0, SlotEye1];
        _slotLink = [SlotLink0, SlotLink1];
        _slotStatus = [SlotStatus0, SlotStatus1];
        _slotTest = [SlotTest0, SlotTest1];
        _slotToggle = [SlotToggle0, SlotToggle1];

        // The "Paste your API key" hint disappears as soon as something is typed.
        for (int index = 0; index < ReputationSettings.KeySlotCount; index++)
        {
            int slot = index;
            void UpdateHint() => _slotKeyHint[slot].Visibility =
                _slotKey[slot].IsEnabled && TypedKey(slot).Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _slotKey[slot].PasswordChanged += (_, _) => UpdateHint();
            _slotKeyPlain[slot].TextChanged += (_, _) => UpdateHint();
        }
    }

    private void RefreshSettings()
    {
        ThemeColorBox.SelectedItem = (ThemeColorBox.ItemsSource as List<KeyedChoice>)?
            .FirstOrDefault(c => string.Equals(c.Key, _s.Config.Theme.Color, StringComparison.OrdinalIgnoreCase));
        ModeBox.SelectedItem = ModeChoices.FirstOrDefault(
            c => string.Equals(c.Key, _s.Config.Theme.Mode, StringComparison.OrdinalIgnoreCase)) ?? ModeChoices[0];
        TrayOption.IsChecked = _s.Config.Settings.MinimizeToTray;

        Quad9Option.IsChecked = _rep.UseQuad9;
        for (int slot = 0; slot < ReputationSettings.KeySlotCount; slot++) RefreshSlot(slot);
        RefreshCustomDns();

        StoragePath.Text = "Settings and backups: " + _paths.DataDirectory;
        StoragePath.ToolTip = _paths.DataDirectory;

        AboutVersion.Text = "Kiitou " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "");
        AboutText.Text =
            "Kiitou changes the Windows hosts file only when you apply or disable a profile or restore a backup, " +
            "and only with Windows' permission. Its checks show how a connection behaves; they cannot prove that " +
            "an address is authentic or a website safe. Provided as is, without warranty. Use it at your own risk.";
        RefreshUpdates();
    }

    private void ThemeColor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ThemeColorBox.SelectedItem is not KeyedChoice choice) return;
        Dispatcher.InvokeAsync(() => SetTheme(choice.Key, _s.Config.Theme.Mode));
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ModeBox.SelectedItem is not KeyedChoice choice) return;
        Dispatcher.InvokeAsync(() => SetTheme(_s.Config.Theme.Color, choice.Key));
    }

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

    // ---- Settings: reputation checks -----------------------------------------------------

    private static int SlotOf(object sender) => int.Parse((string)((FrameworkElement)sender).Tag);

    /// <summary>The key currently typed in a row, from whichever of its two boxes is showing.</summary>
    private string TypedKey(int slot) =>
        _slotEye[slot].IsChecked == true ? _slotKeyPlain[slot].Text : _slotKey[slot].Password;

    private void RefreshSlot(int slot)
    {
        KeyServiceSlot row = _rep.KeySlots[slot];
        string other = _rep.KeySlots[1 - slot].ServiceId;
        KeyServiceInfo? info = ReputationCatalog.Find(row.ServiceId);

        var choices = new List<KeyedChoice> { new() { Key = "", Name = "Key-based Services" } };
        choices.AddRange(ReputationCatalog.KeyServices
            .Where(s => s.Id != other)
            .Select(s => new KeyedChoice { Key = s.Id, Name = s.Name }));
        _slotService[slot].ItemsSource = choices;
        _slotService[slot].SelectedItem = choices.FirstOrDefault(c => c.Key == row.ServiceId) ?? choices[0];
        // A row that is switched on is locked to its service and key.
        _slotService[slot].IsEnabled = !row.Enabled;
        _slotService[slot].ToolTip = row.Enabled ? "Switch this service off to choose a different one." : null;
        _slotHint[slot].Text = info?.Description ?? "Choose a service to use your own account key.";

        bool editable = info is not null && !row.Enabled;
        _slotKey[slot].IsEnabled = _slotKeyPlain[slot].IsEnabled = editable;
        if (!_slotKey[slot].IsKeyboardFocusWithin && !_slotKeyPlain[slot].IsKeyboardFocusWithin)
        {
            string stored = info is null ? "" : _rep.GetKey(row.ServiceId) ?? "";
            if (_slotKey[slot].Password != stored) _slotKey[slot].Password = stored;
            if (_slotKeyPlain[slot].Text != stored) _slotKeyPlain[slot].Text = stored;
        }
        _slotKeyHint[slot].Visibility = info is not null && TypedKey(slot).Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _slotEye[slot].IsEnabled = info is not null;

        (string Text, string BrushKey)? result = _slotResult[slot];
        _slotLink[slot].Visibility = info is not null && result is null ? Visibility.Visible : Visibility.Collapsed;
        _slotStatus[slot].Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;
        if (result is { } shown)
        {
            _slotStatus[slot].Text = shown.Text;
            _slotStatus[slot].Foreground = B(shown.BrushKey);
        }
        _slotTest[slot].IsEnabled = info is not null;
        _slotToggle[slot].IsChecked = row.Enabled;
        _slotToggle[slot].IsEnabled = info is not null;
        _slotToggle[slot].ToolTip = info?.Name ?? "Choose a service first";
    }

    private void RefreshCustomDns()
    {
        CustomDnsSettings dns = _rep.CustomDns;
        if (!DnsName.IsKeyboardFocusWithin && DnsName.Text != dns.Name) DnsName.Text = dns.Name;
        if (!DnsServer.IsKeyboardFocusWithin && DnsServer.Text != dns.Server) DnsServer.Text = dns.Server;
        DnsName.IsEnabled = DnsServer.IsEnabled = !dns.Enabled;
        DnsNameHint.Visibility = DnsName.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DnsServerHint.Visibility = DnsServer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DnsToggle.IsChecked = dns.Enabled;
        DnsStatus.Visibility = _dnsResult is null ? Visibility.Collapsed : Visibility.Visible;
        if (_dnsResult is { } shown)
        {
            DnsStatus.Text = shown.Text;
            DnsStatus.Foreground = B(shown.BrushKey);
        }
    }

    private void Quad9Option_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = Quad9Option.IsChecked == true;
        Try(() => _rep.SetQuad9(enabled));
    }

    private void SlotService_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ComboBox { SelectedItem: KeyedChoice choice }) return;
        int slot = SlotOf(sender);
        if (choice.Key == _rep.KeySlots[slot].ServiceId) return;
        _slotResult[slot] = null;
        Dispatcher.InvokeAsync(() => Try(() => _rep.SetSlotService(slot, choice.Key)));
    }

    /// <summary>Stores what was typed in a row's key box, if it differs from the stored key.</summary>
    private bool CommitKey(int slot)
    {
        KeyServiceSlot row = _rep.KeySlots[slot];
        if (row.Enabled || row.ServiceId.Length == 0) return true;
        string typed = TypedKey(slot).Trim();
        if (typed == (_rep.GetKey(row.ServiceId) ?? "")) return true;
        try
        {
            _rep.SetKey(slot, typed);
            _slotResult[slot] = null;
            return true;
        }
        catch (Catalog.CatalogException ex)
        {
            Warn(ex.Message);
            return false;
        }
    }

    private void SlotKey_LostFocus(object sender, RoutedEventArgs e)
    {
        int slot = SlotOf(sender);
        // Moving between the row's own masked and plain boxes is not leaving the row.
        if (_slotKey[slot].IsKeyboardFocusWithin || _slotKeyPlain[slot].IsKeyboardFocusWithin) return;
        CommitKey(slot);
        RefreshAll();
    }

    private void SlotEye_Click(object sender, RoutedEventArgs e)
    {
        int slot = SlotOf(sender);
        bool show = _slotEye[slot].IsChecked == true;
        if (show) _slotKeyPlain[slot].Text = _slotKey[slot].Password;
        else _slotKey[slot].Password = _slotKeyPlain[slot].Text;
        _slotKeyPlain[slot].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _slotKey[slot].Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SlotLink_Click(object sender, RoutedEventArgs e)
    {
        if (ReputationCatalog.Find(_rep.KeySlots[SlotOf(sender)].ServiceId) is { } info) OpenLink(info.SignUpUrl);
    }

    private async void SlotTest_Click(object sender, RoutedEventArgs e)
    {
        int slot = SlotOf(sender);
        if (!CommitKey(slot)) return;
        string serviceId = _rep.KeySlots[slot].ServiceId;
        string key = _rep.GetKey(serviceId) ?? "";
        if (ReputationCatalog.Find(serviceId) is not { } info) return;
        if (key.Length == 0)
        {
            Info($"Paste your {info.Name} API key first.");
            return;
        }
        KeyTestResult? outcome = null;
        await RunBusyAsync($"Testing the {info.Name} key...", async token => outcome = await _hub.TestKeyAsync(serviceId, key, token));
        _slotResult[slot] = outcome switch
        {
            KeyTestResult.Works => ("Key works", "Success"),
            KeyTestResult.Rejected => ("Key rejected", "Danger"),
            KeyTestResult.LimitReached => ("Request limit reached", "Warning"),
            KeyTestResult.Unreachable => ("Service not reachable", "Warning"),
            _ => null
        };
        RefreshAll();
    }

    private void SlotToggle_Click(object sender, RoutedEventArgs e)
    {
        int slot = SlotOf(sender);
        bool enabled = _slotToggle[slot].IsChecked == true;
        if (enabled && !CommitKey(slot))
        {
            RefreshAll();
            return;
        }
        Try(() => _rep.SetSlotEnabled(slot, enabled));
    }

    private bool CommitCustomDns()
    {
        CustomDnsSettings dns = _rep.CustomDns;
        if (dns.Enabled || (DnsName.Text.Trim() == dns.Name && DnsServer.Text.Trim() == dns.Server)) return true;
        try
        {
            _rep.SetCustomDns(DnsName.Text, DnsServer.Text);
            _dnsResult = null;
            return true;
        }
        catch (Catalog.CatalogException ex)
        {
            Warn(ex.Message);
            return false;
        }
    }

    private void Dns_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DnsName.IsKeyboardFocusWithin || DnsServer.IsKeyboardFocusWithin) return;
        CommitCustomDns();
        RefreshAll();
    }

    private void DnsHint_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        DnsNameHint.Visibility = DnsName.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DnsServerHint.Visibility = DnsServer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void DnsTest_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitCustomDns()) return;
        string server = _rep.CustomDns.Enabled ? _rep.CustomDns.Server : DnsServer.Text.Trim();
        if (server.Length == 0)
        {
            Info("Enter the DNS server's IP address first, such as 1.1.1.2.");
            return;
        }
        bool? answers = null;
        await RunBusyAsync($"Asking the DNS server at {server}...", async token => answers = await _hub.TestCustomDnsAsync(server, token));
        _dnsResult = answers switch
        {
            true => ("Server answers", "Success"),
            false => ("No answer", "Danger"),
            _ => null
        };
        RefreshAll();
    }

    private void DnsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = DnsToggle.IsChecked == true;
        if (enabled && !CommitCustomDns())
        {
            RefreshAll();
            return;
        }
        Try(() => _rep.SetCustomDnsEnabled(enabled));
    }
}
