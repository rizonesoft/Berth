using System.Windows;
using System.Windows.Input;

namespace Berth.Window;

/// <summary>Modal Fluent dialog for renaming a saved layout (SPEC §10).</summary>
internal sealed partial class RenameDialog : System.Windows.Window
{
    public RenameDialog(RenameViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
    }

    public RenameViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FocusAndSelectName();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        FocusAndSelectName();
    }

    internal void FocusAndSelectName()
    {
        NameBox.Focus();
        Keyboard.Focus(NameBox);
        NameBox.SelectAll();
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanRename)
        {
            return;
        }

        DialogResult = true;
    }

    private void OnDialogPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !ViewModel.CanRename)
        {
            e.Handled = true;
        }
    }
}
