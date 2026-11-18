namespace Berth.Core.Model;

/// <summary>One monitor of an arrangement, in physical pixels of the virtual desktop (SPEC §14 <c>monitors</c>).</summary>
internal sealed record MonitorInfo(int X, int Y, int Width, int Height, int Scale, bool Primary);
