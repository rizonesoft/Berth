using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Berth.Core.Model;

namespace Berth.Window;

/// <summary>Right-pane layout header with title, monitors line, action buttons, and More menu (SPEC §6).</summary>
internal sealed partial class DetailHeader : System.Windows.Controls.UserControl
{
    private Layout? _menuTargetLayout;

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

        _menuTargetLayout = (DataContext as DetailViewModel)?.SelectedSavedLayout;
        menu.DataContext = DataContext;
        RevertMenuItem.GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private Layout? ConsumeMenuTarget(DetailViewModel detail)
    {
        Layout? target = _menuTargetLayout ?? detail.SelectedSavedLayout;
        _menuTargetLayout = null;
        return target;
    }

    private void OnRenameMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DetailViewModel detail || ConsumeMenuTarget(detail) is not { } target)
        {
            return;
        }

        string capturedKey = target.Key;
        var renameVm = new RenameViewModel(target.Name, detail.GetOtherSavedNames(capturedKey))
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
        if (DataContext is not DetailViewModel detail || ConsumeMenuTarget(detail) is not { } target)
        {
            return;
        }

        _ = detail.RevertAsync(target.Key, target);
    }

    private void OnDeleteMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DetailViewModel detail || ConsumeMenuTarget(detail) is not { } target)
        {
            return;
        }

        var deleteVm = new DeleteDialogViewModel(target.Name)
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
                () => detail.TryStartDelete(target.Key, target));
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
