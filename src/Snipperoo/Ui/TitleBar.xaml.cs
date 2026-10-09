using System.Windows;
using System.Windows.Controls;

namespace Snipperoo.Ui;

/// <summary>Logo, caption and close button. The window's WindowChrome makes this strip draggable.</summary>
internal partial class TitleBar : UserControl
{
    public const double BarHeight = 48;

    public TitleBar() => InitializeComponent();

    public string Caption
    {
        get => CaptionText.Text;
        set => CaptionText.Text = value;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
