using System.Windows;
using Kiitou.Updates;

namespace Kiitou;

// Updating: Kiitou asks GitHub whether a newer version exists, and installs it on request
// (or by itself, if that is switched on) after confirming it is signed with Kiitou's update key.
public partial class MainWindow
{
    /// <summary>Set by automated tests so runs never depend on what is published at the time.</summary>
    private const string NoUpdateCheckVariable = "KIITOU_NO_UPDATE_CHECK";

    private UpdateService _updates = null!;
    private UpdateInfo? _foundUpdate;
    private string? _updateStatus;
    private bool _updating;

    private void StartUpdates()
    {
        Version version = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _updates = new UpdateService(version);
        if (_s.Config.Settings.CheckUpdates && Environment.GetEnvironmentVariable(NoUpdateCheckVariable) != "1")
            _ = CheckOnStartupAsync();
    }

    /// <summary>A little after opening, so it never slows the first screen. Failing to reach GitHub is silent here.</summary>
    private async Task CheckOnStartupAsync()
    {
        try
        {
            await Task.Delay(2500);
            UpdateInfo info = await _updates.CheckAsync(CancellationToken.None);
            if (!info.IsAvailable) return;
            _foundUpdate = info;
            if (_s.Config.Settings.AutoInstallUpdates)
            {
                await InstallUpdateAsync();
                return;
            }
            _updateStatus = $"Kiitou {info.Available} is ready to install.";
            SetMessage($"Kiitou {info.Available} is ready. Install it in Settings.");
            RefreshUpdates();
        }
        catch (UpdateException)
        {
            // Not reaching the update server just means no update this time.
        }
    }

    private void RefreshUpdates()
    {
        UpdateStatus.Text = _updateStatus ?? $"Kiitou {_updates.Current} is installed.";
        UpdateButton.Content = _foundUpdate is { IsAvailable: true } ? "Install and Restart" : "Check for Updates";
        UpdateButton.IsEnabled = !_updating;
        UpdateCheckOption.IsChecked = _s.Config.Settings.CheckUpdates;
        UpdateAutoOption.IsChecked = _s.Config.Settings.CheckUpdates && _s.Config.Settings.AutoInstallUpdates;
        UpdateAutoOption.IsEnabled = _s.Config.Settings.CheckUpdates;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (_foundUpdate is { IsAvailable: true })
        {
            await InstallUpdateAsync();
            return;
        }

        _updating = true;
        _updateStatus = "Checking for updates...";
        RefreshUpdates();
        try
        {
            UpdateInfo info = await _updates.CheckAsync(CancellationToken.None);
            _foundUpdate = info.IsAvailable ? info : null;
            _updateStatus = info.IsAvailable
                ? $"Kiitou {info.Available} is ready to install."
                : $"Kiitou {_updates.Current} is up to date.";
        }
        catch (UpdateException ex)
        {
            _updateStatus = ex.Message;
        }
        finally
        {
            _updating = false;
            RefreshUpdates();
        }
    }

    /// <summary>Downloads the update, checks its signature, starts the setup program and closes Kiitou.</summary>
    private async Task InstallUpdateAsync()
    {
        if (_foundUpdate is not { IsAvailable: true } update || _updating) return;
        if (_busy)
        {
            Info("Please wait for the current operation to finish before updating.");
            return;
        }
        _updating = true;
        _updateStatus = $"Downloading Kiitou {update.Available}...";
        RefreshUpdates();
        try
        {
            var progress = new Progress<double>(share =>
            {
                _updateStatus = $"Downloading Kiitou {update.Available}... {share:P0}";
                UpdateStatus.Text = _updateStatus;
                SetMessage(_updateStatus);
            });
            string setup = await _updates.DownloadAsync(update, progress, CancellationToken.None);
            _updateStatus = "Installing...";
            RefreshUpdates();
            UpdateService.StartInstaller(setup);
            // The setup program replaces Kiitou and opens it again.
            Application.Current.Shutdown();
        }
        catch (UpdateException ex)
        {
            _updateStatus = ex.Message;
            SetMessage(null);
            _updating = false;
            RefreshUpdates();
        }
    }

    private void UpdateCheckOption_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = UpdateCheckOption.IsChecked == true;
        Try(() => _s.SetUpdateOptions(enabled, _s.Config.Settings.AutoInstallUpdates));
    }

    private void UpdateAutoOption_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = UpdateAutoOption.IsChecked == true;
        Try(() => _s.SetUpdateOptions(_s.Config.Settings.CheckUpdates, enabled));
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenLink(_paths.DataDirectory);
}
