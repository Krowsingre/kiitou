using System.Windows;
using Kiitou.Models;
using Kiitou.Services;

namespace Kiitou;

public partial class MainWindow : Window
{
    private readonly ConfigurationStore _store;
    private readonly AppConfiguration _configuration;

    public MainWindow(ConfigurationStore store, AppConfiguration configuration)
    {
        InitializeComponent();
        _store = store;
        _configuration = configuration;
        StorageStatus.Text = $"Storage: {_store.Paths.DataDirectory}\nTheme: {_configuration.Theme.Color} / {_configuration.Theme.Mode}";
    }
}
