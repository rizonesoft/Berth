using Berth.Core;
using Berth.Core.Model;

namespace Berth.Desktop;

/// <summary>
/// Shell-backed implementation of <see cref="IDesktopIcons"/> that routes every shell call through one dedicated
/// <see cref="StaWorker"/> so callers on any thread never touch COM from an MTA thread.
/// </summary>
internal sealed class ShellDesktopIcons : IDesktopIcons, IDisposable
{
    private readonly StaWorker _worker = new();
    private readonly Func<DesktopView> _openView;
    private readonly Action<string>? _onMemberExecuted;

    public ShellDesktopIcons()
        : this(DesktopView.Open, onMemberExecuted: null)
    {
    }

    internal ShellDesktopIcons(Func<DesktopView> openView, Action<string>? onMemberExecuted = null)
    {
        ArgumentNullException.ThrowIfNull(openView);
        _openView = openView;
        _onMemberExecuted = onMemberExecuted;
    }

    public IReadOnlyList<IconPosition> ReadIcons() =>
        RunMember<IReadOnlyList<IconPosition>>(nameof(ReadIcons), static () =>
            throw new NotImplementedException("ShellDesktopIcons.ReadIcons is implemented in D02 T01 §3."));

    public void SetPositions(IReadOnlyList<IconPosition> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        RunMember(nameof(SetPositions), static () =>
            throw new NotImplementedException("ShellDesktopIcons.SetPositions is implemented in D02 T01 §4."));
    }

    public bool AutoArrange =>
        RunMember<bool>(nameof(AutoArrange), static () =>
            throw new NotImplementedException("ShellDesktopIcons.AutoArrange is implemented in D02 T01 §5."));

    public bool TryTurnAutoArrangeOff() =>
        RunMember<bool>(nameof(TryTurnAutoArrangeOff), static () =>
            throw new NotImplementedException("ShellDesktopIcons.TryTurnAutoArrangeOff is implemented in D02 T01 §5."));

    internal T RunOnWorker<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _worker.Run(work);
    }

    internal T RunWithView<T>(Func<DesktopView, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _worker.Run(() =>
        {
            using DesktopView view = _openView();
            return work(view);
        });
    }

    public void Dispose() => _worker.Dispose();

    private T RunMember<T>(string memberName, Func<T> work) =>
        _worker.Run(() =>
        {
            _onMemberExecuted?.Invoke(memberName);
            return work();
        });

    private void RunMember(string memberName, Action work) =>
        _worker.Run(() =>
        {
            _onMemberExecuted?.Invoke(memberName);
            work();
        });
}
