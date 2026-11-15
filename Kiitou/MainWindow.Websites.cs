using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kiitou.Hosts;
using Kiitou.Models;

namespace Kiitou;

// Websites and Profiles views.
public partial class MainWindow
{
    private Guid? _pendingWebsiteSelection;

    private Guid? SelectedWebsiteId => (WebsiteList.SelectedItem as NamedItem)?.Id;

    private WebsiteEntry? SelectedWebsite =>
        _s.Catalog.Websites.FirstOrDefault(w => w.Id == SelectedWebsiteId);

    private AddressRow BuildAddressRow(WebsiteEntry website, AddressCandidate address)
    {
        AddressAssessment? assessment = address.LastAssessment;
        var (text, glyph, brush) = Display(assessment);
        string detail = assessment is null
            ? ""
            : assessment.Status == VerificationStatus.FailedOrUnsafe
                ? assessment.Reasons.FirstOrDefault() ?? ""
                : assessment.Warnings.FirstOrDefault() ?? assessment.Reasons.LastOrDefault() ?? "";
        return new AddressRow
        {
            WebsiteId = website.Id,
            Id = address.Id,
            Hostname = website.Hostname,
            IpAddress = address.IpAddress,
            Family = IpAddressRules.FamilyOf(address.IpAddress)?.ToString() ?? "",
            StatusText = text,
            StatusGlyph = glyph,
            StatusBrush = brush,
            Checked = assessment is null ? "" : Local(assessment.AssessedUtc),
            Timing = assessment?.TotalMilliseconds is double ms ? $"{ms:0} ms" : "",
            Detail = detail
        };
    }

    private void OpenWebsite(Guid websiteId)
    {
        _pendingWebsiteSelection = websiteId;
        ShowView(1);
    }

    private void RefreshWebsites()
    {
        Guid? keep = _pendingWebsiteSelection ?? SelectedWebsiteId;
        _pendingWebsiteSelection = null;
        var items = _s.Catalog.Websites.Select(w => new NamedItem { Id = w.Id, Name = w.Hostname }).ToList();
        WebsiteList.ItemsSource = items;
        WebsiteList.SelectedItem = items.FirstOrDefault(i => i.Id == keep) ?? items.FirstOrDefault();
        RefreshWebsiteDetail();
    }

