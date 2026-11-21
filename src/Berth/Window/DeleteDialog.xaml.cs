using System.Windows;
using System.Windows.Input;
using Berth.Core;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>View model for the Delete layout confirmation dialog (SPEC §10).</summary>
internal sealed class DeleteDialogViewModel : ObservableObject
{
    private bool _isBusy;

    public DeleteDialogViewModel(string layoutName)
    {
        ArgumentNullException.ThrowIfNull(layoutName);
        Message = $"Delete the layout \"{layoutName}\"? Its saved positions are removed. This cannot be undone.";
    }

    public string Message { get; }

    public string BusyMessage => _isBusy
        ? Messages.Refusal(OperationResult.Refused(Refusal.Busy))
        : string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(BusyMessage));
                RaisePropertyChanged(nameof(CanDelete));
            }
        }
    }

    public bool CanDelete => !_isBusy;
}

/// <summary>Modal Fluent confirmation dialog for deleting a saved layout (SPEC §10), with Cancel as default.</summary>
internal sealed partial class DeleteDialog : System.Windows.Window
{
    private readonly Func<bool>? _tryAdmitDelete;

    public DeleteDialog(DeleteDialogViewModel viewModel, Func<bool>? tryAdmitDelete = null)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _tryAdmitDelete = tryAdmitDelete;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
    }

    public DeleteDialogViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FocusCancel();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        FocusCancel();
    }

    internal void FocusCancel()
    {
        CancelButton.Focus();
        Keyboard.Focus(CancelButton);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanDelete)
        {
            return;
        }

        if (_tryAdmitDelete is not null && !_tryAdmitDelete())
        {
            ViewModel.IsBusy = true;
            return;
        }

        DialogResult = true;
    }

    private void OnDialogPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            // Cancel is the default action (SPEC §10); Enter cancels even if Delete currently holds keyboard focus.
            DialogResult = false;
            e.Handled = true;
        }
    }
}
