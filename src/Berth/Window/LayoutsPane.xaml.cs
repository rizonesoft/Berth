using System.Windows.Controls;

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
}
