using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// Listens to the game's chat log and files tells, free company, linkshell and
/// cross-world linkshell messages into conversations. It also watches for the
/// error lines that mean a message just sent did not go through.
/// </summary>
internal sealed class ChatCapture : IDisposable
{
    /// <summary>A formatted message larger than this is kept as plain text only.</summary>
    private const int MaxRichBytes = 4096;

    private const char PrivateUseFirst = '';
    private const char PrivateUseLast = '';

    private readonly Plugin plugin;
    private readonly ChatBubbles bubbles = new();

    public ChatCapture(Plugin plugin)
    {
        this.plugin = plugin;
        Services.Chat.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        Services.Chat.ChatMessage -= OnChatMessage;
        bubbles.Dispose();
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // This runs inside the game's chat handling, with every other plugin's
        // handler queued behind it, so nothing may escape. Dalamud also reuses
        // the message object for the next line, so nothing of it is kept:
        // whatever is needed is copied out before this returns.
        try
        {
            bubbles.LineArrived();
            Handle(message);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not process a chat message.");
        }
    }

    private void Handle(IHandleableChatMessage message)
    {
        if (!plugin.Store.HasCharacter) return;

        var config = plugin.Config;
        var kind = message.LogKind;
        switch (kind)
        {
            case XivChatType.TellIncoming or XivChatType.TellOutgoing:
                if (config.CaptureTells) HandleTell(message);
                break;

            case >= XivChatType.Ls1 and <= XivChatType.Ls8:
                if (config.CaptureLinkshells) HandleLinkshell(message, ChannelGroup.Linkshell, kind - XivChatType.Ls1 + 1);
                break;

            case XivChatType.CrossLinkShell1:
                if (config.CaptureCrossWorld) HandleLinkshell(message, ChannelGroup.CrossWorld, 1);
                break;

            // The first cross-world slot sits on its own in the enum; the
            // other seven are a run further up.
            case >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8:
                if (config.CaptureCrossWorld) HandleLinkshell(message, ChannelGroup.CrossWorld, kind - XivChatType.CrossLinkShell2 + 2);
                break;

            // A character is only ever in one free company, so it is a
            // channel with a single slot.
            case XivChatType.FreeCompany:
                if (config.CaptureFreeCompany) HandleLinkshell(message, ChannelGroup.FreeCompany, 1);
                break;

            case XivChatType.ErrorMessage or XivChatType.SystemError:
                // Only of interest while a send is waiting for its echo. The
                // check comes first because reading the text means parsing it,
                // which there is no call to do for every error the game prints.
                if (plugin.Outgoing.PendingCount == 0) break;
                var reason = message.Message.TextValue.Trim();
                if (reason.Length > 0) plugin.Outgoing.OnGameError(reason);
                break;
        }
    }

    private void HandleTell(IHandleableChatMessage message)
    {
        var outgoing = message.LogKind == XivChatType.TellOutgoing;

        // For an outgoing tell the "sender" field holds who it was sent to.
        if (!TryReadPlayer(message.Sender, out var name, out var worldId)
            && !TryParsePlainSender(message.Sender.TextValue, out name, out worldId))
            return;

        var conversation = plugin.Store.OpenTell(name, worldId, plugin.Worlds.Name(worldId), authoritative: true);
        var built = outgoing
            ? Build(message.Message, plugin.LocalName, plugin.LocalWorldId, MessageFlags.Outgoing)
            : Build(message.Message, name, worldId, MessageFlags.None);
        if (built == null) return;

        plugin.Store.Add(conversation, built);
        if (outgoing) plugin.Outgoing.OnEcho(conversation);
        else plugin.OnIncoming(conversation, built);

        if (plugin.Config.HideTellsFromGameChat) Hide(message);
    }

