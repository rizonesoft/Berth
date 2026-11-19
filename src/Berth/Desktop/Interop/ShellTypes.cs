// Windows SDK 10.0.26100.0 (Windef.h, ShTypes.h)
// Microsoft Learn:
//   https://learn.microsoft.com/en-us/windows/win32/api/windef/ns-windef-point
//   https://learn.microsoft.com/en-us/windows/win32/api/shtypes/ns-shtypes-strret

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>Win32 <c>POINT</c> structure (signed 32-bit X and Y pixel coordinates).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

/// <summary>Inline 260-byte ANSI string buffer backing <see cref="StrRet.cStr"/>.</summary>
[InlineArray(260)]
internal struct StrRetBuffer
{
    private byte _element0;
}

/// <summary>
/// Win32 <c>STRRET</c> union returned by <c>IShellFolder.GetDisplayNameOf</c> under <c>&lt;pshpack8.h&gt;</c> on x64
/// (4-byte <c>uType</c> discriminator at offset 0, 4 bytes padding, then overlapping union at offset 8).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 272)]
internal struct StrRet
{
    [FieldOffset(0)]
    public uint uType;

    [FieldOffset(8)]
    public nint pOleStr;

    [FieldOffset(8)]
    public uint uOffset;

    [FieldOffset(8)]
    public StrRetBuffer cStr;
}
