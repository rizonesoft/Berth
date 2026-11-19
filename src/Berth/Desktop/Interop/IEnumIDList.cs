// Windows SDK 10.0.26100.0 (ShObjIdl_core.h)
// IID_IEnumIDList = 000214F2-0000-0000-C000-000000000046
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ienumidlist

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>COM vtable declaration for <c>IEnumIDList</c> (vtbl slots 3 through 6).</summary>
[ComImport]
[Guid("000214F2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumIDList
{
    [PreserveSig]
    int Next(uint celt, out nint rgelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    void Reset();

    void Clone([MarshalAs(UnmanagedType.Interface)] out IEnumIDList ppenum);
}
