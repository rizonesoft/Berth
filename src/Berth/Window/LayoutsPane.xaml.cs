using System.Windows.Controls;
using System.Windows.Input;

namespace Berth.Window;

/// <summary>Left-hand layouts list pane (SPEC §5): heading, saved/unsaved layout rows, Active badge, and selection marker.</summary>
internal sealed partial class LayoutsPane : System.Windows.Controls.UserControl
{
    public LayoutsPane()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SPEC §5: exactly one row stays selected while rows exist; Ctrl-click in SelectionMode.Single must not clear it.
        if (LayoutsList.SelectedItem is null && DataContext is MainViewModel { Selected: { } preserved })
        {
            LayoutsList.SelectedItem = preserved;
        }
    }

    private void OnLayoutsPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        int count = LayoutsList.Items.Count;
        if (count == 0)
        {
            return;
        }

        int current = LayoutsList.SelectedIndex;
        int next = e.Key switch
        {
            Key.Down or Key.Right => current < 0 ? 0 : Math.Min(count - 1, current + 1),
            Key.Up or Key.Left => current < 0 ? count - 1 : Math.Max(0, current - 1),
            Key.Home => 0,
            Key.End => count - 1,
            _ => -1,
        };

        if (next < 0)
        {
            return;
        }

        LayoutsList.SelectedIndex = next;
        object item = LayoutsList.Items[next];
        LayoutsList.ScrollIntoView(item);
        LayoutsList.UpdateLayout();
        if (LayoutsList.ItemContainerGenerator.ContainerFromIndex(next) is ListBoxItem container)
        {
            container.Focus();
        }

        e.Handled = true;
    }
}
