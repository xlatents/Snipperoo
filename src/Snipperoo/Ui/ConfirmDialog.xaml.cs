using System.Windows;

namespace Snipperoo.Ui;

/// <summary>Themed yes/no dialog.</summary>
internal partial class ConfirmDialog : ThemedWindow
{
    private ConfirmDialog(string heading, string message, string confirmText)
    {
        InitializeComponent();
        Title = heading;
        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    /// <summary>Returns true if the user confirmed. <paramref name="owner"/> may be null (no window open yet).</summary>
    public static bool Show(Window? owner, string heading, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(heading, message, confirmText);
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return dialog.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
