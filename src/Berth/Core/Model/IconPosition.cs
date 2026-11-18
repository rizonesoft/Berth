namespace Berth.Core.Model;

/// <summary>
/// One desktop icon: <see cref="Id"/> is its parsing name, which identifies it, and <see cref="Name"/> its display
/// name, which is only shown (SPEC §14 <c>icons</c>, §15 "Matching"). The position is in physical pixels of the
/// virtual desktop.
/// </summary>
internal sealed record IconPosition(string Id, string Name, int X, int Y);
