using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Kiitou.Models;
using Kiitou.Network;
using Kiitou.Verification;

namespace Kiitou;

// Discovery & Test view: DNS discovery, address tests, and the reasons behind each status.
public partial class MainWindow
{
    private Guid? _pendingDiscoverySite;
    private DiscoveryResult? _discovery;
    private Guid? _discoverySiteId;

    private Guid? DiscoverySiteId => (DiscoverySite.SelectedItem as NamedItem)?.Id;

    private WebsiteEntry? DiscoveryWebsite => _s.Catalog.Websites.FirstOrDefault(w => w.Id == DiscoverySiteId);

    private void OpenDiscovery(Guid? websiteId, bool discover)
    {
        _pendingDiscoverySite = websiteId;
        ShowView(DiscoveryView);
        if (websiteId is null)
        {
            SetMessage("Choose a website, then use the buttons beside it.");
            return;
        }
        if (discover) Discover_Click(this, new RoutedEventArgs());
        else TestAll_Click(this, new RoutedEventArgs());
    }

    private void RefreshDiscovery()
    {
        Guid? keep = _pendingDiscoverySite ?? DiscoverySiteId;
        _pendingDiscoverySite = null;
        var items = _s.Catalog.Websites.Select(w => new NamedItem { Id = w.Id, Name = w.Hostname }).ToList();
        DiscoverySite.ItemsSource = items;
        DiscoverySite.SelectedItem = items.FirstOrDefault(i => i.Id == keep) ?? items.FirstOrDefault();
        RefreshDiscoveryDetail();
    }

    private void RefreshDiscoveryDetail()
    {
        WebsiteEntry? website = DiscoveryWebsite;
        Guid? keep = (TestGrid.SelectedItem as AddressRow)?.Id;
        List<AddressRow> rows = website is null ? [] : website.Addresses.Select(a => BuildAddressRow(website, a)).ToList();
        TestGrid.ItemsSource = rows;
        TestGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep) ?? rows.FirstOrDefault();

