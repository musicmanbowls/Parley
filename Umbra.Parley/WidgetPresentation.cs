using System.Text;
using Parley.Ipc;

namespace Umbra.Parley;

/// <summary>What one widget instance has been configured to show.</summary>
public sealed record WidgetOptions
{
    public const string StylePhone = "Phone";
    public const string StyleCompact = "Compact";
    public const string StyleTotal = "Total";

    public bool CountTells { get; init; } = true;
    public bool CountFreeCompany { get; init; } = true;
    public bool CountLinkshells { get; init; } = true;
    public bool CountCrossWorld { get; init; } = true;
    public string Style { get; init; } = StylePhone;
    public string IdleText { get; init; } = "Chat";
    public bool HideWhenIdle { get; init; }

    /// <summary>When every unread tell is from one person, say who instead of how many.</summary>
    public bool NameSingleSender { get; init; }
    public bool ShowPreviews { get; init; } = true;

    /// <summary>True when a left click closes an open window rather than only ever opening it.</summary>
    public bool ToggleOnClick { get; init; } = true;
}

public sealed record WidgetDisplay(string Text, string Tooltip, int Unread, bool Visible);

/// <summary>
/// Turns Parley's status into the text on the toolbar button. Kept apart from
/// the widget itself so it can be tested without Umbra or the game running.
/// </summary>
public static class WidgetPresentation
{
    private const int TooltipPreviewLength = 64;

    // Conversation kinds as the status snapshot numbers them.
    private const int Tell = 0;
    private const int Linkshell = 1;
    private const int CrossWorld = 2;
    private const int FreeCompany = 3;

    public static WidgetDisplay Create(StatusSnapshot? status, WidgetOptions options, string unavailableReason)
    {
        var controls = Controls(options);
        var idle = options.IdleText.Length > 0 ? options.IdleText : "Chat";

        if (status == null)
            return new WidgetDisplay(idle, $"{unavailableReason}\n\n{controls}", 0, !options.HideWhenIdle);

        if (!status.LoggedIn)
            return new WidgetDisplay(idle, $"Log in to see your messages.\n\n{controls}", 0, !options.HideWhenIdle);

        var counts = new Counts(
            options.CountTells ? status.Tells : 0,
            options.CountFreeCompany ? status.FreeCompany : 0,
            options.CountLinkshells ? status.Linkshells : 0,
            options.CountCrossWorld ? status.CrossWorld : 0);

        if (counts.Total == 0)
        {
            // Compact keeps its shape at zero: it reads as a row of gauges,
            // and gauges that vanish when empty look broken rather than idle.
            var text = options.Style == WidgetOptions.StyleCompact ? Compact(options, counts) : idle;
            return new WidgetDisplay(text, $"No new messages.\n\n{controls}", 0, !options.HideWhenIdle);
        }

        var label = options.Style switch
        {
            WidgetOptions.StyleCompact => Compact(options, counts),
            WidgetOptions.StyleTotal => counts.Total == 1 ? "1 new message" : $"{counts.Total} new messages",
            _ => Phone(status, options, counts),
        };

        return new WidgetDisplay(label, Tooltip(status, options, counts, controls), counts.Total, true);
    }

    private readonly record struct Counts(int Tells, int FreeCompany, int Linkshells, int CrossWorld)
    {
        public int Total => Tells + FreeCompany + Linkshells + CrossWorld;

        public int Kinds => (Tells > 0 ? 1 : 0) + (FreeCompany > 0 ? 1 : 0) + (Linkshells > 0 ? 1 : 0) + (CrossWorld > 0 ? 1 : 0);
    }

