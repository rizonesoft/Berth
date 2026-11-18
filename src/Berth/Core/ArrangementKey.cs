using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Berth.Core.Model;

namespace Berth.Core;

/// <summary>
/// The key of a monitor arrangement: the same monitors in the same places at the same scale always give the same
/// key, which names the layout file (SPEC §14, last bullet).
/// </summary>
internal static class ArrangementKey
{
    /// <summary>
    /// The first 16 lowercase hexadecimal characters of the SHA-256 of each monitor written <c>x,y,width,height,scale</c>
    /// with invariant decimal integers, sorted by <c>x</c> then <c>y</c> (then the rest, so the order is total) and
    /// joined by <c>;</c>. <c>Primary</c> does not take part.
    /// </summary>
    /// <exception cref="ArgumentException">There is no monitor.</exception>
    public static string From(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("An arrangement has at least one monitor.", nameof(monitors));
        }

        string text = string.Join(
            ';',
            monitors
                .OrderBy(m => m.X)
                .ThenBy(m => m.Y)
                .ThenBy(m => m.Width)
                .ThenBy(m => m.Height)
                .ThenBy(m => m.Scale)
                .Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.X},{m.Y},{m.Width},{m.Height},{m.Scale}")));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash)[..16];
    }
}
