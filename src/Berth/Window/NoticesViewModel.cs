using System.Collections.ObjectModel;
using System.IO;
using Berth.Core;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>
/// View model behind the stacked notice bars between the detail header and the position map (SPEC §9): orders errors
/// before warnings, keeps each bar's action bound to its own item, and wires Turn it off, Retry, and Open folder.
/// </summary>
internal sealed class NoticesViewModel : ObservableObject
{
    public const string AutoArrangeText =
        "Auto arrange icons is on. Windows ignores saved positions while it is.";

    public const string DesktopUnavailableText =
        "Berth cannot reach the desktop. Explorer may be restarting.";

    private readonly string? _layoutsFolder;
    private readonly Func<Task>? _turnAutoArrangeOffAsync;
    private readonly Func<Task>? _retryDesktopAsync;
    private readonly Action<string>? _openFolder;
    private readonly Func<bool>? _isBusyProvider;

    private IReadOnlyList<Notice> _currentNotices = [];
    private Task _lastActionTask = Task.CompletedTask;

    public NoticesViewModel(
        string? layoutsFolder = null,
        Func<Task>? turnAutoArrangeOffAsync = null,
        Func<Task>? retryDesktopAsync = null,
        Action<string>? openFolder = null,
        Func<bool>? isBusyProvider = null)
    {
        _layoutsFolder = layoutsFolder;
        _turnAutoArrangeOffAsync = turnAutoArrangeOffAsync;
        _retryDesktopAsync = retryDesktopAsync;
        _openFolder = openFolder;
        _isBusyProvider = isBusyProvider;
    }

    public ObservableCollection<NoticeItem> Items { get; } = [];

    public bool HasNotices => Items.Count > 0;

    internal Task LastActionTask => _lastActionTask;

    /// <summary>
    /// Computes the ordered SPEC §9 notices for the current desktop state and unreadable layout files (errors first:
    /// unreachable desktop, each unreadable layout file sorted by name, then auto arrange warning).
    /// </summary>
    public static IReadOnlyList<Notice> For(
        DesktopState desktop,
        IReadOnlyList<UnreadableFile> unreadable,
        string? layoutsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(unreadable);

        var list = new List<Notice>();

        if (desktop.Unavailable)
        {
            list.Add(new Notice(
                NoticeKind.DesktopUnavailable,
                IsError: true,
                Text: DesktopUnavailableText,
                ButtonText: "Retry"));
        }

        if (unreadable.Count > 0)
        {
            foreach (UnreadableFile file in unreadable.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase))
            {
                string fullPath = string.IsNullOrWhiteSpace(layoutsFolder)
                    ? file.FileName
                    : Path.Combine(layoutsFolder, file.FileName);
                list.Add(new Notice(
                    NoticeKind.UnreadableFile,
                    IsError: true,
                    Text: Messages.UnreadableLayout(file.FileName),
                    ButtonText: "Open folder",
                    FilePath: fullPath));
            }
        }

        if (!desktop.Unavailable && desktop.AutoArrange)
        {
            list.Add(new Notice(
                NoticeKind.AutoArrange,
                IsError: false,
                Text: AutoArrangeText,
                ButtonText: "Turn it off"));
        }

        return list;
    }

    /// <summary>Updates <see cref="Items"/> from a freshly applied desktop/store snapshot.</summary>
    public void Update(DesktopState desktop, IReadOnlyList<UnreadableFile> unreadable)
    {
        IReadOnlyList<Notice> next = For(desktop, unreadable, _layoutsFolder);
        if (_currentNotices.Count == next.Count && _currentNotices.SequenceEqual(next))
        {
            RaiseCanExecuteChanged();
            return;
        }

        _currentNotices = next;
        Items.Clear();
        foreach (Notice notice in next)
        {
            Notice captured = notice;
            var command = new RelayCommand(
                () => ExecuteNotice(captured),
                () => CanExecuteNotice(captured));
            Items.Add(new NoticeItem(captured, command));
        }

        RaisePropertyChanged(nameof(HasNotices));
    }

    public void RaiseCanExecuteChanged()
    {
        foreach (NoticeItem item in Items)
        {
            item.ActionCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanExecuteNotice(Notice notice) => notice.Kind switch
    {
        NoticeKind.AutoArrange => _turnAutoArrangeOffAsync is not null && !(_isBusyProvider?.Invoke() ?? false),
        NoticeKind.DesktopUnavailable => _retryDesktopAsync is not null,
        NoticeKind.UnreadableFile => _openFolder is not null && !string.IsNullOrWhiteSpace(notice.FilePath),
        _ => false,
    };

    private void ExecuteNotice(Notice notice)
    {
        switch (notice.Kind)
        {
            case NoticeKind.AutoArrange when _turnAutoArrangeOffAsync is not null:
                _lastActionTask = _turnAutoArrangeOffAsync();
                break;

            case NoticeKind.DesktopUnavailable when _retryDesktopAsync is not null:
                _lastActionTask = _retryDesktopAsync();
                break;

            case NoticeKind.UnreadableFile when _openFolder is not null && !string.IsNullOrWhiteSpace(notice.FilePath):
                _openFolder(notice.FilePath);
                _lastActionTask = Task.CompletedTask;
                break;
        }
    }
}
