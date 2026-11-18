using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kiitou.Models;

namespace Kiitou;

// The left-hand menu can be reordered by dragging an item up or down. The order is saved with
// the settings, so it is kept across restarts and updates.
public partial class MainWindow
{
    private ListBoxItem? _pressedItem;
    private Point _pressedAt;
    private bool _draggingNav;
    private bool _reorderingNav;

    /// <summary>Puts the menu items in the saved order.</summary>
    private void ApplyNavOrder()
    {
        var items = Nav.Items.OfType<ListBoxItem>().ToDictionary(i => (string)i.Tag);
        _reorderingNav = true;
        try
        {
            Nav.Items.Clear();
            foreach (string page in Navigation.Normalize(_s.Config.Settings.NavOrder))
                if (items.TryGetValue(page, out ListBoxItem? item)) Nav.Items.Add(item);
            Nav.SelectedItem = items[_currentView];
        }
        finally
        {
            _reorderingNav = false;
        }
        foreach (ListBoxItem item in items.Values)
        {
            item.PreviewMouseLeftButtonDown -= NavItem_PreviewMouseDown;
            item.PreviewMouseLeftButtonDown += NavItem_PreviewMouseDown;
        }
        Nav.PreviewMouseMove -= Nav_PreviewMouseMove;
        Nav.PreviewMouseMove += Nav_PreviewMouseMove;
        Nav.PreviewMouseLeftButtonUp -= Nav_PreviewMouseUp;
        Nav.PreviewMouseLeftButtonUp += Nav_PreviewMouseUp;
        Nav.LostMouseCapture -= Nav_LostMouseCapture;
        Nav.LostMouseCapture += Nav_LostMouseCapture;
    }

    private void NavItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = sender as ListBoxItem;
        _pressedAt = e.GetPosition(Nav);
    }

    private void Nav_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedItem = null;
            return;
        }
        Point now = e.GetPosition(Nav);
        if (!_draggingNav)
        {
            if (Math.Abs(now.Y - _pressedAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _draggingNav = true;
            _pressedItem.Opacity = 0.6;
            Nav.CaptureMouse();
        }

        // The dragged item moves to the slot under the pointer as it goes.
        int target = SlotAt(now.Y);
        int current = Nav.Items.IndexOf(_pressedItem);
        if (target < 0 || target == current) return;
        _reorderingNav = true;
        try
        {
            object? selected = Nav.SelectedItem;
            Nav.Items.RemoveAt(current);
            Nav.Items.Insert(target, _pressedItem);
            Nav.SelectedItem = selected;
        }
        finally
        {
            _reorderingNav = false;
        }
    }

    /// <summary>The index of the menu item whose area contains the given height, or -1.</summary>
    private int SlotAt(double y)
    {
        for (int i = 0; i < Nav.Items.Count; i++)
        {
            if (Nav.Items[i] is not FrameworkElement item || item.ActualHeight <= 0) continue;
            Point top = item.TranslatePoint(new Point(0, 0), Nav);
            if (y < top.Y + item.ActualHeight || i == Nav.Items.Count - 1) return y < top.Y && i == 0 ? 0 : i;
        }
        return -1;
    }

    private void Nav_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingNav)
        {
            e.Handled = true;
            FinishNavDrag();
        }
        _pressedItem = null;
    }

    private void Nav_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggingNav) FinishNavDrag();
    }

    private void FinishNavDrag()
    {
        _draggingNav = false;
        if (_pressedItem is not null) _pressedItem.Opacity = 1;
        _pressedItem = null;
        if (Mouse.Captured == Nav) Nav.ReleaseMouseCapture();

        var order = Nav.Items.OfType<ListBoxItem>().Select(i => (string)i.Tag).ToList();
        if (order.SequenceEqual(_s.Config.Settings.NavOrder)) return;
        Try(() => _s.SetNavOrder(order));
    }
}
