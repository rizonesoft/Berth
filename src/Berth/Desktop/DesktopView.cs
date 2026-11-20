using System.IO;
using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Desktop.Interop;

namespace Berth.Desktop;

/// <summary>
/// Opens the Windows desktop's shell folder view through the ShellWindows COM chain on an STA thread and releases
/// every intermediate COM reference deterministically before returning, holding only the view reference until
/// <see cref="Dispose"/>.
/// </summary>
internal sealed class DesktopView : IDisposable
{
    private readonly Action<object> _releaseComObject;
    private object? _viewOwner;

    private DesktopView(
        object viewOwner,
        IFolderView view,
        IFolderView2? view2,
        Action<object> releaseComObject)
    {
        _viewOwner = viewOwner;
        _releaseComObject = releaseComObject;
        View = view;
        View2 = view2;
    }

    public IFolderView View { get; }

    public IFolderView2? View2 { get; }

    public static DesktopView Open() => Open(CreateShellWindows, DefaultReleaseComObject);

    internal static DesktopView Open(
        Func<object?> createShellWindows,
        Action<object>? releaseComObject = null)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("DesktopView.Open requires an STA thread.");
        }

        Action<object> release = releaseComObject ?? DefaultReleaseComObject;
        object? shellWindowsObj = null;
        object? dispatchObj = null;
        object? browserObj = null;
        object? shellViewObj = null;

        try
        {
            shellWindowsObj = createShellWindows()
                ?? throw new DesktopUnavailableException("ShellWindows activation returned null.");
            var shellWindows = (IShellWindows)shellWindowsObj;

            object? location = ShellConstants.CSIDL_DESKTOP;
            object? root = null;
            int hr = shellWindows.FindWindowSW(
                ref location,
                ref root,
                ShellConstants.SWC_DESKTOP,
                out _,
                ShellConstants.SWFO_NEEDDISPATCH,
                out dispatchObj);
            if (hr < 0)
            {
                Exception? hrException = Marshal.GetExceptionForHR(hr);
                throw hrException is not null
                    ? Unavailable(hrException)
                    : new DesktopUnavailableException(
                        $"Explorer desktop window is unavailable (FindWindowSW returned 0x{hr:X8}).");
            }

            if (hr != ShellConstants.S_OK || dispatchObj is null)
            {
                throw new DesktopUnavailableException(
                    $"Explorer desktop window is unavailable (FindWindowSW returned 0x{hr:X8}).");
            }

            object shellWindowsToRelease = shellWindowsObj;
            shellWindowsObj = null;
            release(shellWindowsToRelease);

            var serviceProvider = (Desktop.Interop.IServiceProvider)dispatchObj;
            Guid serviceId = ShellConstants.SID_STopLevelBrowser;
            Guid browserIid = typeof(IShellBrowser).GUID;
            serviceProvider.QueryService(in serviceId, in browserIid, out browserObj);
            if (browserObj is null)
            {
                throw new DesktopUnavailableException("Top-level shell browser service returned null.");
            }

            object dispatchToRelease = dispatchObj;
            dispatchObj = null;
            release(dispatchToRelease);

            var shellBrowser = (IShellBrowser)browserObj;
            shellBrowser.QueryActiveShellView(out IShellView shellView);
            shellViewObj = shellView
                ?? throw new DesktopUnavailableException("Active shell view returned null.");

            object browserToRelease = browserObj;
            browserObj = null;
            release(browserToRelease);

            var folderView = (IFolderView)shellViewObj;
            var folderView2 = shellViewObj as IFolderView2;
            object viewOwner = shellViewObj;
            shellViewObj = null;

            return new DesktopView(viewOwner, folderView, folderView2, release);
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
        finally
        {
            ReleaseSuppressingShellErrors(shellViewObj, release);
            ReleaseSuppressingShellErrors(browserObj, release);
            ReleaseSuppressingShellErrors(dispatchObj, release);
            ReleaseSuppressingShellErrors(shellWindowsObj, release);
        }
    }

    private static void ReleaseSuppressingShellErrors(object? instance, Action<object> release)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            release(instance);
        }
        catch (Exception ex) when (
            ex is COMException
                or InvalidCastException
                or InvalidComObjectException
                or UnauthorizedAccessException
                or FileNotFoundException
                or DirectoryNotFoundException)
        {
            // Preserve the acquisition exception already in flight and continue releasing remaining intermediates.
        }
    }

    public void Dispose()
    {
        object? owner = Interlocked.Exchange(ref _viewOwner, null);
        if (owner is not null)
        {
            _releaseComObject(owner);
        }
    }

    private static DesktopUnavailableException Unavailable(Exception inner) =>
        new("The Windows desktop folder view could not be reached.", inner);

    private static object? CreateShellWindows()
    {
        Type? comType = Type.GetTypeFromCLSID(ShellConstants.CLSID_ShellWindows, throwOnError: true);
        return comType is null ? null : Activator.CreateInstance(comType);
    }

    private static void DefaultReleaseComObject(object instance)
    {
        if (Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }
}
