// Windows SDK 10.0.26100.0 (servprov.h)
// IID_IServiceProvider = 6D5140C1-7436-11CE-8034-00AA006009FA
// Microsoft Learn: https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/cc678965(v=vs.85)

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>COM vtable declaration for <c>IServiceProvider</c>.</summary>
[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    void QueryService(
        in Guid guidService,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppvObject);
}
