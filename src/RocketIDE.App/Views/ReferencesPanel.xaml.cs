using System.Windows.Controls;
using System.Windows.Input;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Views;

public sealed class ReferenceInvokedEventArgs(ReferenceItemViewModel item) : EventArgs
{
    public ReferenceItemViewModel Item { get; } = item ?? throw new ArgumentNullException(nameof(item));
}

public partial class ReferencesPanel : UserControl
{
    public ReferencesPanel() => InitializeComponent();

    public event EventHandler<ReferenceInvokedEventArgs>? ReferenceInvoked;

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ReferenceItemViewModel item)
        {
            ReferenceInvoked?.Invoke(this, new ReferenceInvokedEventArgs(item));
            e.Handled = true;
        }
    }

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.SelectedItem is ReferenceItemViewModel item)
        {
            ReferenceInvoked?.Invoke(this, new ReferenceInvokedEventArgs(item));
            e.Handled = true;
        }
    }
}