    private void RefreshWebsiteDetail()
    {
        WebsiteEntry? website = SelectedWebsite;
        WebsiteEnabled.IsEnabled = WebsiteIpv4.IsEnabled = WebsiteIpv6.IsEnabled = website is not null;
        if (website is null)
        {
            WebsiteTitle.Text = _s.Catalog.Websites.Count == 0 ? "Add a website to begin" : "Select a website";
            WebsiteEnabled.IsChecked = false;
            WebsiteIpv4.ItemsSource = WebsiteIpv6.ItemsSource = null;
            AddressGrid.ItemsSource = null;
            return;
        }

        ProfileWebsiteSelection selection = _s.Working.View(website.Id);
        WebsiteTitle.Text = website.Hostname;
        WebsiteEnabled.IsChecked = selection.Enabled;
        FillFamily(WebsiteIpv4, website, IpFamily.IPv4, selection.SelectedIpv4Id);
        FillFamily(WebsiteIpv6, website, IpFamily.IPv6, selection.SelectedIpv6Id);

        Guid? keep = (AddressGrid.SelectedItem as AddressRow)?.Id;
        var rows = website.Addresses.Select(a => BuildAddressRow(website, a)).ToList();
        AddressGrid.ItemsSource = rows;
        AddressGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);
    }

    private static void FillFamily(ComboBox combo, WebsiteEntry website, IpFamily family, Guid? selectedId)
    {
        var options = new List<AddressOption> { NotAssigned };
        options.AddRange(website.Addresses
            .Where(a => IpAddressRules.FamilyOf(a.IpAddress) == family)
            .Select(a => new AddressOption { Id = a.Id, Label = a.IpAddress }));
        combo.ItemsSource = options;
        combo.SelectedItem = options.FirstOrDefault(o => o.Id == selectedId) ?? NotAssigned;
    }

    private void WebsiteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            RefreshWebsiteDetail();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void WebsiteEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWebsiteId is not Guid id) return;
        bool enabled = WebsiteEnabled.IsChecked == true;
        Try(() => _s.Working.SetEnabled(id, enabled));
    }

    private void WebsiteFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || SelectedWebsiteId is not Guid id) return;
        if (sender is not ComboBox { SelectedItem: AddressOption option } combo) return;
        IpFamily family = ReferenceEquals(combo, WebsiteIpv4) ? IpFamily.IPv4 : IpFamily.IPv6;
        Dispatcher.InvokeAsync(() => Try(() => _s.Working.SelectAddress(id, family, option.Id)));
    }

    private void RenameWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWebsite is not { } website)
        {
            Info("Select a website first.");
            return;
        }
        string? hostname = InputDialog.Prompt(this, "Rename Website", "Website hostname:", website.Hostname);
        if (hostname is null) return;
        Try(() => _s.RenameWebsite(website.Id, hostname));
    }

    private void RemoveWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWebsite is not { } website)
        {
            Info("Select a website first.");
            return;
        }
        RemoveWebsite(website.Id, website.Hostname);
    }

    private void AddAddress_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWebsite is not { } website)
        {
            Info("Select a website first.");
            return;
        }
        string? address = InputDialog.Prompt(this, "Add Address", $"Candidate IP address for {website.Hostname}:", "",
            "IPv4 such as 93.184.216.34, or IPv6 such as 2606:2800:220:1::1");
        if (address is null) return;
        Try(() => _s.AddAddress(website.Id, address));
    }

    private void EditAddress_Click(object sender, RoutedEventArgs e)
    {
        if (AddressGrid.SelectedItem is not AddressRow row)
        {
            Info("Select an address in the table first.");
            return;
        }
        string? address = InputDialog.Prompt(this, "Edit Address", $"Candidate IP address for {row.Hostname}:", row.IpAddress);
        if (address is null) return;
        Try(() => _s.EditAddress(row.WebsiteId, row.Id, address));
    }

    private void RemoveAddress_Click(object sender, RoutedEventArgs e)
    {
        if (AddressGrid.SelectedItem is not AddressRow row)
        {
            Info("Select an address in the table first.");
            return;
        }
        if (!Confirm($"Remove {row.IpAddress} from the candidates for {row.Hostname}?")) return;
        Try(() => _s.RemoveAddress(row.WebsiteId, row.Id));
    }

    private async void WebsiteTestSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AddressGrid.SelectedItem is not AddressRow row)
        {
            Info("Select an address in the table first.");
            return;
        }
        await RunBusyAsync($"Testing {row.IpAddress}...",
            token => _s.VerifyAddressAsync(row.WebsiteId, row.Id, null, token));
    }

    // ---- Profiles ----------------------------------------------------------------------

    private void RefreshProfiles()
    {
        Guid? keep = (ProfileGrid.SelectedItem as ProfileRow)?.Id ?? _s.Config.ActiveProfileId;
        var rows = _s.Profiles.Profiles.Select(p =>
        {
            var states = new List<string>();
            if (p.Id == _s.Config.ActiveProfileId) states.Add(_s.HasUnsavedChanges ? "Selected (unsaved changes)" : "Selected");
            if (p.Id == _s.Config.Hosts.LastAppliedProfileId) states.Add("Last applied");
            return new ProfileRow
            {
                Id = p.Id,
                Name = p.Name,
                Enabled = $"{p.Selections.Count(x => x.Enabled)} of {_s.Catalog.Websites.Count}",
                LastSaved = Local(p.LastSavedUtc),
                State = string.Join(", ", states)
            };
        }).ToList();
        ProfileGrid.ItemsSource = rows;
        ProfileGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);
    }

    private ProfileRow? RequireProfile()
    {
        if (ProfileGrid.SelectedItem is ProfileRow row) return row;
        Info("Select a profile in the table first.");
        return null;
    }

    private void ProfileSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (RequireProfile() is not { } row || row.Id == _s.Config.ActiveProfileId) return;
        SwitchTo(row.Id);
    }

    private void ProfileNew_Click(object sender, RoutedEventArgs e)
    {
        string? name = InputDialog.Prompt(this, "New Profile", "Name for the new profile:", _s.Profiles.UniqueName("New profile"),
            "A new profile starts with no websites enabled.");
        if (name is null) return;
        Try(() => _s.CreateProfile(name));
    }

    private void ProfileRename_Click(object sender, RoutedEventArgs e)
    {
        if (RequireProfile() is not { } row) return;
        string? name = InputDialog.Prompt(this, "Rename Profile", "Profile name:", row.Name);
        if (name is null) return;
        Try(() => _s.RenameProfile(row.Id, name));
    }

    private void ProfileDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (RequireProfile() is not { } row) return;
        Try(() => _s.DuplicateProfile(row.Id));
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (RequireProfile() is not { } row) return;
        if (!Confirm($"Delete the profile \"{row.Name}\"?\n\nWebsites and their addresses are kept. The hosts file is not changed.")) return;
        Try(() => _s.DeleteProfile(row.Id));
    }

    // ---- Import and export -------------------------------------------------------------

    private const string FileFilter = "Kiitou files (*.json)|*.json|All files (*.*)|*.*";

    private string? PickOpen(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = FileFilter };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickSave(string title, string suggestedName)
    {
        string safe = string.Concat(suggestedName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = FileFilter, FileName = safe + ".json" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static string ReadImportFile(string path)
    {
        if (new FileInfo(path).Length > 5 * 1024 * 1024)
            throw new Catalog.CatalogException("The file is too large to be a Kiitou export.");
        return File.ReadAllText(path);
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (PickOpen("Import Profile") is not { } path) return;
        string? done = null;
        Try(() =>
        {
            var summary = _s.ImportProfile(ReadImportFile(path));
            done = $"Imported the profile \"{summary.Profile!.Name}\".\n\n" +
                   $"Websites added: {summary.WebsitesAdded}\nCandidate addresses added: {summary.AddressesAdded}\n\n" +
                   "Nothing was removed or overwritten, and the hosts file was not changed.";
        });
        if (done is not null) Info(done);
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e) => ExportProfile(_s.Profiles.Active);

    private void ProfileExportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (RequireProfile() is not { } row) return;
        ExportProfile(_s.Profiles.Get(row.Id));
    }

    private void ExportProfile(MappingProfile profile)
    {
        if (profile.Id == _s.Config.ActiveProfileId && _s.HasUnsavedChanges &&
            !Confirm("Unsaved changes are not included in the export; the profile is exported as last saved.\n\nExport anyway?"))
            return;
        if (PickSave("Export Profile", "Kiitou profile - " + profile.Name) is not { } path) return;
        Try(() => File.WriteAllText(path, _s.ExportProfile(profile.Id)));
    }

    private void ImportCollection_Click(object sender, RoutedEventArgs e)
    {
        if (PickOpen("Import Website Collection") is not { } path) return;
        string? done = null;
        Try(() =>
        {
            var summary = _s.ImportCollection(ReadImportFile(path));
            done = $"Websites added: {summary.WebsitesAdded}\nCandidate addresses added: {summary.AddressesAdded}\n\n" +
                   "Nothing was removed or overwritten, and the hosts file was not changed.";
        });
        if (done is not null) Info(done);
    }

    private void ExportCollection_Click(object sender, RoutedEventArgs e)
    {
        if (PickSave("Export Website Collection", "Kiitou websites") is not { } path) return;
        Try(() => File.WriteAllText(path, _s.ExportCollection()));
    }
}
