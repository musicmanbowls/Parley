using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;

namespace Parley.Ui;

/// <summary>
/// The auto-translate picker beside each reply box: the game's dictionary of
/// phrases, which every reader sees in their own language. Pick a group and a
/// phrase, or search for one. Or type the start of one in the box and press
/// Tab, as in the game's chat box, and the picker opens on what was typed.
/// </summary>
internal sealed partial class MainWindow
{
    private const string AutoTranslateMenu = "##autotranslate";
    private const int AutoTranslateResults = 200;

    private string atQuery = string.Empty;
    private uint atGroup;
    private bool atFocusSearch;

    // Tab in a reply box: which box, and the word before the caret that the
    // picked phrase replaces, in UTF-8 bytes as ImGui counts.
    private bool atRequested;
    private string? atRequestOwner;
    private int atReplaceStart = -1;
    private int atReplaceLength;

    /// <param name="conversation">Whose reply box the phrase goes in, or null for General's.</param>
    private void DrawAutoTranslateButton(Conversation? conversation, bool enabled, float size)
    {
        var key = conversation?.Key ?? GeneralKey;
        if (Painter.IconButton("##atbutton", FontAwesomeIcon.Language, enabled ? "Auto-translate (or Tab in the box)" : string.Empty, palette, size, enabled: enabled))
        {
            atQuery = string.Empty;
            atReplaceStart = -1;
            atFocusSearch = true;
            ImGui.OpenPopup(AutoTranslateMenu);
        }

        if (atRequested && string.Equals(atRequestOwner, key, StringComparison.Ordinal))
        {
            atRequested = false;
            atFocusSearch = true;
            ImGui.OpenPopup(AutoTranslateMenu);
        }

        ImGui.SetNextWindowSize(new Vector2(540f, 380f) * scale, ImGuiCond.Appearing);
        if (!BeginMenu(AutoTranslateMenu)) return;
        try
        {
            DrawAutoTranslatePicker(conversation);
        }
        finally
        {
            EndMenu();
        }
    }

    private void DrawAutoTranslatePicker(Conversation? conversation)
    {
        if (atFocusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            atFocusSearch = false;
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##atsearch", "Search the auto-translate dictionary", ref atQuery, 100);

        var height = MathF.Max(80f * scale, ImGui.GetContentRegionAvail().Y);
        if (atQuery.Trim().Length > 0)
        {
            DrawAutoTranslateResults(conversation, height);
            return;
        }

        var groups = plugin.AutoTranslateGroups();
        if (groups.Count == 0)
        {
            ImGui.TextDisabled("The game's auto-translate dictionary could not be read.");
            return;
        }
        if (atGroup == 0) atGroup = groups[0].Id;

        ImGui.BeginChild("##atgroups", new Vector2(180f * scale, height), true);
        try
        {
            foreach (var group in groups)
            {
                if (ImGui.Selectable($"{group.Title}##group{group.Id}", group.Id == atGroup)) atGroup = group.Id;
            }
        }
        finally
        {
            ImGui.EndChild();
        }

        ImGui.SameLine();
        ImGui.BeginChild("##atphrases", new Vector2(0f, height), true);
        try
        {
            var phrases = plugin.AutoTranslatePhrases(atGroup);
            if (phrases.Count == 0) ImGui.TextDisabled("Nothing in this group.");
            DrawPhraseList(conversation, phrases, groupTitles: null);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawAutoTranslateResults(Conversation? conversation, float height)
    {
        ImGui.BeginChild("##atresults", new Vector2(0f, height), true);
        try
        {
            var found = AutoTranslateSearch.Find(plugin.AutoTranslateAll(), atQuery, AutoTranslateResults);
            if (found.Count == 0) ImGui.TextDisabled("No phrase has that in it.");

            var titles = new Dictionary<uint, string>();
            foreach (var group in plugin.AutoTranslateGroups()) titles[group.Id] = group.Title;
            DrawPhraseList(conversation, found, titles);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>The phrases, only those on screen drawn, with their group's name beside each when searching.</summary>
    private void DrawPhraseList(Conversation? conversation, IReadOnlyList<AutoTranslatePhrase> phrases, Dictionary<uint, string>? groupTitles)
    {
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(phrases.Count, ImGui.GetTextLineHeightWithSpacing());
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var phrase = phrases[i];
                    if (ImGui.Selectable($"{phrase.Text}##phrase{phrase.Group}.{phrase.Key}"))
                    {
                        PickPhrase(conversation, phrase);
                        return;
                    }

                    if (groupTitles != null && groupTitles.TryGetValue(phrase.Group, out var title))
                    {
                        ImGui.SameLine(MathF.Max(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(title).X, ImGui.CalcTextSize(phrase.Text).X + (16f * scale)));
                        ImGui.TextDisabled(title);
                    }
                }
            }
            clipper.End();
        }
        finally
        {
            clipper.Destroy();
        }
    }

    /// <summary>Puts the phrase in the reply box: in place of the word Tab was pressed after, or where the caret was.</summary>
    private void PickPhrase(Conversation? conversation, AutoTranslatePhrase phrase)
    {
        var token = plugin.AutoTranslateToken(phrase) + " ";
        if (atReplaceStart >= 0) ReplaceInDraft(conversation, atReplaceStart, atReplaceLength, token);
        else InsertIntoDraft(conversation, token);
        atReplaceStart = -1;
        ImGui.CloseCurrentPopup();
    }

    /// <summary>
    /// Tab in a reply box, from inside ImGui's text box: the word before the
    /// caret becomes the picker's search, and the phrase picked will take its place.
    /// </summary>
    private void StartAutoTranslate(ref ImGuiInputTextCallbackData data)
    {
        var text = data.BufTextSpan;
        var end = Math.Clamp(data.CursorPos, 0, text.Length);
        var start = end;
        while (start > 0 && IsWordByte(text[start - 1])) start--;

        atQuery = Encoding.UTF8.GetString(text[start..end]);
        atReplaceStart = start;
        atReplaceLength = end - start;
        atRequestOwner = caretDrawing;
        atRequested = true;
    }

    /// <summary>A byte of a word: a letter, digit, apostrophe or hyphen, or any part of a character beyond ASCII.</summary>
    private static bool IsWordByte(byte value) => value >= 0x80 || char.IsLetterOrDigit((char)value) || value is (byte)'\'' or (byte)'-';

    /// <summary>Replaces part of a reply box's text, counted in UTF-8 bytes, and puts the caret after what went in.</summary>
    private void ReplaceInDraft(Conversation? conversation, int startBytes, int lengthBytes, string text)
    {
        var key = conversation?.Key ?? GeneralKey;
        var draft = conversation?.Draft ?? generalDraft;
        var start = CharIndex(draft, startBytes);
        var end = CharIndex(draft, startBytes + lengthBytes);
        var updated = draft[..start] + text + draft[end..];
        if (Encoding.UTF8.GetByteCount(updated) > (conversation == null ? GeneralMaxBytes : MaxDraftLength)) return;

        if (conversation == null) generalDraft = updated;
        else store.SetDraft(conversation, updated);
        caretTarget = Encoding.UTF8.GetByteCount(updated.AsSpan(0, start + text.Length));
        caretBytes = caretTarget;
        focusComposer = key;
    }
}
