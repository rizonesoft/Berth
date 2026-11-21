using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;

namespace Berth.Window;

/// <summary>
/// Stacked notice bars between the detail header and the position map (SPEC §9): preserves keyboard focus on the
/// invoking notice button across busy/refusal round-trips and advances focus cleanly when an activated notice resolves.
/// </summary>
internal partial class Notices : System.Windows.Controls.UserControl
{
    private NoticesViewModel? _viewModel;
    private string? _pendingRestoreButtonText;
    private string? _lastFocusedButtonText;

    public Notices()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnDescendantGotKeyboardFocus), handledEventsToo: true);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Items.CollectionChanged -= OnItemsCollectionChanged;
        }

        _viewModel = e.NewValue as NoticesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Items.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void OnDescendantGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is WpfButton button && button.Content is string text && !string.IsNullOrWhiteSpace(text))
        {
            _lastFocusedButtonText = text;
        }
    }

    private void OnNoticeButtonIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not WpfButton button || button.Content is not string text)
        {
            return;
        }

        if (e.NewValue is false)
        {
            if (button.IsKeyboardFocused || string.Equals(_lastFocusedButtonText, text, StringComparison.Ordinal))
            {
                _pendingRestoreButtonText = text;
            }

            return;
        }

        if (e.NewValue is true && string.Equals(_pendingRestoreButtonText, text, StringComparison.Ordinal))
        {
            _pendingRestoreButtonText = null;
            if (IsFocusUnassignedOrOnWindow())
            {
                button.Focus();
            }
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        string? pending = _pendingRestoreButtonText;
        if (pending is null || _viewModel is null)
        {
            return;
        }

        bool stillPresent = _viewModel.Items.Any(i => string.Equals(i.ButtonText, pending, StringComparison.Ordinal));
        if (stillPresent)
        {
            return;
        }

        _pendingRestoreButtonText = null;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!IsFocusUnassignedOrOnWindow())
                {
                    return;
                }

                // Move focus cleanly to the first remaining notice button or the next detail region after notices.
                WpfButton? nextNoticeButton = FindFirstEnabledNoticeButton(this);
                if (nextNoticeButton is not null)
                {
                    nextNoticeButton.Focus();
                    return;
                }

                FocusNextRegionAfterNotices();
            }));
    }

    private bool IsFocusUnassignedOrOnWindow()
    {
        IInputElement? focused = Keyboard.FocusedElement;
        return focused is null || focused is System.Windows.Window || IsKeyboardFocusWithin;
    }

    private void FocusNextRegionAfterNotices()
    {
        // Parent hierarchy in MainWindow: Notices -> NoticesHost (TabIndex 0) -> DetailGrid -> DetailHeaderControl (TabIndex 1).
        DependencyObject? host = VisualTreeHelper.GetParent(this);
        while (host is not null && VisualTreeHelper.GetParent(host) is not Grid)
        {
            host = VisualTreeHelper.GetParent(host);
        }

        if (host is null || VisualTreeHelper.GetParent(host) is not Grid detailGrid)
        {
            return;
        }

        int myTabIndex = KeyboardNavigation.GetTabIndex(host);
        UIElement? nextHost = detailGrid.Children
            .OfType<UIElement>()
            .Where(c => c.IsVisible && KeyboardNavigation.GetTabIndex(c) > myTabIndex)
            .OrderBy(KeyboardNavigation.GetTabIndex)
            .FirstOrDefault();

        nextHost?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private static WpfButton? FindFirstEnabledNoticeButton(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is WpfButton button && button.IsVisible && button.IsEnabled)
            {
                return button;
            }

            WpfButton? nested = FindFirstEnabledNoticeButton(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
