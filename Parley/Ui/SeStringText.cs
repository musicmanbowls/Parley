using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Parley.Core;
using Parley.Core.Text;

namespace Parley.Ui;

/// <summary>
/// Turns a message as the game sent it (an encoded SeString) into the
/// <see cref="RichText"/> Parley draws: its text, the game's colours for each
/// part, and its links: items, map flags, players, party finder listings.
/// </summary>
internal static class SeStringText
{
    public static RichText For(ChatMessage message)
    {
        if (message.Rich is { Length: > 0 } rich)
        {
            try
            {
                return Parse(rich);
            }
            catch (Exception ex)
            {
                // Formatting that cannot be read costs the formatting, never the message.
                Services.Log.Verbose(ex, "Could not read a message's formatting.");
                message.Rich = null;
            }
        }

        return RichText.Plain(message.Text);
    }

    public static RichText Parse(byte[] encoded)
    {
        var builder = new RichText.Builder();
        var colours = new Stack<ushort>();

        foreach (var payload in SeString.Parse(encoded).Payloads)
        {
            var colour = colours.Count > 0 ? colours.Peek() : (ushort)0;
            switch (payload)
            {
                case TextPayload text:
                    builder.Append(text.Text ?? string.Empty, colour);
                    break;

                case UIForegroundPayload foreground:
                    // The game pairs each colour with a "colour off" that
                    // returns to whatever was in force before it.
                    if (foreground.ColorKey == 0)
                    {
                        if (colours.Count > 0) colours.Pop();
                    }
                    else
                    {
                        colours.Push(foreground.ColorKey);
                    }
                    break;

                case ItemPayload item:
                    builder.BeginLink(new TextLink { Kind = LinkKind.Item, Id = item.RawItemId });
                    break;

                case MapLinkPayload map:
                    builder.BeginLink(new TextLink { Kind = LinkKind.Map, Payload = map });
                    break;

                case PlayerPayload player:
                    var world = player.World.RowId;
                    builder.BeginLink(new TextLink
                    {
                        Kind = LinkKind.Player, Name = player.PlayerName, World = world <= ushort.MaxValue ? (ushort)world : (ushort)0,
                    });
                    break;

                case PartyFinderPayload listing:
                    builder.BeginLink(new TextLink { Kind = LinkKind.PartyFinder, Id = listing.ListingId });
                    break;

                case QuestPayload or StatusPayload or DalamudLinkPayload:
                    builder.BeginLink(new TextLink { Kind = LinkKind.Other });
                    break;

                case RawPayload raw when raw.Equals(RawPayload.LinkTerminator):
                    builder.EndLink();
                    break;

                case IconPayload icon:
                    builder.AppendObject(icon.Encode(), colour);
                    break;

                case NewLinePayload:
                    builder.Append("\n", colour);
                    break;

                case ITextProvider provider:
                    builder.Append(provider.Text ?? string.Empty, colour);
                    break;
            }
        }

        return builder.Build();
    }
}
