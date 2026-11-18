using System.IO;
using System.Windows;
using Kiitou.Hosts;
using Kiitou.Network;
using Kiitou.Services;
using Kiitou.Session;
using Kiitou.Theming;
using Kiitou.Verification;

namespace Kiitou;

public partial class App : Application
{
    private const string LayoutCheckSwitch = "--layout-check";
    private const string UninstallCleanupSwitch = "--uninstall-cleanup";

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Elevated helper mode: perform the one requested hosts-file write and exit without any UI.
        if (e.Args.Length > 0 && e.Args[0] == HostsHelper.Switch)
        {
            int exitCode;
            try
            {
                exitCode = HostsHelper.Run(e.Args);
            }
            catch (Exception)
            {
                exitCode = (int)HostsWriteOutcome.WriteFailed;
            }
            Shutdown(exitCode);
            return;
        }

        // Run by the uninstaller: offer to take Kiitou's mappings out of the hosts file first.
        if (e.Args.Length > 0 && e.Args[0] == UninstallCleanupSwitch)
        {
            Shutdown(RunUninstallCleanup());
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "Kiitou encountered an unexpected error and must close.\n\n" + args.Exception.Message,
                "Kiitou error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        try
        {
            StoragePaths paths = StoragePaths.Resolve();
            var store = new ConfigurationStore(paths);
            var config = store.Load();

            var hostsFile = new HostsFileService(HostsFileService.SystemHostsPath);
            var hosts = new HostsApplyService(
                hostsFile,
                new HostsBackupStore(Path.Combine(paths.DataDirectory, "backups")),
                HostsApplyService.CreateSystemWriter(hostsFile, paths.DataDirectory));
            var reputation = new ReputationSettings(config, new WindowsSecretProtector(), () => store.Save(config));
            var hub = new ReputationHub(reputation);
            var session = new KiitouSession(store, config, hosts, new DirectDnsResolver(), new HttpsAddressTester(), hub);

            ThemeManager.Apply(config.Theme.Color, config.Theme.Mode);
            var window = new MainWindow(session, paths, reputation, hub);
            MainWindow = window;
            window.Show();

            // Design verification: measure every view against the layout rules, write a report, exit.
            if (e.Args.Length == 2 && e.Args[0] == LayoutCheckSwitch)
            {
                string report = e.Args[1];
                window.Dispatcher.InvokeAsync(() =>
                {
                    List<string> problems = window.RunLayoutCheck();
                    File.WriteAllLines(report, problems.Count == 0 ? ["Layout check passed."] : problems);
                    Shutdown(problems.Count == 0 ? 0 : 3);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Kiitou could not initialize its local configuration.\n\n" + ex.Message,
                "Kiitou startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// If Kiitou's mappings are still in the hosts file, asks whether to remove them before Kiitou
    /// is uninstalled. Never stops the uninstall: every outcome returns 0.
    /// </summary>
    private static int RunUninstallCleanup()
    {
        try
        {
            var hostsFile = new HostsFileService(HostsFileService.SystemHostsPath);
            int count = ManagedHostsBlock.Analyze(hostsFile.Read().Text).ManagedMappings.Count;
            if (count == 0) return 0;

            MessageBoxResult answer = MessageBox.Show(
                $"Kiitou's {count} mapping{(count == 1 ? " is" : "s are")} still in the Windows hosts file.\n\n" +
                "Remove them before Kiitou is uninstalled? Windows will ask for permission.\n" +
                "Choose No to leave them in place.",
                "Uninstall Kiitou", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes) return 0;

            StoragePaths paths = StoragePaths.Resolve();
            var hosts = new HostsApplyService(
                hostsFile,
                new HostsBackupStore(Path.Combine(paths.DataDirectory, "backups")),
                HostsApplyService.CreateSystemWriter(hostsFile, paths.DataDirectory));
            HostsOperationResult result = hosts.Apply(hosts.Preview([], null));
            if (!result.Succeeded && result.Outcome != HostsWriteOutcome.Cancelled)
                MessageBox.Show("Kiitou's mappings could not be removed from the hosts file. They are still there.",
                    "Uninstall Kiitou", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Nothing can be done here; the uninstall carries on.
        }
        return 0;
    }
}
