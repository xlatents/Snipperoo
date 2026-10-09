using System.Windows;
using System.Windows.Controls;

namespace Snipperoo.Ui;

/// <summary>Three cards for picking the Discord plan, i.e. the size limit clips are encoded to.</summary>
internal partial class PlanPicker : UserControl
{
    public PlanPicker() => InitializeComponent();

    public event Action<DiscordPlan>? PlanChanged;

    public DiscordPlan Plan
    {
        get => NitroCard.IsChecked == true ? DiscordPlan.Nitro
            : BasicCard.IsChecked == true ? DiscordPlan.NitroBasic
            : DiscordPlan.Free;
        set => CardFor(value).IsChecked = true;
    }

    private RadioButton CardFor(DiscordPlan plan) => plan switch
    {
        DiscordPlan.Nitro => NitroCard,
        DiscordPlan.NitroBasic => BasicCard,
        _ => FreeCard,
    };

    private void OnChecked(object sender, RoutedEventArgs e) => PlanChanged?.Invoke(Plan);
}