    private void HandleLinkshell(IHandleableChatMessage message, ChannelGroup group, int slot)
    {
        var name = plugin.Linkshells.Name(group, slot);
        if (name.Length == 0)
        {
            // A message for a slot with no name: the list is stale, or has not
            // loaded yet. Look again before settling for a placeholder.
            plugin.RefreshLinkshells();
            name = plugin.Linkshells.Name(group, slot);
        }

        var conversation = plugin.Store.GetLinkshell(group, name, slot);

        // The game links everyone's name in a linkshell except your own.
        var linked = TryReadPlayer(message.Sender, out var senderName, out var senderWorld);
        var self = message.SourceKind == XivChatRelationKind.LocalPlayer
                   || !linked
                   || (senderWorld == plugin.LocalWorldId && string.Equals(senderName, plugin.LocalName, StringComparison.Ordinal));

        var built = self
            ? Build(message.Message, plugin.LocalName, plugin.LocalWorldId, MessageFlags.Outgoing)
            : Build(message.Message, senderName, senderWorld, MessageFlags.None);
        if (built == null) return;

        plugin.Store.Add(conversation, built);
        if (self) plugin.Outgoing.OnEcho(conversation);
        else plugin.OnIncoming(conversation, built);

        var hide = group switch
        {
            ChannelGroup.Linkshell => plugin.Config.HideLinkshellsFromGameChat,
            ChannelGroup.FreeCompany => plugin.Config.HideFreeCompanyFromGameChat,
            _ => plugin.Config.HideCrossWorldFromGameChat,
        };
        if (hide) Hide(message);
    }

    /// <summary>Keeps a line out of the game's chat log. Its chat bubble, if it gets one, still says it.</summary>
    private void Hide(IHandleableChatMessage message)
    {
        message.PreventOriginal();
        bubbles.Hiding(message.LogKind, message.Message);
    }

    private static bool TryReadPlayer(SeString sender, out string name, out ushort worldId)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is not PlayerPayload player || string.IsNullOrEmpty(player.PlayerName)) continue;

            name = player.PlayerName;
            var world = player.World.RowId;
            worldId = world <= ushort.MaxValue ? (ushort)world : (ushort)0;
            return true;
        }

        name = string.Empty;
        worldId = 0;
        return false;
    }

    /// <summary>
    /// For a sender with no player link in it. The text is then the name,
    /// optionally followed by the cross-world icon and a world name.
    /// </summary>
    private bool TryParsePlainSender(string text, out string name, out ushort worldId)
    {
        var cut = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is >= PrivateUseFirst and <= PrivateUseLast)
            {
                cut = i;
                break;
            }
        }

        name = (cut >= 0 ? text[..cut] : text).Trim();
        worldId = 0;
        if (cut >= 0)
        {
            var world = new StringBuilder();
            foreach (var ch in text.AsSpan(cut))
            {
                if (ch is < PrivateUseFirst or > PrivateUseLast) world.Append(ch);
            }
            worldId = plugin.Worlds.Id(world.ToString());
        }

        // No world shown means they are from the world the player is on now.
        if (worldId == 0) worldId = plugin.CurrentWorldId;
        return PlayerName.IsPlausible(name) && worldId != 0;
    }

    private static ChatMessage? Build(SeString body, string sender, ushort senderWorld, MessageFlags flags)
    {
        var text = new StringBuilder();
        var formatted = false;

        foreach (var payload in body.Payloads)
        {
            switch (payload)
            {
                case TextPayload plain:
                    text.Append(plain.Text);
                    break;

                case AutoTranslatePayload phrase:
                    // The phrase arrives wrapped in the game's two bracket
                    // glyphs, which only the game's own font can draw.
                    text.Append('[').Append(Unbracket(phrase.Text)).Append(']');
                    formatted = true;
                    break;

                case ITextProvider provider:
                    text.Append(provider.Text);
                    formatted = true;
                    break;

                default:
                    formatted = true;
                    break;
            }
        }

        var clean = Tidy(text);
        if (clean.Length == 0) return null;

        return new ChatMessage
        {
            Flags = flags,
            Sender = sender,
            SenderWorld = senderWorld,
            Text = clean,
            Rich = formatted ? Encode(body) : null,
        };
    }

    /// <summary>
    /// The message as bytes the SeString renderer can draw, links and colours
    /// included. Null if that fails; the plain text is always there to fall
    /// back on.
    /// </summary>
    private static byte[]? Encode(SeString body)
    {
        try
        {
            var payloads = new List<Payload>(body.Payloads.Count);
            foreach (var payload in body.Payloads)
                payloads.Add(payload is AutoTranslatePayload phrase ? new TextPayload(phrase.Text) : payload);

            var bytes = new SeString(payloads).Encode();
            return bytes.Length is > 0 and <= MaxRichBytes ? bytes : null;
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not keep a message's formatting.");
            return null;
        }
    }

    private static string Unbracket(string? phrase) =>
        (phrase ?? string.Empty).Trim((char)SeIconChar.AutoTranslateOpen, (char)SeIconChar.AutoTranslateClose, ' ');

    private static string Tidy(StringBuilder text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is '\r' or '\n' or '\t') builder.Append(' ');
            else if (!char.IsControl(ch)) builder.Append(ch);
        }
        return builder.ToString().Trim();
    }
}
