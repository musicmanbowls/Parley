using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// Hands a line to the game as though it had been typed into the chat box.
///
/// Dalamud has no API for this. The entry point is the game's own chat box
/// handler, reached through ClientStructs rather than a signature of our own,
/// so a game patch is absorbed by the ClientStructs update Dalamud ships.
/// Framework thread only.
/// </summary>
internal static unsafe class ChatSender
{
    // Letters, digits, punctuation, the game's special characters and payloads.
    // The same set other plugins pass when sanitising text bound for the chat box.
    private const AllowedEntities Allowed = (AllowedEntities)0x27F;

    /// <summary>
    /// Removes whatever the game's own input handling would not let through.
    /// Items linked into the text are left as they are; they become real links
    /// when the line is sent.
    /// </summary>
    public static string Sanitise(string text) => text.Length == 0 ? text : ItemLinks.AroundTokens(text, SanitisePlain);

    private static string SanitisePlain(string text)
    {
        if (text.Length == 0) return text;

        // Control characters first: the chat line format uses 0x02 and 0x03 to
        // delimit payloads, and nothing typed by hand should ever contain them.
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t') builder.Append(' ');
            else if (!char.IsControl(ch)) builder.Append(ch);
        }

        var utf8 = Utf8String.FromString(builder.ToString());
        try
        {
            utf8->SanitizeString(Allowed);
            return utf8->ToString();
        }
        finally
        {
            utf8->Dtor(true);
        }
    }

    public static bool TrySend(string line)
    {
        var bytes = ItemLinks.Encode(line);
        if (bytes.Length == 0 || bytes.Length > OutgoingQueue.MaxLineBytes) return false;

        var ui = UIModule.Instance();
        if (ui == null) return false;

        var entry = Utf8String.FromSequence(bytes);
        try
        {
            ui->ProcessChatBoxEntry(entry);
        }
        finally
        {
            entry->Dtor(true);
        }
        return true;
    }
}
