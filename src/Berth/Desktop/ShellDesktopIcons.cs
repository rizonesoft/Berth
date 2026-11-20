using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop.Interop;

namespace Berth.Desktop;

/// <summary>
/// Shell-backed implementation of <see cref="IDesktopIcons"/> that routes every shell call through one dedicated
/// <see cref="StaWorker"/> so callers on any thread never touch COM from an MTA thread.
/// </summary>
internal sealed class ShellDesktopIcons : IDesktopIcons, IDisposable
{
    private readonly StaWorker _worker;
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
        _worker = new StaWorker();
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
        RunMemberWithView(nameof(AutoArrange), static desktop => ReadAutoArrange(desktop.View, desktop.View2));

    public bool TryTurnAutoArrangeOff() =>
        RunMemberWithView(nameof(TryTurnAutoArrangeOff), ClearAutoArrange);

    internal static bool ReadAutoArrange(IFolderView view, IFolderView2? view2)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view2 is not null)
        {
            view2.GetCurrentFolderFlags(out uint flags);
            return (flags & ShellConstants.FWF_AUTOARRANGE) != 0;
        }

        int hr = view.GetAutoArrange();
        if (hr == ShellConstants.S_OK)
        {
            return true;
        }

        if (hr == ShellConstants.S_FALSE)
        {
            return false;
        }

        if (hr < 0)
        {
            Exception? hrException = Marshal.GetExceptionForHR(hr);
            throw hrException is not null
                ? new DesktopUnavailableException("The Windows desktop folder view could not be reached.", hrException)
                : new DesktopUnavailableException(
                    $"Could not read desktop auto arrange state (GetAutoArrange returned 0x{hr:X8}).");
        }

        throw new DesktopUnavailableException(
            $"Could not read desktop auto arrange state (GetAutoArrange returned 0x{hr:X8}).");
    }

    private static bool ClearAutoArrange(DesktopView desktop)
    {
        IFolderView2? view2 = desktop.View2;
        if (view2 is null)
        {
            return false;
        }

        view2.SetCurrentFolderFlags(ShellConstants.FWF_AUTOARRANGE, 0);
        view2.GetCurrentFolderFlags(out uint flags);
        return (flags & ShellConstants.FWF_AUTOARRANGE) == 0;
    }

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

    private T RunMemberWithView<T>(string memberName, Func<DesktopView, T> work) =>
        _worker.Run(() =>
        {
            _onMemberExecuted?.Invoke(memberName);
            using DesktopView view = _openView();
            try
            {
                return work(view);
            }
            catch (COMException ex)
            {
                throw Unavailable(ex);
            }
            catch (InvalidCastException ex)
            {
                throw Unavailable(ex);
            }
            catch (InvalidComObjectException ex)
            {
                throw Unavailable(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw Unavailable(ex);
            }
        });

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

    private static DesktopUnavailableException Unavailable(Exception inner) =>
        new("The Windows desktop folder view could not be reached.", inner);
}
