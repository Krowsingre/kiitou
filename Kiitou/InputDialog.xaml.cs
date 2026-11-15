using System.Windows;

namespace Kiitou;

/// <summary>A small prompt for one line of text.</summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    /// <summary>Returns the entered text, or null if the user cancelled.</summary>
    public static string? Prompt(Window owner, string title, string prompt, string initial = "", string hint = "")
    {
        var dialog = new InputDialog { Owner = owner, Title = title };
        dialog.PromptText.Text = prompt;
        dialog.ValueBox.Text = initial;
        dialog.HintText.Text = hint;
        dialog.HintText.Visibility = hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        return dialog.ShowDialog() == true ? dialog.ValueBox.Text : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
