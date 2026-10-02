using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Parley.Game;

/// <summary>
/// The game's own chat log windows, kept out of sight while Parley stands in
/// for them. As Chat 2 does, each window's visibility is switched off every
/// frame, because the game keeps switching it back on. When Parley stops, only
/// the windows it hid are shown again. Framework thread only.
/// </summary>
internal static unsafe class GameChatWindow
{
    private static readonly string[] Names = ["ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3"];
    private static readonly HashSet<string> HiddenByParley = [];

    public static void Hide()
    {
        foreach (var name in Names)
        {
            var addon = Find(name);
            if (addon == null || !addon->IsVisible) continue;
            addon->IsVisible = false;
            HiddenByParley.Add(name);
        }
    }

    public static void Restore()
    {
        if (HiddenByParley.Count == 0) return;
        foreach (var name in HiddenByParley)
        {
            var addon = Find(name);
            if (addon != null) addon->IsVisible = true;
        }
        HiddenByParley.Clear();
    }

    /// <summary>
    /// Whether the game has put the keyboard in its own chat box, as "Send
    /// Tell" on a player does. With that box hidden, whatever was typed would
    /// go unseen.
    /// </summary>
    public static bool InputActive()
    {
        var module = RaptureAtkModule.Instance();
        if (module == null || !module->IsTextInputActive()) return false;

        var chat = Find("ChatLog");
        return chat != null && module->IsAddonFocused(chat->Id);
    }

    /// <summary>Takes the keyboard back from the game's chat box.</summary>
    public static void ReleaseInput()
    {
        var module = RaptureAtkModule.Instance();
        if (module != null) module->ClearFocus();
    }

    /// <summary>
    /// Whatever has been typed into the game's chat box, as plain text, such
    /// as the "/" its slash key starts a command with. The box is emptied, so
    /// the same text is not carried over twice.
    /// </summary>
    public static string TakeInput()
    {
        var chat = (AddonChatLog*)Find("ChatLog");
        if (chat == null || chat->TextInput == null) return string.Empty;

        var raw = chat->TextInput->RawString.AsSpan();
        if (raw.IsEmpty) return string.Empty;

        var text = Dalamud.Game.Text.SeStringHandling.SeString.Parse(raw.ToArray()).TextValue;
        chat->TextInput->SetText(string.Empty);
        return text;
    }

    /// <summary>
    /// Whether the game has put its own chat log out of sight, as it does for
    /// most cutscenes, by the flag it keeps for that. Read the way Chat 2
    /// reads it, so Parley goes when the game's chat would and not before:
    /// the game marks a cutscene as started a moment before it hides anything.
    /// </summary>
    public static bool HiddenByGame()
    {
        var manager = RaptureAtkUnitManager.Instance();
        return manager == null || manager->UiFlags.HasFlag(UiFlags.Chat);
    }

    /// <summary>The main command behind "Log Window Settings" in the game's System menu.</summary>
    private const uint LogWindowSettings = 55;

    /// <summary>Opens the game's own Log Window Settings, as its System menu does: filters, colours and tabs.</summary>
    public static bool OpenLogSettings()
    {
        var ui = UIModule.Instance();
        if (ui == null || !ui->IsMainCommandUnlocked(LogWindowSettings)) return false;

        ui->ExecuteMainCommand(LogWindowSettings);
        return true;
    }

    /// <summary>
    /// Presses the "+" the game shows after its chat tabs while there is room
    /// for another, so the game adds the tab its own way. The press goes
    /// through the event the game gave that button, the same as a click, so
    /// nothing about what the button does is assumed here.
    /// </summary>
    public static bool AddTab()
    {
        var chat = (AddonChatLog*)Find("ChatLog");
        if (chat == null || chat->AddTabComponentNode == null) return false;
        return Press(&chat->AtkUnitBase, (AtkResNode*)chat->AddTabComponentNode);
    }

    private static bool Press(AtkUnitBase* addon, AtkResNode* button)
    {
        for (var listened = button->AtkEventManager.Event; listened != null; listened = listened->NextEvent)
        {
            if (listened->State.EventType != AtkEventType.ButtonClick || listened->Listener != (AtkEventListener*)addon) continue;

            var data = default(AtkEventData);
            addon->ReceiveEvent(AtkEventType.ButtonClick, (int)listened->Param, listened, &data);
            return true;
        }
        return false;
    }

    private static AtkUnitBase* Find(string name)
    {
        var module = RaptureAtkModule.Instance();
        if (module == null) return null;

        var addon = module->RaptureAtkUnitManager.GetAddonByName(name);
        return addon != null && addon->IsReady ? addon : null;
    }
}
