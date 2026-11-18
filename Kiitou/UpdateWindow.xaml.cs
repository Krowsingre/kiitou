using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Kiitou.Theming;
using Kiitou.Updates;

namespace Kiitou;

/// <summary>Everything the update window needs, written by Kiitou before it closes for an update.</summary>
public sealed class UpdateRequest
{
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    /// <summary>Where to download the setup program from. Not needed when <see cref="SetupPath"/> is set.</summary>
    public string? Url { get; set; }
    public string? Signature { get; set; }
    /// <summary>A setup program already on disk (it is still checked against the signature).</summary>
    public string? SetupPath { get; set; }
    /// <summary>The installed Kiitou to start once the update is installed.</summary>
    public string TargetExe { get; set; } = "";
    public string DownloadFolder { get; set; } = "";
    public string? SnapshotPath { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public string ThemeColor { get; set; } = "purple";
    public string ThemeMode { get; set; } = "auto";
}

/// <summary>
/// Run as a temporary copy of Kiitou.exe (<c>--apply-update request.json</c>), because the installed
/// Kiitou.exe is about to be replaced. It downloads and checks the update, installs it, starts the
/// new Kiitou in the same place, and closes.
/// </summary>
public partial class UpdateWindow : Window
{
    public const string Switch = "--apply-update";
    public const string PreviewSwitch = "--update-preview";

    private readonly UpdateRequest _request;
    private readonly bool _preview;
    private bool _installing;

    public UpdateWindow(UpdateRequest request, bool preview = false)
    {
        _request = request;
        _preview = preview;
        InitializeComponent();
        ThemeManager.Apply(request.ThemeColor, request.ThemeMode);
        Icon = ThemeManager.WindowIcon(request.ThemeColor);
        CardIcon.Source = ThemeManager.LargeIcon(request.ThemeColor);
        Versions.Text = $"{request.FromVersion} → {request.ToVersion}";
        if (!double.IsNaN(request.Left) && !double.IsNaN(request.Top))
        {
            Left = request.Left;
            Top = request.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        LoadSnapshot(request.SnapshotPath);
        Closing += OnClosing;
        if (!preview) Loaded += async (_, _) => await RunAsync();
    }

    public static UpdateRequest ReadRequest(string path) =>
        JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllText(path))
        ?? throw new UpdateException("The update instructions could not be read.");

    /// <summary>Shows one stage without doing anything, for screenshots of the design.</summary>
    public void ShowPreview(string stage)
    {
        switch (stage)
        {
            case "installing":
                ShowProgress("Installing…", null);
                break;
            case "starting":
                ShowProgress("Starting Kiitou…", null);
                break;
            case "error":
                ShowError($"The update could not be installed. Kiitou {_request.FromVersion} is unchanged.");
                break;
            default:
                ShowProgress("Downloading… 42%", 0.42);
                break;
        }
    }

    private void LoadSnapshot(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            Snapshot.Source = bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            // The plain background is shown instead.
        }
    }

    private void ShowProgress(string status, double? share)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        Heading.Text = "Updating Kiitou";
        Status.Text = status;
        Progress.IsIndeterminate = share is null;
        if (share is double value) Progress.Value = value;
    }

    private void ShowError(string message)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        Heading.Text = "Update not installed";
        ErrorText.Text = message;
    }

    private async Task RunAsync()
    {
        string setup;
        var updates = new UpdateService(Version.Parse(_request.FromVersion), downloadFolder: _request.DownloadFolder);
        try
        {
            if (!string.IsNullOrEmpty(_request.SetupPath))
            {
                ShowProgress("Checking the update…", null);
                setup = _request.SetupPath;
                await Task.Run(() => updates.VerifyFile(setup, _request.Signature));
            }
            else
            {
                ShowProgress("Downloading… 0%", 0);
                var info = new UpdateInfo(updates.Current, Version.Parse(_request.ToVersion), null, _request.Url, _request.Signature);
                var progress = new Progress<double>(share => ShowProgress($"Downloading… {share:P0}", share));
                setup = await updates.DownloadAsync(info, progress, CancellationToken.None);
            }

            _installing = true;
            ShowProgress("Installing…", null);
            string log = Path.Combine(_request.DownloadFolder, "install.log");
            int exitCode = await UpdateService.RunInstallerAsync(setup, log, TimeSpan.FromMinutes(5));
            _installing = false;
            if (exitCode != 0 || UpdateService.LogHasErrors(log))
                throw new UpdateException($"The update could not be installed. Kiitou {_request.FromVersion} is unchanged.");
        }
        catch (UpdateException ex)
        {
            _installing = false;
            ShowError(ex.Message);
            return;
        }

        ShowProgress("Starting Kiitou…", null);
        if (await StartKiitouAsync()) Close();
        else ShowError("Kiitou was updated, but it could not be started. Start it from the Start menu.");
    }

    /// <summary>Starts the installed Kiitou and waits for its window, so there is no gap between the two.</summary>
    private async Task<bool> StartKiitouAsync()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(_request.TargetExe) { UseShellExecute = false });
            if (process is null) return false;
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < TimeSpan.FromSeconds(60))
            {
                process.Refresh();
                if (process.HasExited) return false;
                if (process.MainWindowHandle != IntPtr.Zero) return true;
                await Task.Delay(250);
            }
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Closing in the middle of installing would leave the user without a window to report the result.
        if (_installing) e.Cancel = true;
    }

    private async void TryAgain_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) return;
        await RunAsync();
    }

    private async void OpenKiitou_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) return;
        if (await StartKiitouAsync()) Close();
        else ErrorText.Text = "Kiitou could not be started. Start it from the Start menu.";
    }
}