    private static string Phone(StatusSnapshot status, WidgetOptions options, Counts counts)
    {
        if (counts.Kinds == 1)
        {
            if (counts.Tells > 0)
            {
                var tells = counts.Tells;
                if (options.NameSingleSender && SingleTellSender(status, tells) is { } sender)
                    return tells == 1 ? $"Tell from {sender}" : $"{tells} Tells from {sender}";
                return tells == 1 ? "1 new Tell" : $"{tells} new Tells";
            }

            if (counts.FreeCompany > 0) return Messages(counts.FreeCompany, "FC");
            return counts.Linkshells > 0 ? Messages(counts.Linkshells, "LS") : Messages(counts.CrossWorld, "CWLS");
        }

        var parts = new List<string>(4);
        if (counts.Tells > 0) parts.Add(counts.Tells == 1 ? "1 Tell" : $"{counts.Tells} Tells");
        if (counts.FreeCompany > 0) parts.Add($"{counts.FreeCompany} FC");
        if (counts.Linkshells > 0) parts.Add($"{counts.Linkshells} LS");
        if (counts.CrossWorld > 0) parts.Add($"{counts.CrossWorld} CWLS");
        return string.Join(", ", parts);
    }

    private static string Messages(int count, string kind) => count == 1 ? $"1 new {kind} message" : $"{count} new {kind} messages";

    private static string Compact(WidgetOptions options, Counts counts)
    {
        var parts = new List<string>(4);
        if (options.CountTells) parts.Add($"T:{counts.Tells}");
        if (options.CountFreeCompany) parts.Add($"FC:{counts.FreeCompany}");
        if (options.CountLinkshells) parts.Add($"LS:{counts.Linkshells}");
        if (options.CountCrossWorld) parts.Add($"CW:{counts.CrossWorld}");
        return parts.Count == 0 ? "Parley" : string.Join(" ", parts);
    }

    /// <summary>The one person every unread tell is from, or null if there are several or the list is incomplete.</summary>
    private static string? SingleTellSender(StatusSnapshot status, int tells)
    {
        StatusConversation? only = null;
        foreach (var conversation in status.Conversations)
        {
            if (conversation.Group != Tell || conversation.Unread <= 0) continue;
            if (only != null) return null;
            only = conversation;
        }

        // The snapshot lists only the most recent few conversations. If this
        // one does not account for every unread tell, someone else is unlisted.
        return only != null && only.Unread == tells && only.Title.Length > 0 ? only.Title : null;
    }

    private static string Tooltip(StatusSnapshot status, WidgetOptions options, Counts counts, string controls)
    {
        var builder = new StringBuilder("Parley");
        var listed = 0;
        var listedUnread = 0;

        foreach (var conversation in status.Conversations)
        {
            if (!Includes(options, conversation.Group) || conversation.Unread <= 0) continue;

            var name = conversation.Group == Tell && conversation.World.Length > 0
                ? $"{conversation.Title}@{conversation.World}"
                : conversation.Title;
            builder.Append('\n').Append(Kind(conversation.Group)).Append(' ').Append(name)
                   .Append(" (").Append(conversation.Unread).Append(')');

            if (options.ShowPreviews && conversation.Preview.Length > 0)
            {
                builder.Append("\n    ");
                // In a group chat the speaker matters; in a tell it is the title again.
                if (conversation.Group != Tell && conversation.Sender.Length > 0)
                    builder.Append(conversation.Sender).Append(": ");
                builder.Append(Shorten(conversation.Preview));
            }

            listed++;
            listedUnread += conversation.Unread;
        }

        if (listed > 0 && counts.Total > listedUnread) builder.Append("\n… and ").Append(counts.Total - listedUnread).Append(" more");

        return builder.Append("\n\n").Append(controls).ToString();
    }

    private static bool Includes(WidgetOptions options, int group) => group switch
    {
        Tell => options.CountTells,
        Linkshell => options.CountLinkshells,
        CrossWorld => options.CountCrossWorld,
        FreeCompany => options.CountFreeCompany,
        _ => false,
    };

    private static string Kind(int group) => group switch
    {
        Tell => "Tell ·",
        Linkshell => "LS ·",
        FreeCompany => "FC ·",
        _ => "CWLS ·",
    };

    private static string Shorten(string text)
    {
        if (text.Length <= TooltipPreviewLength) return text;
        var cut = TooltipPreviewLength;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    private static string Controls(WidgetOptions options) =>
        (options.ToggleOnClick ? "Left-click: open or close the chat window" : "Left-click: open the chat window")
        + "\nRight-click: mark everything as read";
}
