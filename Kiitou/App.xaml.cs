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
}
