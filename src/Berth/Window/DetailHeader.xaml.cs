using System.Collections.Specialized;
using System.ComponentModel;
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

    private void OnRenameMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DetailViewModel detail || detail.SelectedSavedKey is not { } capturedKey)
        {
            return;
        }

        var renameVm = new RenameViewModel(detail.Title, detail.GetOtherSavedNames(capturedKey))
        {
            IsBusy = detail.IsBusy,
        };

        void OnRowsChanged(object? _, NotifyCollectionChangedEventArgs __) =>
            renameVm.UpdateOthers(detail.GetOtherSavedNames(capturedKey));

        void OnDetailPropertyChanged(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(DetailViewModel.IsBusy) or "")
            {
                renameVm.IsBusy = detail.IsBusy;
            }
        }

        detail.RowsCollection.CollectionChanged += OnRowsChanged;
        detail.PropertyChanged += OnDetailPropertyChanged;
        try
        {
            var dialog = new RenameDialog(
                renameVm,
                trimmedName => detail.TryStartRename(capturedKey, trimmedName));
            System.Windows.Window? owner = System.Windows.Window.GetWindow(this);
            if (owner is not null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            _ = dialog.ShowDialog();
        }
        finally
        {
            detail.RowsCollection.CollectionChanged -= OnRowsChanged;
            detail.PropertyChanged -= OnDetailPropertyChanged;
        }
    }

    private void OnRevertMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DetailViewModel detail || detail.SelectedSavedKey is not { } capturedKey)
        {
            return;
        }

        _ = detail.RevertAsync(capturedKey);
    }

    private void OnDeleteMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DetailViewModel detail || detail.SelectedSavedKey is not { } capturedKey)
        {
            return;
        }

        string capturedName = detail.Title;
        var deleteVm = new DeleteDialogViewModel(capturedName)
        {
            IsBusy = detail.IsBusy,
        };

        void OnDetailPropertyChanged(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(DetailViewModel.IsBusy) or "")
            {
                deleteVm.IsBusy = detail.IsBusy;
            }
        }

        detail.PropertyChanged += OnDetailPropertyChanged;
        try
        {
            var dialog = new DeleteDialog(
                deleteVm,
                () => detail.TryStartDelete(capturedKey, capturedName));
            System.Windows.Window? owner = System.Windows.Window.GetWindow(this);
            if (owner is not null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            _ = dialog.ShowDialog();
        }
        finally
        {
            detail.PropertyChanged -= OnDetailPropertyChanged;
        }
    }
}
