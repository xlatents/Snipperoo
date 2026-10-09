using System.Windows;
using System.Windows.Controls;

namespace Snipperoo.Ui;

/// <summary>A settings row: icon, title, description and an on/off switch. The whole row is clickable.</summary>
internal partial class SwitchRow : UserControl
{
    public SwitchRow() => InitializeComponent();

    /// <summary>Raised when the user flips the switch (not when <see cref="IsOn"/> is set from code).</summary>
    public event Action<bool>? Toggled;

    public string Glyph { get => GlyphText.Text; set => GlyphText.Text = value; }
    public string Title { get => TitleText.Text; set => TitleText.Text = value; }
    public string Description { get => DescriptionText.Text; set => DescriptionText.Text = value; }

    public bool IsOn
    {
        get => Toggle.IsChecked == true;
        set => Toggle.IsChecked = value;
    }

    private void OnClick(object sender, RoutedEventArgs e) => Toggled?.Invoke(IsOn);
}
