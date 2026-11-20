// Windows SDK 10.0.26100.0 (Shlwapi.h)
// StrRetToStrW: https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-strrettostrw

using System.IO;
using System.Runtime.ExceptionServices;
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
        : this(monitors, log, DesktopView.Open)
    {
    }

    internal ShellDesktopIcons(
        IMonitors monitors,
        Log log,
        Func<DesktopView> openView,
        Action<nint>? freeCoTaskMem = null,
        Action<object>? releaseComObject = null)
        : this(
            openView,
            monitors ?? throw new ArgumentNullException(nameof(monitors)),
            (log ?? throw new ArgumentNullException(nameof(log))).Warn,
            freeCoTaskMem ?? Marshal.FreeCoTaskMem,
            releaseComObject ?? DefaultReleaseComObject,
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

    internal IReadOnlyList<IconPosition> ReadIcons(string namePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);
        return RunMemberWithView(nameof(ReadIcons), desktop =>
            ReadIconsFromView(desktop, _freeCoTaskMem, _releaseComObject, namePrefix));
    }

    public void SetPositions(IReadOnlyList<IconPosition> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        RunMemberWithView(nameof(SetPositions), desktop =>
        {
            SetPositionsOnView(desktop, icons, beforeMove: null);
            return 0;
        });
    }

    public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(beforeMove);
        RunMemberWithView(nameof(SetPositions), desktop =>
        {
            SetPositionsOnView(desktop, icons, beforeMove);
            return 0;
        });
    }

    internal void SetPositions(IReadOnlyList<IconPosition> icons, Action? beforeMove, string namePrefix)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);
        RunMemberWithView(nameof(SetPositions), desktop =>
        {
            SetPositionsOnView(desktop, icons, beforeMove, namePrefix);
            return 0;
        });
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
            DesktopView view = OpenViewChecked();
            T result;
            try
            {
                result = work(view);
            }
            catch
            {
                DisposeViewSuppressingShellErrors(view);
                throw;
            }

            view.Dispose();
            return result;
        });
    }

    public void Dispose() => _worker.Dispose();

    private void SetPositionsOnView(
        DesktopView desktop,
        IReadOnlyList<IconPosition> icons,
        Action? beforeMove,
        string? namePrefix = null)
    {
        if (icons.Count == 0)
        {
            InvokeBeforeMove(beforeMove);
            return;
        }

        IReadOnlyList<MonitorInfo> monitors = _monitors.Read();
        object? folderObj = null;
        object? enumObj = null;
        var allocatedPidls = new List<nint>();
        bool bodyCompleted = false;

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
            var pidlById = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
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

                if (namePrefix is not null)
                {
                    string scopedName;
                    try
                    {
                        folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_NORMAL, out StrRet scopedDisplayRet);
                        scopedName = DecodeStrRet(ref scopedDisplayRet, pidl, _freeCoTaskMem);
                    }
                    catch
                    {
                        _freeCoTaskMem(pidl);
                        throw;
                    }

                    if (!scopedName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _freeCoTaskMem(pidl);
                        continue;
                    }
                }

                allocatedPidls.Add(pidl);
                folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_FORPARSING, out StrRet parsingRet);
                string id = DecodeStrRet(ref parsingRet, pidl, _freeCoTaskMem);
                if (string.IsNullOrEmpty(id))
                {
                    throw new DesktopUnavailableException("Desktop icon returned an empty parsing name.");
                }

                pidlById.TryAdd(id, pidl);
            }

            var matchedPidls = new List<nint>(icons.Count);
            var matchedPoints = new List<NativePoint>(icons.Count);
            var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IconPosition icon in icons)
            {
                if (!matchedIds.Add(icon.Id))
                {
                    continue;
                }

                if (!pidlById.TryGetValue(icon.Id, out nint pidl))
                {
                    continue;
                }

                (int viewX, int viewY) = DesktopCoordinates.ToView(icon.X, icon.Y, monitors);
                matchedPidls.Add(pidl);
                matchedPoints.Add(new NativePoint { X = viewX, Y = viewY });
            }

            InvokeBeforeMove(beforeMove);

            if (matchedPidls.Count > 0)
            {
                desktop.View.SelectAndPositionItems(
                    (uint)matchedPidls.Count,
                    [.. matchedPidls],
                    [.. matchedPoints],
                    ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOSTATECHANGE);
            }

            bodyCompleted = true;
        }
        finally
        {
            foreach (nint pidl in allocatedPidls)
            {
                _freeCoTaskMem(pidl);
            }

            ReleaseComPair(enumObj, folderObj, _releaseComObject, suppressMappedShellErrors: !bodyCompleted);
        }
    }

    private IReadOnlyList<IconPosition> ReadIconsFromView(DesktopView desktop) =>
        ReadIconsFromView(desktop, _freeCoTaskMem, _releaseComObject, namePrefix: null);

    private IReadOnlyList<IconPosition> ReadIconsFromView(
        DesktopView desktop,
        Action<nint> freeCoTaskMem,
        Action<object> releaseComObject,
        string? namePrefix = null)
    {
        IReadOnlyList<MonitorInfo> monitors = _monitors.Read();
        object? folderObj = null;
        object? enumObj = null;
        bool bodyCompleted = false;

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
                    if (namePrefix is not null)
                    {
                        folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_NORMAL, out StrRet scopedDisplayRet);
                        string scopedName = DecodeStrRet(ref scopedDisplayRet, pidl, freeCoTaskMem);
                        if (!scopedName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        desktop.View.GetItemPosition(pidl, out NativePoint scopedViewPoint);
                        folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_FORPARSING, out StrRet scopedParsingRet);
                        string scopedId = DecodeStrRet(ref scopedParsingRet, pidl, freeCoTaskMem);
                        if (string.IsNullOrEmpty(scopedId))
                        {
                            throw new DesktopUnavailableException("Desktop icon returned an empty parsing name.");
                        }

                        (int scopedX, int scopedY) = DesktopCoordinates.ToDesktop(scopedViewPoint.X, scopedViewPoint.Y, monitors);
                        raw.Add(new IconPosition(scopedId, scopedName, scopedX, scopedY));
                        continue;
                    }

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

            IReadOnlyList<IconPosition> result = Deduplicate(raw, _warn);
            bodyCompleted = true;
            return result;
        }
        finally
        {
            ReleaseComPair(enumObj, folderObj, releaseComObject, suppressMappedShellErrors: !bodyCompleted);
        }
    }

    private static void ReleaseComPair(
        object? first,
        object? second,
        Action<object> release,
        bool suppressMappedShellErrors)
    {
        Exception? cleanupError = null;
        ReleaseOne(first, release, suppressMappedShellErrors, ref cleanupError);
        ReleaseOne(second, release, suppressMappedShellErrors, ref cleanupError);
        if (cleanupError is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }
    }

    private static void ReleaseOne(
        object? instance,
        Action<object> release,
        bool suppressMappedShellErrors,
        ref Exception? cleanupError)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            release(instance);
        }
        catch (Exception ex) when (suppressMappedShellErrors && IsMappedShellException(ex))
        {
            // Preserve the exception already in flight and still release remaining COM objects.
        }
        catch (Exception ex)
        {
            cleanupError ??= ex;
        }
    }

    internal static string DecodeStrRet(ref StrRet strRet, nint pidl, Action<nint> freeCoTaskMem)
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
            DesktopView view = OpenViewChecked();
            T result;
            try
            {
                result = work(view);
            }
            catch (BeforeMoveExceptionWrapper wrapper)
            {
                DisposeViewSuppressingShellErrors(view);
                ExceptionDispatchInfo.Capture(wrapper.Inner).Throw();
                throw;
            }
            catch (Exception ex) when (IsMappedShellException(ex))
            {
                DisposeViewSuppressingShellErrors(view);
                throw Unavailable(ex);
            }
            catch
            {
                DisposeViewSuppressingShellErrors(view);
                throw;
            }

            try
            {
                view.Dispose();
            }
            catch (Exception ex) when (IsMappedShellException(ex))
            {
                throw Unavailable(ex);
            }

            return result;
        });

    private static void DisposeViewSuppressingShellErrors(DesktopView view)
    {
        try
        {
            view.Dispose();
        }
        catch (Exception ex) when (IsMappedShellException(ex))
        {
            // Preserve the exception already in flight.
        }
    }

    private static bool IsMappedShellException(Exception ex) =>
        ex is COMException
            or InvalidCastException
            or InvalidComObjectException
            or UnauthorizedAccessException
            or FileNotFoundException
            or DirectoryNotFoundException;

    private DesktopView OpenViewChecked()
    {
        try
        {
            DesktopView? view = _openView();
            return view ?? throw new DesktopUnavailableException("The Windows desktop folder view factory returned null.");
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
    }

    private static void InvokeBeforeMove(Action? beforeMove)
    {
        if (beforeMove is null)
        {
            return;
        }

        try
        {
            beforeMove();
        }
        catch (Exception ex)
        {
            throw new BeforeMoveExceptionWrapper(ex);
        }
    }

    private sealed class BeforeMoveExceptionWrapper(Exception inner) : Exception
    {
        public Exception Inner { get; } = inner;
    }

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
