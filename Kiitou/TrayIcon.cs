using System.Windows;
using Kiitou.Theming;

namespace Kiitou;

/// <summary>
/// The optional notification-area icon. It uses the icon of the active color family and
/// restores the window when clicked. It exists only while the option is switched on.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Window _window;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _image;

    public TrayIcon(Window window) => _window = window;

    public void Update(string color, bool enabled)
    {
        if (!enabled)
        {
            Dispose();
            return;
        }

        _notifyIcon ??= Create();
        System.IO.Stream? stream = Application.GetResourceStream(ThemeManager.IconUri(color))?.Stream;
        if (stream is not null)
        {
            using (stream)
            {
                System.Drawing.Icon? previous = _image;
                _image = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
                _notifyIcon.Icon = _image;
                previous?.Dispose();
            }
        }
        _notifyIcon.Visible = true;
    }

    private System.Windows.Forms.NotifyIcon Create()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Kiitou", null, (_, _) => Restore());
        menu.Items.Add("Exit", null, (_, _) => _window.Close());

        var icon = new System.Windows.Forms.NotifyIcon { Text = "Kiitou", ContextMenuStrip = menu };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) Restore();
        };
        return icon;
    }

    private void Restore()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _image?.Dispose();
        _image = null;
    }
}
