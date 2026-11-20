// Windows SDK 10.0.26100.0 (Shlwapi.h)
// StrRetToStrW: https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-strrettostrw

using System.IO;
using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop.Interop;

namespace Berth.Desktop;

/// <summary>
/// Shell-backed implementation of <see cref="IDesktopIcons"/> that routes every shell call through one dedicated
/// <see cref="StaWorker"/> so callers on any thread never touch COM from an MTA thread.
/// </summary>
internal sealed partial class ShellDesktopIcons : IDesktopIcons, IDisposable
{
    private readonly StaWorker _worker;
    private readonly Func<DesktopView> _openView;
    private readonly IMonitors _monitors;
    private readonly Action<string>? _warn;
    private readonly Action<nint> _freeCoTaskMem;
    private readonly Action<object> _releaseComObject;
    private readonly Action<string>? _onMemberExecuted;

    public ShellDesktopIcons(IMonitors monitors, Log log)
        : this(
            DesktopView.Open,
            monitors ?? throw new ArgumentNullException(nameof(monitors)),
            (log ?? throw new ArgumentNullException(nameof(log))).Warn,
            Marshal.FreeCoTaskMem,
            DefaultReleaseComObject,
            onMemberExecuted: null)
    {
    }

    internal ShellDesktopIcons(
        Func<DesktopView> openView,
        Action<string>? onMemberExecuted = null,
        IMonitors? monitors = null,
        Action<string>? warn = null,
        Action<nint>? freeCoTaskMem = null,
        Action<object>? releaseComObject = null)
        : this(
            openView,
            monitors ?? new SystemMonitors(),
            warn,
            freeCoTaskMem ?? Marshal.FreeCoTaskMem,
            releaseComObject ?? DefaultReleaseComObject,
            onMemberExecuted)
    {
    }

    private ShellDesktopIcons(
        Func<DesktopView> openView,
        IMonitors monitors,
        Action<string>? warn,
        Action<nint> freeCoTaskMem,
        Action<object> releaseComObject,
        Action<string>? onMemberExecuted)
    {
        ArgumentNullException.ThrowIfNull(openView);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(freeCoTaskMem);
        ArgumentNullException.ThrowIfNull(releaseComObject);
        _openView = openView;
        _monitors = monitors;
        _warn = warn;
        _freeCoTaskMem = freeCoTaskMem;
        _releaseComObject = releaseComObject;
        _onMemberExecuted = onMemberExecuted;
        _worker = new StaWorker();
    }

    public IReadOnlyList<IconPosition> ReadIcons() =>
        RunMemberWithView(nameof(ReadIcons), ReadIconsFromView);

