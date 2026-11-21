using System.Globalization;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>Title and body text for a Berth notification-area failure balloon (SPEC §12, §16).</summary>
internal sealed record BalloonMessage(string Title, string Text);

/// <summary>
/// Pure policy deciding whether an automatic restore result shows Berth's one notification balloon (SPEC §12, §16, §18).
/// Success (including missing saved icons) and <see cref="Refusal.NoLayout"/> are silent; every other refusal or failure
/// shows the warning balloon with the exact §18 or §8 partial sentence, shortened within Windows's 255-character
/// <c>NOTIFYICONDATA.szInfo</c> limit without dropping the reason or closing guidance.
/// </summary>
internal static class BalloonPolicy
{
    public const string FailureTitle = "Berth could not restore your icons";
    public const int MaxBodyLength = 255;

    private const string DetailsInLogSuffix = ". The details are in the log.";
    private const string UnreadableSuffix = " could not be read. It was left untouched.";
    private const char Ellipsis = '\u2026';

    public static BalloonMessage? For(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Kind == OperationKind.Restored)
        {
            return null;
        }

        if (result.Kind == OperationKind.Refused && result.Refusal == Refusal.NoLayout)
        {
            return null;
        }

        string text = FitBody(Messages.Refusal(result));
        return new BalloonMessage(FailureTitle, text);
    }

    internal static string FitBody(string text)
    {
        if (text.Length <= MaxBodyLength)
        {
            return text;
        }

        if (text.EndsWith(DetailsInLogSuffix, StringComparison.Ordinal))
        {
            string suffix = Ellipsis + DetailsInLogSuffix;
            int budget = MaxBodyLength - suffix.Length;
            string head = text[..^DetailsInLogSuffix.Length];
            return TruncateOnTextElements(head, budget) + suffix;
        }

        if (text.EndsWith(UnreadableSuffix, StringComparison.Ordinal))
        {
            string suffix = Ellipsis + UnreadableSuffix;
            int budget = MaxBodyLength - suffix.Length;
            string head = text[..^UnreadableSuffix.Length];
            return TruncateOnTextElements(head, budget) + suffix;
        }

        // Preserve ": <reason>." on "Berth could not write <path>: <reason>." by shortening <path> first.
        int reasonSeparator = text.LastIndexOf(": ", StringComparison.Ordinal);
        if (reasonSeparator > 0 && text.EndsWith('.'))
        {
            string reasonTail = text[reasonSeparator..]; // ": <reason>."
            if (reasonTail.Length + 24 <= MaxBodyLength)
            {
                string suffix = Ellipsis + reasonTail;
                int budget = MaxBodyLength - suffix.Length;
                string head = text[..reasonSeparator];
                return TruncateOnTextElements(head, budget) + suffix;
            }
        }

        return TruncateOnTextElements(text, MaxBodyLength - 1) + Ellipsis;
    }

    private static string TruncateOnTextElements(string value, int maxLength)
    {
        if (maxLength <= 0 || value.Length == 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        int lastSafeEnd = 0;
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            int nextEnd = enumerator.ElementIndex + enumerator.GetTextElement().Length;
            if (nextEnd > maxLength)
            {
                break;
            }

            lastSafeEnd = nextEnd;
        }

        return value[..lastSafeEnd].TrimEnd();
    }
}
