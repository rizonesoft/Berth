using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>Notice categories from SPEC §9, ordered most serious first.</summary>
internal enum NoticeKind
{
    DesktopUnavailable,
    UnreadableFile,
    AutoArrange,
}

/// <summary>Pure description of one notice bar shown between the detail header and the position map (SPEC §9).</summary>
internal sealed record Notice(
    NoticeKind Kind,
    bool IsError,
    string Text,
    string ButtonText,
    string? FilePath = null);

/// <summary>Bindable item for one notice bar, carrying its icon glyph, accessibility severity name, and action command.</summary>
internal sealed class NoticeItem
{
    public const string ErrorGlyph = "\uEA39";
    public const string WarningGlyph = "\uE7BA";

    public NoticeItem(Notice notice, RelayCommand actionCommand)
    {
        Notice = notice ?? throw new ArgumentNullException(nameof(notice));
        ActionCommand = actionCommand ?? throw new ArgumentNullException(nameof(actionCommand));
    }

    public Notice Notice { get; }

    public NoticeKind Kind => Notice.Kind;

    public bool IsError => Notice.IsError;

    public string Text => Notice.Text;

    public string ButtonText => Notice.ButtonText;

    public string? FilePath => Notice.FilePath;

    public string Glyph => Notice.IsError ? ErrorGlyph : WarningGlyph;

    public string SeverityName => Notice.IsError ? "Error" : "Warning";

    public RelayCommand ActionCommand { get; }
}
