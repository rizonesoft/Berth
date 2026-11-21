using System.Globalization;
using Berth.Core;
using Berth.Core.Model;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>Right-side action offered on the status line (SPEC §8).</summary>
internal enum StatusAction
{
    None,
    Undo,
    Retry,
}

/// <summary>
/// View model behind the status line card under the position map (SPEC §8): chooses the exact sentence and right-side
/// time or action (Undo / Retry) in SPEC §8 precedence order, keeping a just-finished sentence sticky until the next
/// operation, a layouts-pane selection change, or a refresh that finds a different comparison.
/// </summary>
internal sealed class StatusViewModel : ObservableObject
{
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly Func<Task>? _undoHandler;
    private readonly Func<WindowOperationRequest, Task>? _retryHandler;

    private bool _isBusy;
    private bool _hasStickyResult;
    private string _stickySentence = string.Empty;
    private StatusAction _stickyAction = StatusAction.None;
    private WindowOperationRequest? _retryRequest;

    private string? _lastSelectedKey;
    private ComparisonSignature? _lastComparisonSignature;
    private string _comparisonSentence = string.Empty;
    private string _comparisonRightText = string.Empty;

    private string _sentence = string.Empty;
    private string _rightText = string.Empty;
    private StatusAction _action = StatusAction.None;
    private Task _lastActionTask = Task.CompletedTask;

    public StatusViewModel(
        TimeProvider clock,
        TimeZoneInfo? timeZone = null,
        Func<Task>? undoHandler = null,
        Func<WindowOperationRequest, Task>? retryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _undoHandler = undoHandler;
        _retryHandler = retryHandler;
        ActionCommand = new RelayCommand(ExecuteAction, () => CanInvokeAction);
    }

    public string Sentence
    {
        get => _sentence;
        private set => Set(ref _sentence, value);
    }

    public string RightText
    {
        get => _rightText;
        private set
        {
            if (Set(ref _rightText, value))
            {
                RaisePropertyChanged(nameof(ShowRightText));
            }
        }
    }

