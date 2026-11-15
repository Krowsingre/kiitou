using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Kiitou.Catalog;
using Kiitou.Hosts;
using Kiitou.Models;
using Kiitou.Services;
using Kiitou.Session;
using Kiitou.Theming;
using Kiitou.Verification;

namespace Kiitou;

public partial class MainWindow : Window
{
    private static readonly AddressOption NotAssigned = new() { Id = null, Label = "— Not Assigned —" };

    private readonly KiitouSession _s;
    private readonly StoragePaths _paths;
    private readonly TrayIcon _tray;
    private bool _ready;
    private bool _refreshing;
    private bool _busy;
    private CancellationTokenSource? _cancel;
    private string? _message;

    public MainWindow(KiitouSession session, StoragePaths paths)
    {
        _s = session;
        _paths = paths;
        InitializeComponent();
        _tray = new TrayIcon(this);
        BuildColorChoices();
        VerifyCaveat.Text = LegitimacyAssessor.Caveat;
        ApplyTheme();
        _ready = true;
        RefreshAll();

        SourceInitialized += (_, _) => ApplyTitleBar();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _s.Config.Settings.MinimizeToTray) Hide();
        };
        Closed += (_, _) =>
        {
            _cancel?.Cancel();
            _tray.Dispose();
        };
    }

    // ---- Shared helpers ----------------------------------------------------------------

    private Brush B(string key) => (Brush)FindResource(key);

    private static string Local(DateTime? utc) =>
        utc is DateTime value
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "Never";

    private (string Text, string Glyph, Brush Brush) Display(AddressAssessment? assessment)
    {
        string stale = LegitimacyAssessor.IsStale(assessment, DateTime.UtcNow) ? " (stale)" : "";
        VerificationStatus status = assessment?.Status ?? VerificationStatus.NotTested;
        string text = LegitimacyAssessor.StatusText(status) + stale;
        return status switch
        {
            VerificationStatus.ConnectionVerified => (text, "", B("Success")),
            VerificationStatus.ConnectionWorksVerificationIncomplete => (text, "", B("Warning")),
            VerificationStatus.FailedOrUnsafe => (text, "", B("Danger")),
            _ => (text, "", B("Muted"))
        };
    }

    private void Info(string text) => MessageBox.Show(this, text, "Kiitou", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Warn(string text) => MessageBox.Show(this, text, "Kiitou", MessageBoxButton.OK, MessageBoxImage.Warning);

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, "Kiitou", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void SetMessage(string? text)
    {
        _message = text;
        RefreshFooter(null);
    }

    /// <summary>Runs a change, shows any refusal to the user, and refreshes every view.</summary>
    private void Try(Action change)
    {
        if (_busy)
        {
            Info("Please wait for the current operation to finish, or cancel it.");
            return;
        }
        try
        {
            change();
        }
        catch (CatalogException ex)
        {
            Warn(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Warn("The change could not be saved.\n\n" + ex.Message);
        }
        RefreshAll();
    }

    /// <summary>Runs a cancellable background operation while keeping the window responsive.</summary>
    private async Task RunBusyAsync(string description, Func<CancellationToken, Task> work)
    {
        if (_busy)
        {
            Info("Please wait for the current operation to finish, or cancel it.");
            return;
        }
        _busy = true;
        _cancel = new CancellationTokenSource();
        CancelButton.IsEnabled = CancelButton2.IsEnabled = true;
        SetMessage(description);
        string? finished = null;
        try
        {
            await work(_cancel.Token);
        }
        catch (OperationCanceledException)
        {
            finished = "Cancelled.";
        }
        catch (CatalogException ex)
        {
            Warn(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Warn(ex.Message);
        }
        finally
        {
            _busy = false;
            _cancel.Dispose();
            _cancel = null;
            CancelButton.IsEnabled = CancelButton2.IsEnabled = false;
            _message = finished;
            RefreshAll();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    // ---- Refresh -----------------------------------------------------------------------

    private void RefreshAll()
    {
        if (!_ready) return;
        _refreshing = true;
        try
        {
            HostsStatus status = _s.GetHostsStatus();
            RefreshHeader(status);
            RefreshHome();
            RefreshWebsites();
            RefreshProfiles();
            RefreshDiscovery();
            RefreshVerification();
            RefreshHosts(status);
            RefreshSettings();
            RefreshFooter(status);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshHeader(HostsStatus status)
    {
        var items = _s.Profiles.Profiles.Select(p => new NamedItem { Id = p.Id, Name = p.Name }).ToList();
        ProfilePicker.ItemsSource = items;
        ProfilePicker.SelectedItem = items.FirstOrDefault(i => i.Id == _s.Config.ActiveProfileId);

        bool active = status.MatchesSelectedProfile && status.ManagedMappingCount > 0;
        bool nothing = status.MatchesSelectedProfile && status.ManagedMappingCount == 0;
        StateText.Text = active ? "Profile Active" : nothing ? "No Mappings" : "Not Applied";
        StateGlyph.Text = active ? "" : nothing ? "" : "";
        Brush brush = active ? B("Success") : nothing ? B("Muted") : B("Warning");
        StateText.Foreground = StateGlyph.Foreground = brush;
        StatePill.ToolTip = active
            ? "The hosts file contains exactly this profile's saved mappings."
            : nothing
                ? "This profile has no enabled mappings and Kiitou manages none in the hosts file."
                : "The hosts file does not match this profile's saved mappings. Use Apply Selected Profile.";
    }

    private void RefreshHome()
    {
        Guid? selectedId = (HomeGrid.SelectedItem as WebsiteRow)?.Id;
        var rows = new List<WebsiteRow>();
        foreach (WebsiteEntry website in _s.Catalog.Websites)
        {
            ProfileWebsiteSelection selection = _s.Working.View(website.Id);
            var options = new List<AddressOption> { NotAssigned };
            options.AddRange(website.Addresses.Select(a => new AddressOption
            {
                Id = a.Id,
                Label = $"{a.IpAddress} ({IpAddressRules.FamilyOf(a.IpAddress)})"
            }));

            AddressCandidate? v4 = website.Addresses.FirstOrDefault(a => a.Id == selection.SelectedIpv4Id);
            AddressCandidate? v6 = website.Addresses.FirstOrDefault(a => a.Id == selection.SelectedIpv6Id);
            AddressCandidate? shown = v4 ?? v6;
            string summary = !selection.Enabled
                ? "Disabled"
                : shown is null
                    ? "Enabled, but no address is assigned"
                    : v4 is not null && v6 is not null ? "Enabled · also IPv6 " + v6.IpAddress : "Enabled";
            var (text, glyph, brush) = Display(shown?.LastAssessment);

            rows.Add(new WebsiteRow
            {
                Id = website.Id,
                Hostname = website.Hostname,
                Summary = summary,
                Enabled = selection.Enabled,
                Options = options,
                Selected = options.FirstOrDefault(o => o.Id == shown?.Id) ?? NotAssigned,
                AddressCount = website.Addresses.Count,
                StatusText = text,
                StatusGlyph = glyph,
                StatusBrush = brush
            });
        }
        HomeGrid.ItemsSource = rows;
        HomeGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);

        MappingProfile profile = _s.Profiles.Active;
        CardProfileName.Text = profile.Name;
        bool unsaved = _s.HasUnsavedChanges;
        CardBadgeText.Text = unsaved ? "Unsaved changes" : "Selected";
        CardBadgeText.Foreground = CardBadge.BorderBrush = unsaved ? B("Warning") : B("Success");
        CardCounts.Text = $"Websites: {_s.Catalog.Websites.Count} (Enabled: {_s.Working.EnabledCount})";
        CardSaved.Text = "Last Saved: " + Local(profile.LastSavedUtc);
        SaveCurrentButton.IsEnabled = unsaved;
    }

    private void RefreshFooter(HostsStatus? status)
    {
        if (!_ready) return;
        if (status is not null)
        {
            bool problem = status.ReadError is not null || status.BlockDamaged;
            FooterHosts.Text = status.ReadError is not null
                ? "Could not be read"
                : status.ManagedMappingCount == 0
                    ? "No Kiitou mappings"
                    : $"Managed ({status.ManagedMappingCount} active mapping{(status.ManagedMappingCount == 1 ? "" : "s")})";
            FooterGlyph.Text = problem ? "" : "";
            FooterGlyph.Foreground = problem ? B("Warning") : status.ManagedMappingCount > 0 ? B("Success") : B("Muted");
            FooterApplied.Text = "Last Applied: " + Local(_s.Config.Hosts.LastAppliedUtc);
        }
        FooterMessage.Text = _message ?? "Profile: " + _s.Profiles.Active.Name;
    }

    // ---- Navigation --------------------------------------------------------------------

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ShowView(Nav.SelectedIndex);
    }

    private void ShowView(int index)
    {
        UIElement[] views = [ViewHome, ViewWebsites, ViewProfiles, ViewDiscovery, ViewVerification, ViewHosts, ViewSettings];
        for (int i = 0; i < views.Length; i++)
            views[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        if (Nav.SelectedIndex != index) Nav.SelectedIndex = index;
        if (!_busy) _message = null;
        RefreshAll();
    }

    // ---- Header ------------------------------------------------------------------------

    private void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ProfilePicker.SelectedItem is not NamedItem item || item.Id == _s.Config.ActiveProfileId) return;
        Dispatcher.InvokeAsync(() => SwitchTo(item.Id));
    }

    private void SwitchTo(Guid profileId)
    {
        if (_s.HasUnsavedChanges &&
            !Confirm($"Unsaved changes to \"{_s.Profiles.Active.Name}\" will be discarded.\n\nSwitch profile anyway?"))
        {
            RefreshAll();
            return;
        }
        Try(() => _s.SwitchProfile(profileId));
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Info("Please wait for the current operation to finish, or cancel it.");
            return;
        }
        try
        {
            if (_s.HasUnsavedChanges)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    $"You have unsaved changes. Apply uses the profile as it was last saved.\n\nSave the changes to \"{_s.Profiles.Active.Name}\" first?",
                    "Kiitou", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) _s.SaveToCurrentProfile();
            }

            HostsApplyPreview preview = _s.PreviewApply();
            if (!preview.ChangeNeeded)
            {
                Info("The hosts file already contains exactly this profile's mappings. Nothing needs to change.");
                return;
            }

            var notes = new List<string>
            {
                $"Kiitou will write {preview.Desired.Count} mapping{(preview.Desired.Count == 1 ? "" : "s")} for \"{_s.Profiles.Active.Name}\" to the Windows hosts file.",
                "A backup is made first. Lines Kiitou does not manage are left exactly as they are."
            };
            if (preview.ExternalEditDetected)
                notes.Add("NOTE: The hosts file was changed outside Kiitou since Kiitou last wrote it. Those changes will be kept.");
            if (preview.Analysis.BlockDamaged)
                notes.Add("NOTE: Kiitou's section markers in the hosts file were altered. Only Kiitou's own tagged lines will be replaced.");
            foreach (HostsConflict conflict in preview.Conflicts)
                notes.Add($"CONFLICT: line {conflict.Existing.LineNumber} already maps {conflict.Existing.Hostname} to {conflict.Existing.IpAddress}. Windows uses the first entry, so that line will take precedence over {conflict.Desired.IpAddress} until you remove it yourself.");
            foreach (SelectionIssue issue in new SelectionEditor(_s.Config, _s.Profiles.Active.Selections).FindIssues())
                notes.Add("NOTE: " + issue.Message);
            notes.Add("Windows will ask for administrator permission. Continue?");
            if (!Confirm(string.Join("\n\n", notes))) return;

            _busy = true;
            SetMessage("Applying profile to the hosts file...");
            HostsOperationResult result = await Task.Run(() => _s.Apply(preview));
            _busy = false;
            ReportHostsOutcome(result, "The profile was applied.");
        }
        catch (CatalogException ex)
        {
            Warn(ex.Message);
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

    private void ReportHostsOutcome(HostsOperationResult result, string successText)
    {
        switch (result.Outcome)
        {
            case HostsWriteOutcome.Success:
                Info(successText);
                break;
            case HostsWriteOutcome.SuccessFlushFailed:
                Warn(successText + "\n\nThe Windows DNS cache could not be cleared, so the change may take a few minutes to take effect. Restarting your browser usually helps.");
                break;
            case HostsWriteOutcome.Cancelled:
                Info("Administrator permission was not given. The hosts file was not changed.");
                break;
            case HostsWriteOutcome.HostsChanged:
                Warn("The hosts file was changed by something else while Kiitou was preparing. Nothing was written. Please try again.");
                break;
            case HostsWriteOutcome.VerifyFailed:
                Warn("The hosts file does not contain what Kiitou tried to write. Security software may have blocked or reverted the change. A backup of the previous file is in Kiitou's backup folder.");
                break;
            default:
                Warn("The hosts file could not be written. Security software may be blocking changes to it. Nothing was changed.");
                break;
        }
    }

    // ---- Home --------------------------------------------------------------------------

    private WebsiteRow? HomeSelection => HomeGrid.SelectedItem as WebsiteRow;

    private void AddWebsite_Click(object sender, RoutedEventArgs e)
    {
        string? hostname = InputDialog.Prompt(this, "Add Website", "Website hostname:", "", "For example: example.com");
        if (hostname is null) return;
        Try(() =>
        {
            WebsiteEntry added = _s.AddWebsite(hostname);
            _pendingWebsiteSelection = added.Id;
        });
    }

    private void HomeEdit_Click(object sender, RoutedEventArgs e)
    {
        if (HomeSelection is not { } row)
        {
            Info("Select a website in the table first.");
            return;
        }
        OpenWebsite(row.Id);
    }

    private void HomeRemove_Click(object sender, RoutedEventArgs e)
    {
        if (HomeSelection is not { } row)
        {
            Info("Select a website in the table first.");
            return;
        }
        RemoveWebsite(row.Id, row.Hostname);
    }

    private void RemoveWebsite(Guid websiteId, string hostname)
    {
        if (!Confirm($"Remove {hostname} and all of its candidate addresses from Kiitou?\n\nIt will also be removed from every profile. The hosts file is not changed until you apply a profile."))
            return;
        Try(() => _s.RemoveWebsite(websiteId));
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e) => Try(() => _s.Working.EnableAll());

    private void DisableAll_Click(object sender, RoutedEventArgs e) => Try(() => _s.Working.DisableAll());

    private void HomeEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: WebsiteRow row } box) return;
        bool enabled = box.IsChecked == true;
        Dispatcher.InvokeAsync(() => Try(() => _s.Working.SetEnabled(row.Id, enabled)));
    }

    private void HomeAddress_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ComboBox { DataContext: WebsiteRow row } combo) return;
        if (!combo.IsDropDownOpen && !combo.IsKeyboardFocusWithin) return;
        if (combo.SelectedItem is not AddressOption option) return;
        Dispatcher.InvokeAsync(() => Try(() => ChooseAddress(row.Id, option.Id)));
    }

    /// <summary>Home's single drop-down: picks the address for its own family, or clears both families.</summary>
    private void ChooseAddress(Guid websiteId, Guid? addressId)
    {
        if (addressId is not Guid id)
        {
            _s.Working.SelectAddress(websiteId, IpFamily.IPv4, null);
            _s.Working.SelectAddress(websiteId, IpFamily.IPv6, null);
            return;
        }
        AddressCandidate address = _s.Catalog.Get(websiteId).Addresses.First(a => a.Id == id);
        _s.Working.SelectAddress(websiteId, WebsiteCatalog.FamilyOf(address), id);
    }

    private void HomeMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WebsiteRow row }) OpenWebsite(row.Id);
    }

    private void HomeDiscover_Click(object sender, RoutedEventArgs e) => OpenDiscovery(HomeSelection?.Id, discover: true);

    private void HomeTest_Click(object sender, RoutedEventArgs e) => OpenDiscovery(HomeSelection?.Id, discover: false);

    private void SaveCurrent_Click(object sender, RoutedEventArgs e) => Try(() => _s.SaveToCurrentProfile());

    private void SaveAsNew_Click(object sender, RoutedEventArgs e)
    {
        string? name = InputDialog.Prompt(this, "Save as New Profile", "Name for the new profile:",
            _s.Profiles.UniqueName(_s.Profiles.Active.Name + " (copy)"));
        if (name is null) return;
        Try(() => _s.SaveAsNewProfile(name));
    }

    // ---- Window chrome -----------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void ApplyTitleBar()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = ThemeManager.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private void ApplyTheme()
    {
        string color = _s.Config.Theme.Color;
        ThemeManager.Apply(color, _s.Config.Theme.Mode);
        Icon = ThemeManager.WindowIcon(color);
        HeaderIcon.Source = ThemeManager.LargeIcon(color);
        _tray.Update(color, _s.Config.Settings.MinimizeToTray);
        ApplyTitleBar();
    }
}