    internal IReadOnlyList<IconPosition> ReadIconsFrom(
        DesktopView desktop,
        Action<nint>? freeCoTaskMem = null,
        Action<object>? releaseComObject = null)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        return _worker.Run(() => ReadIconsFromView(
            desktop,
            freeCoTaskMem ?? _freeCoTaskMem,
            releaseComObject ?? _releaseComObject));
    }

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

    internal static IReadOnlyList<IconPosition> Deduplicate(IReadOnlyList<IconPosition> icons, Action<string>? warn)
    {
        ArgumentNullException.ThrowIfNull(icons);
        if (icons.Count <= 1)
        {
            return icons;
        }

        var seen = new HashSet<string>(icons.Count, StringComparer.OrdinalIgnoreCase);
        var result = new List<IconPosition>(icons.Count);
        foreach (IconPosition icon in icons)
        {
            if (seen.Add(icon.Id))
            {
                result.Add(icon);
            }
            else
            {
                warn?.Invoke($"Duplicate desktop icon parsing name ignored: \"{Messages.SingleLine(icon.Id)}\".");
            }
        }

        return result;
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

    private IReadOnlyList<IconPosition> ReadIconsFromView(DesktopView desktop) =>
        ReadIconsFromView(desktop, _freeCoTaskMem, _releaseComObject);

    private IReadOnlyList<IconPosition> ReadIconsFromView(
        DesktopView desktop,
        Action<nint> freeCoTaskMem,
        Action<object> releaseComObject)
    {
        IReadOnlyList<MonitorInfo> monitors = _monitors.Read();
        object? folderObj = null;
        object? enumObj = null;

        try
        {
            Guid folderIid = typeof(IShellFolder).GUID;
            desktop.View.GetFolder(in folderIid, out folderObj);
            if (folderObj is null)
            {
                throw new DesktopUnavailableException("Desktop folder view returned a null IShellFolder.");
            }

            var folder = (IShellFolder)folderObj;

            Guid enumIid = typeof(IEnumIDList).GUID;
            desktop.View.Items(ShellConstants.SVGIO_ALLVIEW, in enumIid, out enumObj);
            if (enumObj is null)
            {
                throw new DesktopUnavailableException("Desktop folder view returned a null IEnumIDList.");
            }

            var enumerator = (IEnumIDList)enumObj;
            var raw = new List<IconPosition>();
            var batch = new nint[1];

            while (true)
            {
                batch[0] = nint.Zero;
                int hr = enumerator.Next(1, batch, out uint fetched);
                if (hr < 0)
                {
                    Exception? hrEx = Marshal.GetExceptionForHR(hr);
                    throw hrEx is not null
                        ? Unavailable(hrEx)
                        : new DesktopUnavailableException($"IEnumIDList.Next failed (HRESULT 0x{hr:X8}).");
                }

                if (fetched == 0)
                {
                    break;
                }

                nint pidl = batch[0];
                if (pidl == nint.Zero)
                {
                    throw new DesktopUnavailableException("Desktop icon enumeration returned a null item identifier list.");
                }

                try
                {
                    desktop.View.GetItemPosition(pidl, out NativePoint viewPoint);
                    folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_FORPARSING, out StrRet parsingRet);
                    string id = DecodeStrRet(ref parsingRet, pidl, freeCoTaskMem);
                    if (string.IsNullOrEmpty(id))
                    {
                        throw new DesktopUnavailableException("Desktop icon returned an empty parsing name.");
                    }

                    folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_NORMAL, out StrRet displayRet);
                    string name = DecodeStrRet(ref displayRet, pidl, freeCoTaskMem);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = id;
                    }

                    (int x, int y) = DesktopCoordinates.ToDesktop(viewPoint.X, viewPoint.Y, monitors);
                    raw.Add(new IconPosition(id, name, x, y));
                }
                finally
                {
                    freeCoTaskMem(pidl);
                }
            }

            return Deduplicate(raw, _warn);
        }
        finally
        {
            if (enumObj is not null)
            {
                releaseComObject(enumObj);
            }

            if (folderObj is not null)
            {
                releaseComObject(folderObj);
            }
        }
    }

    private static string DecodeStrRet(ref StrRet strRet, nint pidl, Action<nint> freeCoTaskMem)
    {
        // Per Microsoft's StrRetToStrW contract, strRet is consumed by the call (including freeing STRRET_WSTR
        // pOleStr internally) and must not be freed by the caller afterwards; only ppsz is caller-owned.
        int hr = NativeMethods.StrRetToStrW(ref strRet, pidl, out nint ppsz);
        try
        {
            if (hr != ShellConstants.S_OK || ppsz == nint.Zero)
            {
                Exception? hrEx = hr < 0 ? Marshal.GetExceptionForHR(hr) : null;
                throw hrEx is not null
                    ? Unavailable(hrEx)
                    : new DesktopUnavailableException($"StrRetToStrW failed (HRESULT 0x{hr:X8}).");
            }

            return Marshal.PtrToStringUni(ppsz) ?? string.Empty;
        }
        finally
        {
            if (ppsz != nint.Zero)
            {
                freeCoTaskMem(ppsz);
            }
        }
    }

    private static bool ReadAutoArrange(IFolderView view, IFolderView2? view2)
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
                ? Unavailable(hrException)
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
            catch (FileNotFoundException ex)
            {
                throw Unavailable(ex);
            }
            catch (DirectoryNotFoundException ex)
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

    private static void DefaultReleaseComObject(object instance)
    {
        if (Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("shlwapi.dll")]
        internal static partial int StrRetToStrW(ref StrRet pstr, nint pidl, out nint ppsz);
    }
}
