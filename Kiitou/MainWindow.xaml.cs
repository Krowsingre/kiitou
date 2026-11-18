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
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Session;
using Kiitou.Theming;
using Kiitou.Verification;

namespace Kiitou;

public partial class MainWindow : Window
{
    private static readonly AddressOption NotAssigned = new() { Id = null, Label = "— Not Assigned —" };

    private readonly KiitouSession _s;
    private readonly ReputationSettings _rep;
    private readonly ReputationHub _hub;
    private readonly NetworkSettings _net;
    private readonly StoragePaths _paths;
    private readonly TrayIcon _tray;
    private bool _ready;
    private bool _refreshing;
    private bool _busy;
    private CancellationTokenSource? _cancel;
    private string? _message;

    public MainWindow(KiitouSession session, StoragePaths paths, ReputationSettings reputation, ReputationHub hub, NetworkSettings network)
    {
        _net = network;
        _s = session;
        _paths = paths;
        _rep = reputation;
        _hub = hub;
        InitializeComponent();
        _tray = new TrayIcon(this);
        BuildSettingsChoices();
        FilterBox.ItemsSource = HomeFilters;
        FilterBox.SelectedIndex = 0;
        TestCaveat.Text = "Tests never touch the hosts file and never select or apply an address. " + LegitimacyAssessor.Caveat;
        ApplyTheme();
        ApplyNavOrder();
        RestoreWindowPosition();
        StartUpdates();
        _ready = true;
        RefreshAll();

        SourceInitialized += (_, _) => ApplyTitleBar();
        Loaded += (_, _) => AlignHeader();
        ContentHost.SizeChanged += (_, _) => AlignHeader();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _s.Config.Settings.MinimizeToTray) Hide();
        };
        Closing += (_, _) => SaveWindowPosition();
        Closed += (_, _) =>
        {
            _cancel?.Cancel();
            _tray.Dispose();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
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
        TestBarCancel.IsEnabled = true;
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
            TestBarCancel.IsEnabled = false;
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
            RefreshHosts(status);
            RefreshNetwork();
            RefreshSettings();
            RefreshFooter(status);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Sizes the header's right-hand group to the width of the right-hand Settings card, which puts
    /// the drop-down's right edge on the left card's right edge and keeps every gap at 12.
    /// </summary>
    private void AlignHeader()
    {
        double half = (ContentHost.ActualWidth - 12) / 2;
        if (half > 0) HeaderRightHalf.Width = new GridLength(half);
    }

    /// <summary>In Auto mode the theme follows Windows when its light/dark setting changes.</summary>
    private void OnWindowsPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (!string.Equals(_s.Config.Theme.Mode, "auto", StringComparison.OrdinalIgnoreCase)) return;
            bool wasDark = ThemeManager.IsDark;
            ApplyTheme();
            if (wasDark != ThemeManager.IsDark) RefreshAll();
        });
    }

    private ApplyState _state;

    private void RefreshHeader(HostsStatus status)
    {
        _state = status.State;
        bool locked = status.ProfileLocked;
        var items = _s.Profiles.Profiles.Select(p => new NamedItem
        {
            Id = p.Id,
            Name = locked && p.Id == _s.Config.ActiveProfileId ? p.Name + " (Active)" : p.Name
        }).ToList();
        ProfilePicker.ItemsSource = items;
        ProfilePicker.SelectedItem = items.FirstOrDefault(i => i.Id == _s.Config.ActiveProfileId);
        ProfilePicker.IsEnabled = !locked;
        ProfilePicker.ToolTip = locked
            ? "This profile is applied. Disable it to choose another."
            : "Choose the profile to edit and apply";

        // One button: apply the profile, disable it, or apply edits made while it is active.
        (string text, string glyph, bool accent, string tip) = _state switch
        {
            ApplyState.Active => ("Disable Profile", "\uE71A", false,
                "Remove this profile's mappings from the Windows hosts file and unlock the profile list"),
            ApplyState.ChangesPending => ("Apply Changes", "\uE768", true,
                "Save your changes and update the Windows hosts file in one step"),
            ApplyState.UnknownMappings => ("Disable Profile", "\uE71A", false,
                "Remove Kiitou's mappings, which match no saved profile, from the Windows hosts file"),
            _ => ("Apply Profile", "\uE768", true,
                "Save any changes to the selected profile, write it to the Windows hosts file, and return to Home")
        };
        ApplyText.Text = text;
        ApplyGlyph.Text = glyph;
        ApplyButton.Style = (Style)FindResource(accent ? "AccentBtn" : "Btn");
        ApplyButton.Margin = new Thickness(0, 0, 12, 0);
        ApplyButton.Padding = new Thickness(14, 7, 14, 7);
        ApplyButton.ToolTip = tip;

        // One badge: lit while the hosts file holds exactly what is on screen, grey otherwise.
        bool lit = _state == ApplyState.Active;
        Brush brush = lit ? B("Success") : B("Muted");
        StateText.Foreground = StateGlyph.Foreground = brush;
        StatePill.BorderBrush = lit ? B("Success") : B("Border");
        StatePill.Opacity = lit ? 1.0 : 0.55;
        StatePill.ToolTip = _state switch
        {
            ApplyState.Active => "The hosts file contains exactly this profile's mappings.",
            ApplyState.ChangesPending => "This profile is applied, but your changes are not in the hosts file yet.",
            ApplyState.UnknownMappings => "Kiitou's mappings in the hosts file match no saved profile.",
            _ => "No profile is applied."
        };
    }

    private static readonly string[] HomeFilters =
    [
        "All websites", "Enabled", "Disabled", "Connection Verified",
        "Connection Works, Verification Incomplete", "Failed or Unsafe", "Not Tested"
    ];

    private void HomeFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _refreshing) return;
        _refreshing = true;
        try
        {
            RefreshHome();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Whether a website passes Home's search text and filter choice.</summary>
    private bool PassesHomeFilter(WebsiteEntry website, ProfileWebsiteSelection selection, AddressCandidate? shown)
    {
        string search = SearchBox.Text.Trim();
        if (search.Length > 0 &&
            !website.Hostname.Contains(search, StringComparison.OrdinalIgnoreCase) &&
            !website.Addresses.Any(a => a.IpAddress.Contains(search, StringComparison.OrdinalIgnoreCase)))
            return false;

        VerificationStatus status = shown?.LastAssessment?.Status ?? VerificationStatus.NotTested;
        return FilterBox.SelectedIndex switch
        {
            1 => selection.Enabled,
            2 => !selection.Enabled,
            3 => status == VerificationStatus.ConnectionVerified,
            4 => status == VerificationStatus.ConnectionWorksVerificationIncomplete,
            5 => status == VerificationStatus.FailedOrUnsafe,
            6 => status == VerificationStatus.NotTested,
            _ => true
        };
    }

    /// <summary>The choices for one address family's drop-down: "none" first, then that family's candidates.</summary>
    private static List<AddressOption> FamilyOptions(WebsiteEntry website, IpFamily family, string noneLabel)
    {
        var options = new List<AddressOption> { new() { Id = null, Label = noneLabel } };
        options.AddRange(website.Addresses
            .Where(a => IpAddressRules.FamilyOf(a.IpAddress) == family)
            .Select(a => new AddressOption { Id = a.Id, Label = a.IpAddress }));
        return options;
    }

    private void RefreshHome()
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Guid? selectedId = (HomeGrid.SelectedItem as WebsiteRow)?.Id;
        var rows = new List<WebsiteRow>();
        foreach (WebsiteEntry website in _s.Catalog.Websites)
        {
            ProfileWebsiteSelection selection = _s.Working.View(website.Id);
            AddressCandidate? v4 = website.Addresses.FirstOrDefault(a => a.Id == selection.SelectedIpv4Id);
            AddressCandidate? v6 = website.Addresses.FirstOrDefault(a => a.Id == selection.SelectedIpv6Id);
            AddressCandidate? shown = v4 ?? v6;
            if (!PassesHomeFilter(website, selection, shown)) continue;

            // A drop-down is shown for each address family the website has candidates for.
            bool hasV4 = website.Addresses.Any(a => IpAddressRules.FamilyOf(a.IpAddress) == IpFamily.IPv4);
            bool hasV6 = website.Addresses.Any(a => IpAddressRules.FamilyOf(a.IpAddress) == IpFamily.IPv6);
            bool both = hasV4 && hasV6;
            var v4Options = FamilyOptions(website, IpFamily.IPv4,
                both ? "— No IPv4 address —" : hasV4 ? NotAssigned.Label : "— No addresses yet —");
            var v6Options = FamilyOptions(website, IpFamily.IPv6, both ? "— No IPv6 address —" : NotAssigned.Label);

            string summary = !selection.Enabled
                ? "Disabled"
                : shown is null ? "Enabled, but no address is assigned" : "Enabled";
            var (text, glyph, brush) = Display(shown?.LastAssessment);

            rows.Add(new WebsiteRow
            {
                Id = website.Id,
                Hostname = website.Hostname,
                Summary = summary,
                Enabled = selection.Enabled,
                Ipv4Options = v4Options,
                Ipv4Selected = v4Options.FirstOrDefault(o => o.Id == selection.SelectedIpv4Id) ?? v4Options[0],
                Ipv4Visibility = hasV4 || !hasV6 ? Visibility.Visible : Visibility.Collapsed,
                Ipv6Options = v6Options,
                Ipv6Selected = v6Options.FirstOrDefault(o => o.Id == selection.SelectedIpv6Id) ?? v6Options[0],
                Ipv6Visibility = hasV6 ? Visibility.Visible : Visibility.Collapsed,
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
        string applied = _state switch
        {
            ApplyState.Active or ApplyState.ChangesPending => _s.Profiles.Active.Name,
            ApplyState.UnknownMappings => "mappings that match no saved profile",
            _ => "none"
        };
        FooterMessage.Text = _message ?? "Applied: " + applied;
    }

    // ---- Navigation --------------------------------------------------------------------

    private const string HomeView = Navigation.Home;
    private const string WebsitesView = Navigation.Websites;
    private const string DiscoveryView = Navigation.Discovery;
    private const string SettingsView = Navigation.Settings;
    private string _currentView = Navigation.Home;

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _reorderingNav || Nav.SelectedItem is not ListBoxItem { Tag: string page }) return;
        ShowView(page);
    }

    private Dictionary<string, UIElement> Views => new()
    {
        [Navigation.Home] = ViewHome,
        [Navigation.Websites] = ViewWebsites,
        [Navigation.Discovery] = ViewDiscovery,
        [Navigation.Profiles] = ViewProfiles,
        [Navigation.Network] = ViewNetwork,
        [Navigation.Hosts] = ViewHosts,
        [Navigation.Settings] = ViewSettings
    };

    private void ShowView(string page)
    {
        foreach (var (id, view) in Views)
            view.Visibility = id == page ? Visibility.Visible : Visibility.Collapsed;
        _currentView = page;
        ListBoxItem? item = Nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == page);
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item)) Nav.SelectedItem = item;
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

    /// <summary>
    /// The header button. With nothing applied it saves any changes to the selected profile, writes
    /// it to the hosts file and returns to Home. With the profile active it disables it. With edits
    /// pending on the active profile it saves and applies them in a single write.
    /// </summary>
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Info("Please wait for the current operation to finish, or cancel it.");
            return;
        }
        if (_state is ApplyState.Active or ApplyState.UnknownMappings)
        {
            await RunHostsOperationAsync("Disabling the profile...", _s.RemoveAllMappings,
                _state == ApplyState.Active
                    ? $"Disabled \"{_s.Profiles.Active.Name}\": its mappings were removed from the hosts file."
                    : "Kiitou's mappings were removed from the hosts file.");
            return;
        }

        string? done = null;
        bool leaveEditing = false;
        try
        {
            if (_s.HasUnsavedChanges) _s.SaveToCurrentProfile();

            HostsApplyPreview preview = _s.PreviewApply();
            if (preview.Desired.Count == 0 && preview.Analysis.ManagedMappings.Count == 0)
            {
                Info($"\"{_s.Profiles.Active.Name}\" has no enabled website with an address assigned, so there is nothing to apply.");
                return;
            }
            if (!preview.ChangeNeeded)
            {
                leaveEditing = true;
                done = $"\"{_s.Profiles.Active.Name}\" is saved and already applied.";
                return;
            }

            // Ask only when there is something the user needs to know before the file is written.
            var warnings = new List<string>();
            if (preview.ExternalEditDetected)
                warnings.Add("The hosts file was changed outside Kiitou since Kiitou last wrote it. Those changes will be kept.");
            if (preview.Analysis.BlockDamaged)
                warnings.Add("Kiitou's section markers in the hosts file were altered. Only Kiitou's own tagged lines will be replaced.");
            foreach (HostsConflict conflict in preview.Conflicts)
                warnings.Add($"Line {conflict.Existing.LineNumber} of the hosts file already maps {conflict.Existing.Hostname} to {conflict.Existing.IpAddress}. Windows uses the first entry, so that line will win over {conflict.Desired.IpAddress} until you remove it yourself.");
            foreach (SelectionIssue issue in new SelectionEditor(_s.Config, _s.Profiles.Active.Selections).FindIssues())
                warnings.Add(issue.Message);
            if (warnings.Count > 0 && !Confirm(string.Join("\n\n", warnings) + "\n\nApply anyway?")) return;

            _busy = true;
            SetMessage("Applying profile to the hosts file...");
            HostsOperationResult result = await Task.Run(() => _s.Apply(preview));
            _busy = false;
            int count = preview.Desired.Count;
            done = ReportHostsOutcome(result,
                $"Applied \"{_s.Profiles.Active.Name}\": {count} mapping{(count == 1 ? "" : "s")} written to the hosts file.");
            leaveEditing = result.Succeeded;
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
            if (leaveEditing) ShowView(HomeView);
            _message = done;
            RefreshAll();
        }
    }

    /// <summary>
    /// Explains a failed or partly successful hosts-file change in a message box. A plain success
    /// needs no box; its text is returned for the status footer.
    /// </summary>
    private string? ReportHostsOutcome(HostsOperationResult result, string successText)
    {
        switch (result.Outcome)
        {
            case HostsWriteOutcome.Success:
                return successText;
            case HostsWriteOutcome.SuccessFlushFailed:
                Warn(successText + "\n\nThe Windows DNS cache could not be cleared, so the change may take a few minutes to take effect. Restarting your browser usually helps.");
                return successText;
            case HostsWriteOutcome.Cancelled:
                Info("Administrator permission was not given. The hosts file was not changed.");
                return "The hosts file was not changed.";
            case HostsWriteOutcome.HostsChanged:
                Warn("The hosts file was changed by something else while Kiitou was preparing. Nothing was written. Please try again.");
                return null;
            case HostsWriteOutcome.VerifyFailed:
                Warn("The hosts file does not contain what Kiitou tried to write. Security software may have blocked or reverted the change. A backup of the previous file is in Kiitou's backup folder.");
                return null;
            default:
                Warn("The hosts file could not be written. Security software may be blocking changes to it. Nothing was changed.");
                return null;
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
        IpFamily family = (string)combo.Tag == "IPv6" ? IpFamily.IPv6 : IpFamily.IPv4;
        Dispatcher.InvokeAsync(() => Try(() => _s.Working.SelectAddress(row.Id, family, option.Id)));
    }

    /// <summary>Chooses an address as the active one for its own family.</summary>
    private void ChooseAddress(Guid websiteId, Guid addressId)
    {
        AddressCandidate address = _s.Catalog.Get(websiteId).Addresses.First(a => a.Id == addressId);
        _s.Working.SelectAddress(websiteId, WebsiteCatalog.FamilyOf(address), addressId);
    }

    private void HomeDiscover_Click(object sender, RoutedEventArgs e) => OpenDiscovery(HomeSelection?.Id, discover: true);

    private void HomeTest_Click(object sender, RoutedEventArgs e)
    {
        if (HomeSelection is { } row) OpenDiscovery(row.Id, discover: false);
        else TestEveryWebsite();
    }

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
