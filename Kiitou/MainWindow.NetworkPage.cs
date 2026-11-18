using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kiitou.Catalog;
using Kiitou.Models;
using Kiitou.Network;

namespace Kiitou;

/// <summary>A row of the test log on the Network page.</summary>
public sealed class TestLogRow
{
    public string When { get; init; } = "";
    public string Website { get; init; } = "";
    public string Address { get; init; } = "";
    public string From { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string StatusGlyph { get; init; } = "";
    public System.Windows.Media.Brush? StatusBrush { get; init; }
}

// Network page: where tests appear to come from, the optional test proxy, and the test log.
public partial class MainWindow
{
    private const string AllWebsites = "All websites";
    private const string AllLocations = "All locations";
    private string? _locationError;
    private (string Text, string BrushKey)? _proxyResult;
    private bool _locationChecked;

    private void RefreshNetwork()
    {
        // Where tests appear to come from.
        PublicLocation? origin = _s.LastOrigin;
        if (origin is not null)
        {
            LocationIp.Text = origin.IpAddress;
            LocationCountry.Text = origin.CountryName + (origin.ThroughProxy ? " · through the test proxy" : " · direct connection");
            LocationChecked.Text = "Checked " + Local(origin.CheckedUtc);
        }
        else
        {
            LocationIp.Text = _locationError is null ? "Not checked yet" : "Unknown";
            LocationCountry.Text = _locationError ?? "Press Refresh to find out where tests appear to come from.";
            LocationChecked.Text = _locationChecked ? "" : "Kiitou asks Cloudflare, which needs no account.";
        }

        // Test proxy. Like the reputation rows, it is locked while switched on.
        ProxySettings proxy = _net.Proxy;
        bool locked = proxy.Enabled;
        ProxyToggle.IsChecked = proxy.Enabled;
        ProxyType.IsEnabled = ProxyHost.IsEnabled = ProxyPort.IsEnabled = ProxyUser.IsEnabled =
            ProxyPassword.IsEnabled = ProxyPasswordPlain.IsEnabled = !locked;
        if (!ProxyHost.IsKeyboardFocusWithin && ProxyHost.Text != proxy.Host) ProxyHost.Text = proxy.Host;
        string port = proxy.Port == 0 ? "" : proxy.Port.ToString(CultureInfo.InvariantCulture);
        if (!ProxyPort.IsKeyboardFocusWithin && ProxyPort.Text != port) ProxyPort.Text = port;
        if (!ProxyUser.IsKeyboardFocusWithin && ProxyUser.Text != proxy.Username) ProxyUser.Text = proxy.Username;
        if (!ProxyPassword.IsKeyboardFocusWithin && !ProxyPasswordPlain.IsKeyboardFocusWithin)
        {
            string stored = _net.Password;
            if (ProxyPassword.Password != stored) ProxyPassword.Password = stored;
            if (ProxyPasswordPlain.Text != stored) ProxyPasswordPlain.Text = stored;
        }
        UpdateProxyHints();
        if (_proxyResult is { } result)
        {
            ProxyStatus.Text = result.Text;
            ProxyStatus.Foreground = B(result.BrushKey);
        }
        else
        {
            ProxyStatus.Text = locked
                ? $"Tests go through {proxy.Host}:{proxy.Port}. Switch it off to change it."
                : "Only Kiitou's tests use this. Tor Browser: 127.0.0.1, port 9150.";
            ProxyStatus.Foreground = B("Muted");
        }

        RefreshTestLog();
    }

    private void RefreshTestLog()
    {
        IReadOnlyList<TestLogEntry> entries = _s.TestLog?.Entries ?? [];

        var websites = new List<string> { AllWebsites };
        websites.AddRange(entries.Select(e => e.Hostname).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h, StringComparer.OrdinalIgnoreCase));
        var locations = new List<string> { AllLocations };
        locations.AddRange(entries.Select(LocationLabel).Distinct().OrderBy(l => l, StringComparer.OrdinalIgnoreCase));
        string website = LogWebsite.SelectedItem as string ?? AllWebsites;
        string location = LogLocation.SelectedItem as string ?? AllLocations;
        LogWebsite.ItemsSource = websites;
        LogWebsite.SelectedItem = websites.Contains(website) ? website : AllWebsites;
        LogLocation.ItemsSource = locations;
        LogLocation.SelectedItem = locations.Contains(location) ? location : AllLocations;

