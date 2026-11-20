using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Berth.Window;

/// <summary>Right-pane layout header with title, monitors line, action buttons, and More menu (SPEC §6).</summary>
internal sealed partial class DetailHeader : System.Windows.Controls.UserControl
{
    public DetailHeader()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.DataContext = e.NewValue;
            RevertMenuItem.GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();
        }
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.DataContext = DataContext;
        RevertMenuItem.GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
