using System.Windows;
using Kiitou.Hosts;
using Kiitou.Services;

namespace Kiitou;

public partial class App : Application
{
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
            var store = new ConfigurationStore(StoragePaths.Resolve());
            var config = store.Load();
            var window = new MainWindow(store, config);
            MainWindow = window;
            window.Show();
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
