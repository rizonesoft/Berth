using System.Globalization;
using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>Formats the one-line tray icon tooltip within <see cref="MaxTooltipLength"/> characters (SPEC §12).</summary>
internal static class TrayText
{
    /// <summary>The Windows <c>NotifyIcon.Text</c> hard length limit in UTF-16 code units.</summary>
    public const int MaxTooltipLength = 127;

    private const string Prefix = "Berth: ";
    private const string NoLayoutText = "Berth: no layout for this monitor arrangement";
    private const char Ellipsis = '\u2026';

    /// <summary>
    /// Formats <c>Berth: Docked, saved today, 09:14</c> or <c>Berth: no layout for this monitor arrangement</c>,
    /// shortening only the layout name with a single ellipsis character (<c>…</c>) on text-element boundaries when
    /// the whole tooltip would exceed <see cref="MaxTooltipLength"/> characters.
    /// </summary>
    public static string Tooltip(Layout? active, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (active is null)
        {
            return NoLayoutText;
        }

        string safeName = Messages.SingleLine(active.Name);
        string when = Formats.When(active.SavedAt, now, zone);
        string suffix = $", saved {when}";
        string full = Prefix + safeName + suffix;
        if (full.Length <= MaxTooltipLength)
        {
            return full;
        }

        int maxNameChars = MaxTooltipLength - Prefix.Length - suffix.Length - 1;
        if (maxNameChars <= 0)
        {
            return (Prefix + Ellipsis + suffix)[..Math.Min(MaxTooltipLength, Prefix.Length + 1 + suffix.Length)];
        }

        var truncated = new StringBuilder(maxNameChars);
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(safeName);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            if (truncated.Length + element.Length > maxNameChars)
            {
                break;
            }

            truncated.Append(element);
        }

        return Prefix + truncated + Ellipsis + suffix;
    }
}
