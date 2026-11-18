using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        // Files left by an earlier update (the setup program, the update window's copy) are no longer needed.
        _updates.CleanUpDownloads();
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

    /// <summary>
    /// Hands the update over to the update window: a temporary copy of Kiitou that opens in exactly
    /// this window's place, showing a picture of it, while this window closes underneath. The
    /// update window downloads and checks the update, installs it, and starts the new version.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (_foundUpdate is not { IsAvailable: true } update || _updating) return;
        if (_busy)
        {
            Info("Please wait for the current operation to finish before updating.");
            return;
        }
        _updating = true;
        _updateStatus = $"Preparing to update to Kiitou {update.Available}...";
        RefreshUpdates();
        try
        {
            string? installed = Environment.ProcessPath;
            if (string.IsNullOrEmpty(installed)) throw new UpdateException("Kiitou could not find its own program file.");

            // A copy of the settings, in case anything about the new version goes wrong.
            _s.Store.CreateSafetyCopy($"before-{update.Available}");
            SaveWindowPosition();

            string folder = _updates.DownloadFolder;
            Directory.CreateDirectory(folder);
            string updater = Path.Combine(folder, "Kiitou-updater.exe");
            await Task.Run(() => File.Copy(installed, updater, overwrite: true));
            string snapshot = Path.Combine(folder, "snapshot.png");
            SaveSnapshot(snapshot);

            var request = new UpdateRequest
            {
                FromVersion = _updates.Current.ToString(3),
                ToVersion = update.Available!.ToString(3),
                Url = update.Url,
                Signature = update.Signature,
                TargetExe = installed,
                DownloadFolder = folder,
                SnapshotPath = snapshot,
                Left = Left,
                Top = Top,
                ThemeColor = _s.Config.Theme.Color,
                ThemeMode = _s.Config.Theme.Mode
            };
            string requestPath = Path.Combine(folder, "update-request.json");
            File.WriteAllText(requestPath, System.Text.Json.JsonSerializer.Serialize(request));

            using Process? window = Process.Start(new ProcessStartInfo(updater)
            {
                ArgumentList = { UpdateWindow.Switch, requestPath },
                UseShellExecute = false
            });
            if (window is null) throw new UpdateException("The update could not be started.");

            // Close once the update window is showing, so there is no moment without a Kiitou window.
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < TimeSpan.FromSeconds(30))
            {
                window.Refresh();
                if (window.HasExited) throw new UpdateException("The update window closed unexpectedly. Nothing was changed.");
                if (window.MainWindowHandle != IntPtr.Zero) break;
                await Task.Delay(150);
            }
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _updateStatus = ex is UpdateException ? ex.Message : "The update could not be started: " + ex.Message;
            SetMessage(null);
            _updating = false;
            RefreshUpdates();
        }
    }

    /// <summary>Saves a picture of the window's contents for the update window to show.</summary>
    private void SaveSnapshot(string path)
    {
        try
        {
            if (Content is not FrameworkElement root || root.ActualWidth < 1) return;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(path);
            encoder.Save(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The update window shows its plain background instead.
        }
    }

    // ---- Uninstalling from inside Kiitou --------------------------------------------------

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        string uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(uninstaller))
        {
            Info("This copy of Kiitou was not installed with its setup program, so there is nothing to uninstall.");
            return;
        }
        if (MessageBox.Show(this,
                "Kiitou will close and the uninstaller will open. It will ask whether to keep your settings.",
                "Uninstall Kiitou", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;
        try
        {
            SaveWindowPosition();
            Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true })?.Dispose();
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Warn("The uninstaller could not be started.\n\n" + ex.Message);
        }
    }

    // ---- Window position ------------------------------------------------------------------

    /// <summary>Puts the window where it was last time, if that place is still on a screen.</summary>
    private void RestoreWindowPosition()
    {
        if (_s.Config.Settings.WindowLeft is not double left || _s.Config.Settings.WindowTop is not double top) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        // At least the title bar must be reachable.
        if (left + 100 > screen.Right || left + Width - 100 < screen.Left || top < screen.Top || top + 40 > screen.Bottom) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void SaveWindowPosition()
    {
        if (WindowState != WindowState.Normal || double.IsNaN(Left) || double.IsNaN(Top)) return;
        try
        {
            _s.SetWindowPosition(Left, Top);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Not worth interrupting the user for.
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