        var discovered = new List<DiscoveredRow>();
        if (website is null)
        {
            DiscoverySummary.Text = "Add a website first.";
        }
        else if (_discovery is null || _discoverySiteId != website.Id)
        {
            DiscoverySummary.Text = "Discover DNS looks up the addresses currently published for the website.";
        }
        else if (!_discovery.Lookup.Succeeded)
        {
            DiscoverySummary.Text = "DNS lookup failed: " + _discovery.Lookup.Error;
        }
        else
        {
            var saved = website.Addresses.Select(a => a.IpAddress).ToHashSet();
            discovered = _discovery.NewAddresses.Where(a => !saved.Contains(a)).Select(a => new DiscoveredRow { IpAddress = a }).ToList();
            int published = _discovery.Lookup.All.Count();
            int unpublished = _discovery.SavedButNotPublished.Count;
            DiscoverySummary.Text =
                $"DNS publishes {published} address{(published == 1 ? "" : "es")} for {website.Hostname}. " +
                (discovered.Count == 0 ? "All of them are already saved." : $"{discovered.Count} not saved yet:") +
                (unpublished > 0
                    ? $"\n{unpublished} saved address{(unpublished == 1 ? " is" : "es are")} not currently published; kept."
                    : "");
        }
        DiscoveredList.ItemsSource = discovered;
        AddDiscoveredButton.IsEnabled = discovered.Count > 0;
        RefreshTestDetail();
    }

    private void DiscoverySite_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            RefreshDiscoveryDetail();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void TestGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) RefreshTestDetail();
    }

    /// <summary>Shows why the selected address has its status.</summary>
    private void RefreshTestDetail()
    {
        if (TestGrid.SelectedItem is not AddressRow row)
        {
            TestDetail.Text = "Select an address to see the reasons for its status.";
            return;
        }
        AddressAssessment? assessment = _s.Catalog.Websites
            .SelectMany(w => w.Addresses).FirstOrDefault(a => a.Id == row.Id)?.LastAssessment;
        if (assessment is null)
        {
            TestDetail.Text = $"{row.IpAddress} for {row.Hostname} has not been tested.";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"{LegitimacyAssessor.StatusText(assessment.Status)} — {row.IpAddress}, checked {Local(assessment.AssessedUtc)}");
        if (assessment.TestedFromIp is { Length: > 0 } fromIp)
            text.AppendLine($"Tested from {fromIp} \u00B7 {assessment.TestedFromCountry}" + (assessment.ThroughProxy ? ", through the test proxy" : ""));
        else if (assessment.ThroughProxy)
            text.AppendLine("Tested through the test proxy; its location was not found out.");
        if (LegitimacyAssessor.IsStale(assessment, DateTime.UtcNow))
            text.AppendLine("This result is more than a day old. Test again for a current answer.");
        foreach (string reason in assessment.Reasons) text.AppendLine("• " + reason);
        foreach (string warning in assessment.Warnings) text.AppendLine("• Warning: " + warning);
        if (assessment.CertificateSubject is not null)
            text.AppendLine($"Certificate: {assessment.CertificateSubject}; issued by {assessment.CertificateIssuer}; expires {Local(assessment.CertificateNotAfterUtc)}");
        if (assessment.TotalMilliseconds is double total)
            text.AppendLine($"Timing: connected in {assessment.ConnectMilliseconds:0} ms, replied in {total:0} ms");
        TestDetail.Text = text.ToString().TrimEnd();
    }

    private WebsiteEntry? RequireDiscoveryWebsite()
    {
        if (DiscoveryWebsite is { } website) return website;
        Info("Choose a website first.");
        return null;
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (RequireDiscoveryWebsite() is not { } website) return;
        await RunBusyAsync($"Looking up {website.Hostname} in DNS...", async token =>
        {
            _discovery = await _s.DiscoverAsync(website.Id, token);
            _discoverySiteId = website.Id;
        });
    }

    private void AddDiscovered_Click(object sender, RoutedEventArgs e)
    {
        if (DiscoveryWebsite is not { } website || DiscoveredList.ItemsSource is not List<DiscoveredRow> rows) return;
        var chosen = rows.Where(r => r.Checked).Select(r => r.IpAddress).ToList();
        if (chosen.Count == 0)
        {
            Info("Tick at least one address to add.");
            return;
        }
        Try(() => _s.AddDiscovered(website.Id, chosen));
    }

    private async void TestSelected_Click(object sender, RoutedEventArgs e)
    {
        if (TestGrid.SelectedItem is not AddressRow row)
        {
            Info("Select an address in the table first.");
            return;
        }
        await RunBusyAsync($"Testing {row.IpAddress}...", token => _s.VerifyAddressAsync(row.WebsiteId, row.Id, null, token));
    }

    private async void TestAll_Click(object sender, RoutedEventArgs e)
    {
        if (RequireDiscoveryWebsite() is not { } website) return;
        if (website.Addresses.Count == 0)
        {
            Info($"{website.Hostname} has no candidate addresses yet. Use Discover DNS or add one on the Websites page.");
            return;
        }
        var progress = new Progress<AddressCandidate>(_ => RefreshAll());
        await RunBusyAsync($"Testing all addresses for {website.Hostname}...",
            token => _s.VerifyAllAsync(website.Id, progress, token));
    }

    private async void FindFirst_Click(object sender, RoutedEventArgs e)
    {
        if (RequireDiscoveryWebsite() is not { } website) return;
        if (website.Addresses.Count == 0)
        {
            Info($"{website.Hostname} has no candidate addresses yet. Use Discover DNS or add one on the Websites page.");
            return;
        }
        var progress = new Progress<AddressCandidate>(_ => RefreshAll());
        AddressCandidate? found = null;
        bool finished = false;
        await RunBusyAsync($"Looking for the first working address for {website.Hostname}...", async token =>
        {
            found = await _s.FindFirstWorkingAsync(website.Id, progress, token);
            finished = true;
        });
        if (!finished) return;
        if (found is null)
        {
            Info($"None of the candidate addresses for {website.Hostname} passed the connection test.");
            return;
        }
        TestGrid.SelectedItem = (TestGrid.ItemsSource as List<AddressRow>)?.FirstOrDefault(r => r.Id == found.Id);
        SetMessage($"{found.IpAddress} is the first address whose connection works. It has not been selected; use Use Selected Address to choose it.");
    }

    /// <summary>Home's "Test Addresses" with no website selected: tests every address of every website.</summary>
    private async void TestEveryWebsite()
    {
        var websites = _s.Catalog.Websites.Where(w => w.Addresses.Count > 0).ToList();
        if (websites.Count == 0)
        {
            Info("There are no addresses to test yet. Add a website and its addresses first.");
            return;
        }
        var progress = new Progress<AddressCandidate>(_ => RefreshAll());
        await RunBusyAsync("Testing every address...", async token =>
        {
            foreach (WebsiteEntry website in websites)
            {
                token.ThrowIfCancellationRequested();
                SetMessage($"Testing addresses for {website.Hostname}...");
                await _s.VerifyAllAsync(website.Id, progress, token);
            }
        });
    }

    private void UseSelectedAddress_Click(object sender, RoutedEventArgs e)
    {
        if (TestGrid.SelectedItem is not AddressRow row)
        {
            Info("Select an address in the table first.");
            return;
        }
        Try(() => ChooseAddress(row.WebsiteId, row.Id));
        SetMessage($"{row.IpAddress} is now chosen for {row.Hostname}. Apply the profile to use it.");
    }

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (RequireDiscoveryWebsite() is not { } website) return;
        OpenLink("https://" + website.Hostname + "/");
    }

    private void OpenLink(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Warn("It could not be opened.\n\n" + ex.Message);
        }
    }
}