        LogGrid.ItemsSource = entries
            .Where(e => (string)LogWebsite.SelectedItem == AllWebsites || string.Equals(e.Hostname, (string)LogWebsite.SelectedItem, StringComparison.OrdinalIgnoreCase))
            .Where(e => (string)LogLocation.SelectedItem == AllLocations || LocationLabel(e) == (string)LogLocation.SelectedItem)
            .Select(e =>
            {
                var (text, glyph, brush) = Display(new AddressAssessment { Status = e.Status, AssessedUtc = DateTime.UtcNow });
                return new TestLogRow
                {
                    When = Local(e.TestedUtc),
                    Website = e.Hostname,
                    Address = e.IpAddress,
                    From = e.FromIp.Length == 0 ? "Unknown" : $"{e.FromIp} · {LocationLabel(e)}",
                    StatusText = text,
                    StatusGlyph = glyph,
                    StatusBrush = brush
                };
            }).ToList();
        ClearLogButton.IsEnabled = entries.Count > 0;
    }

    private static string LocationLabel(TestLogEntry entry) =>
        entry.FromCountry.Length == 0 ? "Unknown" : entry.FromCountry + (entry.ThroughProxy ? " (proxy)" : "");

    private void LogFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _refreshing) return;
        _refreshing = true;
        try
        {
            RefreshTestLog();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Delete the whole test log?\n\nThe results shown with each address are kept.")) return;
        Try(() => _s.TestLog?.Clear());
    }

    private async void LocationRefresh_Click(object sender, RoutedEventArgs e)
    {
        _locationChecked = true;
        await RunBusyAsync("Finding where tests appear to come from...", async token =>
        {
            try
            {
                await _s.GetOriginAsync(token, refresh: true);
                _locationError = null;
            }
            catch (LocationException ex)
            {
                _locationError = ex.Message;
            }
        });
    }

    // ---- Proxy ----------------------------------------------------------------------------

    private string TypedProxyPassword => ProxyEye.IsChecked == true ? ProxyPasswordPlain.Text : ProxyPassword.Password;

    private void UpdateProxyHints()
    {
        ProxyHostHint.Visibility = ProxyHost.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProxyPortHint.Visibility = ProxyPort.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProxyUserHint.Visibility = ProxyUser.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProxyPasswordHint.Visibility = TypedProxyPassword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProxyField_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) UpdateProxyHints();
    }

    /// <summary>Stores what is typed, if it differs from what is stored. Returns false if it is not valid.</summary>
    private bool CommitProxy()
    {
        ProxySettings proxy = _net.Proxy;
        if (proxy.Enabled) return true;
        string port = proxy.Port == 0 ? "" : proxy.Port.ToString(CultureInfo.InvariantCulture);
        if (ProxyHost.Text.Trim() == proxy.Host && ProxyPort.Text.Trim() == port &&
            ProxyUser.Text.Trim() == proxy.Username && TypedProxyPassword == _net.Password)
            return true;
        try
        {
            _net.SetProxy(ProxyHost.Text, ProxyPort.Text, ProxyUser.Text, TypedProxyPassword);
            _proxyResult = null;
            return true;
        }
        catch (CatalogException ex)
        {
            Warn(ex.Message);
            return false;
        }
    }

    private void ProxyField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ProxyHost.IsKeyboardFocusWithin || ProxyPort.IsKeyboardFocusWithin || ProxyUser.IsKeyboardFocusWithin ||
            ProxyPassword.IsKeyboardFocusWithin || ProxyPasswordPlain.IsKeyboardFocusWithin)
            return;
        CommitProxy();
        RefreshAll();
    }

    private void ProxyEye_Click(object sender, RoutedEventArgs e)
    {
        bool show = ProxyEye.IsChecked == true;
        if (show) ProxyPasswordPlain.Text = ProxyPassword.Password;
        else ProxyPassword.Password = ProxyPasswordPlain.Text;
        ProxyPasswordPlain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProxyPassword.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ProxyTest_Click(object sender, RoutedEventArgs e)
    {
        Socks5Proxy proxy;
        try
        {
            proxy = _net.Proxy.Enabled
                ? _net.ActiveProxy!
                : NetworkSettings.BuildFromInput(ProxyHost.Text, ProxyPort.Text, ProxyUser.Text, TypedProxyPassword);
        }
        catch (CatalogException ex)
        {
            Warn(ex.Message);
            return;
        }
        if (proxy.Host.Length == 0)
        {
            Info("Enter the proxy's server and port first, such as 127.0.0.1 and 9150.");
            return;
        }

        PublicLocation? seen = null;
        string? error = null;
        await RunBusyAsync($"Testing the proxy at {proxy}...", async token =>
        {
            try
            {
                seen = await new CloudflareLocationService().GetAsync(proxy, token);
            }
            catch (LocationException ex)
            {
                error = ex.Message;
            }
        });
        _proxyResult = seen is not null
            ? ($"Proxy works · appears in {seen.CountryName} ({seen.IpAddress})", "Success")
            : error is not null ? (error, "Danger") : null;
        RefreshAll();
    }

    private void ProxyToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ProxyToggle.IsChecked == true;
        if (enabled && !CommitProxy())
        {
            RefreshAll();
            return;
        }
        _proxyResult = null;
        Try(() => _net.SetEnabled(enabled));
    }
}