    public StatusAction Action
    {
        get => _action;
        private set
        {
            if (Set(ref _action, value))
            {
                RaisePropertyChanged(nameof(ShowRightText));
                RaisePropertyChanged(nameof(ShowActionButton));
                RaisePropertyChanged(nameof(ActionButtonText));
                ActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowRightText => !string.IsNullOrEmpty(_rightText) && _action == StatusAction.None;

    public bool ShowActionButton => _action != StatusAction.None;

    public string ActionButtonText => _action switch
    {
        StatusAction.Undo => "Undo",
        StatusAction.Retry => "Retry",
        _ => string.Empty,
    };

    public RelayCommand ActionCommand { get; }

    internal Task LastActionTask => _lastActionTask;

    private bool CanInvokeAction =>
        !_isBusy
        && ((_action == StatusAction.Undo && _undoHandler is not null)
            || (_action == StatusAction.Retry && _retryHandler is not null && _retryRequest is not null));

    /// <summary>Switches the status line to "Working" (SPEC §8 row 1) and clears any earlier sticky sentence.</summary>
    public void SetBusy()
    {
        _isBusy = true;
        ClearStickyState();
        ApplyDisplay();
    }

    /// <summary>Updates busy state from combined window/tray busy state.</summary>
    public void SetBusyState(bool busy)
    {
        if (busy)
        {
            SetBusy();
            return;
        }

        if (_isBusy)
        {
            _isBusy = false;
            ApplyDisplay();
        }
    }

    /// <summary>
    /// Displays a completed, refused, or failed operation result (SPEC §8 rows 2-6). Successful Rename and Delete have
    /// no SPEC §8 status row and fall through to the refreshed comparison state.
    /// </summary>
    public void SetResult(
        OperationResult result,
        bool canUndo = false,
        WindowOperationRequest? retryRequest = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        _isBusy = false;
        if (result.Kind is OperationKind.Renamed or OperationKind.Deleted)
        {
            ClearStickyState();
            ApplyDisplay();
            return;
        }

        _hasStickyResult = true;
        _stickySentence = Messages.Status(result, _clock.GetUtcNow(), _timeZone);
        _retryRequest = null;

        if (result.Kind == OperationKind.Restored
            || (result.Kind == OperationKind.Failed
                && result.Failure is FailureKind.NotPlaced or FailureKind.DesktopUnavailable))
        {
            _stickyAction = canUndo ? StatusAction.Undo : StatusAction.None;
        }
        else if (result.Kind == OperationKind.Undone)
        {
            _stickyAction = StatusAction.None;
        }
        else if (OffersRetry(result) && retryRequest is not null)
        {
            _stickyAction = StatusAction.Retry;
            _retryRequest = retryRequest;
        }
        else
        {
            _stickyAction = StatusAction.None;
        }

        ApplyDisplay();
    }

    /// <summary>
    /// Publishes an explicit non-OperationResult status message (for example SPEC §9 "Windows did not turn auto arrange off").
    /// </summary>
    public void SetCustomMessage(string sentence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        _isBusy = false;
        _hasStickyResult = true;
        _stickySentence = sentence;
        _stickyAction = StatusAction.None;
        _retryRequest = null;
        ApplyDisplay();
    }

    /// <summary>
    /// Updates comparison-derived status (SPEC §8 rows 7-12). A sticky result stays until selection changes or the
    /// comparison itself changes; an unchanged refresh preserves the sticky sentence while updating live Undo eligibility.
    /// </summary>
    public void SetComparison(
        LayoutRow? selected,
        LayoutComparison? comparison,
        ComparisonKind kind,
        DesktopState desktop,
        bool canUndo = false)
    {
        string? newKey = selected?.Key;
        ComparisonSignature newSignature = ComparisonSignature.From(kind, comparison, desktop.Unavailable);

        bool selectionChanged = _lastSelectedKey is not null
            && !string.Equals(_lastSelectedKey, newKey, StringComparison.Ordinal);
        bool comparisonChanged = _lastComparisonSignature is not null
            && !_lastComparisonSignature.Equals(newSignature);

        _lastSelectedKey = newKey;
        _lastComparisonSignature = newSignature;

        (_comparisonSentence, _comparisonRightText) = FormatComparison(selected, comparison, kind, desktop);

        if (_hasStickyResult)
        {
            if (selectionChanged || comparisonChanged)
            {
                ClearStickyState();
            }
            else if (_stickyAction == StatusAction.Undo && !canUndo)
            {
                // Another Berth process consumed undo.json: preserve sticky sentence but drop the dead Undo button.
                _stickyAction = StatusAction.None;
            }
        }

        ApplyDisplay();
    }

    /// <summary>Determines whether SPEC §8 offers Retry for a refusal or failure (never for NotPlaced or user-action refusals).</summary>
    public static bool OffersRetry(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Kind == OperationKind.Refused)
        {
            return result.Refusal is Refusal.DesktopUnavailable or Refusal.Busy;
        }

        if (result.Kind == OperationKind.Failed)
        {
            // Mid-placement DesktopUnavailable preserves undo.json (Restore says "Undo puts back what was there."),
            // so offering Retry would overwrite the pre-restore snapshot. Pre-placement unavailability is Refusal.DesktopUnavailable.
            return result.Failure is FailureKind.WriteFailed
                or FailureKind.ReadFailed
                or FailureKind.DisplaysChanged
                or FailureKind.Unexpected;
        }

        return false;
    }

    private (string Sentence, string RightText) FormatComparison(
        LayoutRow? selected,
        LayoutComparison? comparison,
        ComparisonKind kind,
        DesktopState desktop)
    {
        if (selected is null)
        {
            return desktop.Unavailable
                ? (Messages.Refusal(OperationResult.Refused(Refusal.DesktopUnavailable)), string.Empty)
                : (string.Empty, string.Empty);
        }

        if (selected.IsUnsaved)
        {
            return desktop.Unavailable
                ? (Messages.Refusal(OperationResult.Refused(Refusal.DesktopUnavailable)), string.Empty)
                : (Messages.Unsaved, string.Empty);
        }

        if (selected.IsActive)
        {
            if (desktop.Unavailable || kind != ComparisonKind.Compared || comparison is null)
            {
                return (Messages.Refusal(OperationResult.Refused(Refusal.DesktopUnavailable)), string.Empty);
            }

            string sentence = Messages.Comparison(comparison.Moved, comparison.Missing);
            DateTimeOffset localChecked = TimeZoneInfo.ConvertTime(desktop.CheckedAt, _timeZone);
            string right = string.Create(CultureInfo.InvariantCulture, $"Checked {localChecked:HH:mm}");
            return (sentence, right);
        }

        Layout layout = selected.Layout!;
        return (Messages.OtherArrangement(layout.SavedAt, _clock.GetUtcNow(), _timeZone), string.Empty);
    }

    private void ClearStickyState()
    {
        _hasStickyResult = false;
        _stickySentence = string.Empty;
        _stickyAction = StatusAction.None;
        _retryRequest = null;
    }

    private void ApplyDisplay()
    {
        if (_isBusy)
        {
            Sentence = Messages.Working;
            RightText = string.Empty;
            Action = StatusAction.None;
            return;
        }

        if (_hasStickyResult)
        {
            Sentence = _stickySentence;
            RightText = string.Empty;
            Action = _stickyAction;
            return;
        }

        Sentence = _comparisonSentence;
        RightText = _comparisonRightText;
        Action = StatusAction.None;
    }

    private void ExecuteAction()
    {
        if (!CanInvokeAction)
        {
            return;
        }

        if (_action == StatusAction.Undo && _undoHandler is not null)
        {
            _lastActionTask = _undoHandler();
        }
        else if (_action == StatusAction.Retry && _retryHandler is not null && _retryRequest is { } req)
        {
            _lastActionTask = _retryHandler(req);
        }
    }

    private sealed class ComparisonSignature : IEquatable<ComparisonSignature>
    {
        private readonly ComparisonKind _kind;
        private readonly bool _unavailable;
        private readonly (string Id, int SavedX, int SavedY, IconState State, int? CurX, int? CurY)[] _icons;

        private ComparisonSignature(
            ComparisonKind kind,
            bool unavailable,
            (string Id, int SavedX, int SavedY, IconState State, int? CurX, int? CurY)[] icons)
        {
            _kind = kind;
            _unavailable = unavailable;
            _icons = icons;
        }

        public static ComparisonSignature From(ComparisonKind kind, LayoutComparison? comparison, bool unavailable)
        {
            if (comparison is null)
            {
                return new ComparisonSignature(kind, unavailable, []);
            }

            var items = new (string, int, int, IconState, int?, int?)[comparison.Icons.Count];
            for (int i = 0; i < comparison.Icons.Count; i++)
            {
                ComparedIcon icon = comparison.Icons[i];
                items[i] = (icon.Saved.Id, icon.Saved.X, icon.Saved.Y, icon.State, icon.Current?.X, icon.Current?.Y);
            }

            return new ComparisonSignature(kind, unavailable, items);
        }

        public bool Equals(ComparisonSignature? other)
        {
            if (other is null || _kind != other._kind || _unavailable != other._unavailable || _icons.Length != other._icons.Length)
            {
                return false;
            }

            for (int i = 0; i < _icons.Length; i++)
            {
                if (!string.Equals(_icons[i].Id, other._icons[i].Id, StringComparison.OrdinalIgnoreCase)
                    || _icons[i].SavedX != other._icons[i].SavedX
                    || _icons[i].SavedY != other._icons[i].SavedY
                    || _icons[i].State != other._icons[i].State
                    || _icons[i].CurX != other._icons[i].CurX
                    || _icons[i].CurY != other._icons[i].CurY)
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ComparisonSignature other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_kind, _unavailable, _icons.Length);
    }
}
