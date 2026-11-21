using Berth.Core;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>
/// Pure view model for the Rename layout dialog (SPEC §10): validates on every change via <see cref="LayoutNames.Validate"/>,
/// updates live if other saved layouts change while the dialog is open, and blocks submission while Berth is busy.
/// </summary>
internal sealed class RenameViewModel : ObservableObject
{
    private List<string> _others;
    private string _name;
    private string _validationError = string.Empty;
    private bool _isBusy;

    public RenameViewModel(string current, IEnumerable<string> others)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(others);

        _others = [.. others];
        _name = current;
        Validate();
    }

    public string Name
    {
        get => _name;
        set
        {
            string next = value ?? string.Empty;
            if (Set(ref _name, next))
            {
                Validate();
            }
        }
    }

    public string TrimmedName => LayoutNames.Trimmed(_name);

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(Error));
                RaisePropertyChanged(nameof(HasError));
                RaisePropertyChanged(nameof(CanRename));
            }
        }
    }

    public string Error => !string.IsNullOrEmpty(_validationError)
        ? _validationError
        : (_isBusy ? Messages.Refusal(OperationResult.Refused(Refusal.Busy)) : string.Empty);

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool CanRename => !_isBusy && string.IsNullOrEmpty(_validationError);

    /// <summary>
    /// Refreshes the set of other saved layout names while the dialog stays open without touching the user's draft.
    /// </summary>
    public void UpdateOthers(IEnumerable<string> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        _others = [.. others];
        Validate();
    }

    private void Validate()
    {
        NameError problem = LayoutNames.Validate(_name, _others);
        string nextError = problem == NameError.None
            ? string.Empty
            : Messages.NameProblem(problem);

        if (!string.Equals(_validationError, nextError, StringComparison.Ordinal))
        {
            _validationError = nextError;
            RaisePropertyChanged(nameof(Error));
            RaisePropertyChanged(nameof(HasError));
            RaisePropertyChanged(nameof(CanRename));
        }
    }
}
