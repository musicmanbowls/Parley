using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Parley.Core;
using Parley.Game;

namespace Parley.Ui;

internal sealed partial class MainWindow
{
    private const string NewTellPopup = "##parleynewtell";
    private const string ConfirmPopup = "Are you sure?##parleyconfirm";
    private const int MaxSuggestions = 10;

    private enum ConfirmKind
    {
        ClearHistory,
        Forget,
    }

    private bool openNewTell;
    private bool focusNewTell;
    private string newTellInput = string.Empty;
    private string newTellError = string.Empty;
    private List<Friend> friends = [];

    private bool openConfirm;
    private ConfirmKind confirmKind;
    private Conversation? confirmTarget;

    /// <summary>A person who could be written to: someone already spoken with, or a friend.</summary>
    private readonly record struct Suggestion(string Name, ushort WorldId, string World, string Note);

    // ------------------------------------------------------------------
    // New tell
    // ------------------------------------------------------------------

    private void DrawNewTellPopup()
    {
        if (openNewTell)
        {
            openNewTell = false;
            focusNewTell = true;
            newTellInput = string.Empty;
            newTellError = string.Empty;
            friends = FriendList.Read();
            ImGui.OpenPopup(NewTellPopup);
        }

        if (!ImGui.BeginPopup(NewTellPopup)) return;

        try
        {
            ImGui.TextUnformatted("Send a tell to");
            ImGui.SetNextItemWidth(320f * scale);
            if (focusNewTell)
            {
                focusNewTell = false;
                ImGui.SetKeyboardFocusHere();
            }

            if (ImGui.InputTextWithHint("##who", "First Last@World", ref newTellInput, 64)) newTellError = string.Empty;
            var entered = ImGui.IsItemDeactivated()
                          && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));

            var suggestions = BuildSuggestions(newTellInput);
            if (entered)
            {
                // Enter takes focus off the box. If what was typed could not be
                // used, put the cursor back so it can be corrected.
                if (TryOpenTyped(suggestions)) ImGui.CloseCurrentPopup();
                else focusNewTell = true;
            }

            if (newTellError.Length > 0) ImGui.TextColored(palette.Error, newTellError);

            if (Services.Targets.Target is IPlayerCharacter target)
            {
                var name = target.Name.TextValue;
                var worldId = target.HomeWorld.RowId;
                var world = plugin.Worlds.Name(worldId);
                if (world.Length > 0 && plugin.Worlds.IsPlayable(worldId) && !IsLocalPlayer(name, worldId))
                {
                    ImGui.Separator();
                    if (ImGui.Selectable($"Your target: {name}@{world}"))
                    {
                        plugin.OpenTell(name, (ushort)worldId);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }

            if (suggestions.Count > 0)
            {
                ImGui.Separator();
                for (var i = 0; i < suggestions.Count; i++)
                {
                    var suggestion = suggestions[i];
                    if (ImGui.Selectable($"{suggestion.Name}@{suggestion.World}##suggest{i}"))
                    {
                        plugin.OpenTell(suggestion.Name, suggestion.WorldId);
                        ImGui.CloseCurrentPopup();
                    }

                    if (suggestion.Note.Length > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled(suggestion.Note);
                    }
                }
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }

    /// <summary>People matching what has been typed so far: earlier conversations first, then friends, online before offline.</summary>
    private List<Suggestion> BuildSuggestions(string input)
    {
        var filter = input.Trim();
        var seen = new HashSet<(string, ushort)>();
        var suggestions = new List<Suggestion>(MaxSuggestions);

        bool Matches(string name, string world) =>
            filter.Length == 0
            || name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || $"{name}@{world}".Contains(filter, StringComparison.OrdinalIgnoreCase);

        var recent = new List<Conversation>();
        foreach (var conversation in store.All)
        {
            if (conversation.IsTell && conversation.WorldName.Length > 0 && Matches(conversation.Title, conversation.WorldName))
                recent.Add(conversation);
        }
        recent.Sort((a, b) => b.LastActivity.CompareTo(a.LastActivity));

        foreach (var conversation in recent)
        {
            if (suggestions.Count >= MaxSuggestions) return suggestions;
            if (seen.Add((conversation.Title.ToLowerInvariant(), conversation.WorldId)))
                suggestions.Add(new Suggestion(conversation.Title, conversation.WorldId, conversation.WorldName, conversation.Closed ? "closed" : "recent"));
        }

        foreach (var friend in friends)
        {
            if (suggestions.Count >= MaxSuggestions) return suggestions;
            var world = plugin.Worlds.Name(friend.WorldId);
            if (world.Length == 0 || !Matches(friend.Name, world)) continue;
            if (seen.Add((friend.Name.ToLowerInvariant(), friend.WorldId)))
                suggestions.Add(new Suggestion(friend.Name, friend.WorldId, world, friend.Online ? "friend, online" : "friend"));
        }

        return suggestions;
    }

    private bool TryOpenTyped(List<Suggestion> suggestions)
    {
        var hasWorld = PlayerName.TrySplit(newTellInput, out var name, out var world);
        if (!PlayerName.IsPlausible(name))
        {
            newTellError = "Enter a character name, like First Last@World.";
            return false;
        }

        ushort worldId;
        if (hasWorld)
        {
            worldId = plugin.Worlds.Id(world);
            if (worldId == 0)
            {
                newTellError = $"\"{world}\" is not a world.";
                return false;
            }
        }
        else
        {
            // No world typed. That is only unambiguous if exactly one known
            // person has that name; guessing a world would send the tell to a
            // stranger who happens to share it.
            Suggestion? only = null;
            var count = 0;
            foreach (var suggestion in suggestions)
            {
                if (!string.Equals(suggestion.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                only = suggestion;
                count++;
            }

            if (count != 1)
            {
                newTellError = "Add the world: First Last@World.";
                return false;
            }

            name = only!.Value.Name;
            worldId = only.Value.WorldId;
        }

        if (IsLocalPlayer(name, worldId))
        {
            newTellError = "That is you.";
            return false;
        }

        plugin.OpenTell(name, worldId);
        return true;
    }

    private bool IsLocalPlayer(string name, uint worldId) =>
        worldId == plugin.LocalWorldId && string.Equals(name, plugin.LocalName, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // Confirmation
    // ------------------------------------------------------------------

    private void AskToConfirm(ConfirmKind kind, Conversation conversation)
    {
        confirmKind = kind;
        confirmTarget = conversation;
        openConfirm = true;
    }

    private void DrawConfirmPopup()
    {
        if (openConfirm)
        {
            openConfirm = false;
            ImGui.OpenPopup(ConfirmPopup);
        }

        if (!ImGui.BeginPopupModal(ConfirmPopup, ImGuiWindowFlags.AlwaysAutoResize)) return;

        try
        {
            var target = confirmTarget;
            if (target == null)
            {
                ImGui.CloseCurrentPopup();
                return;
            }

            var forget = confirmKind == ConfirmKind.Forget;
            ImGui.TextUnformatted(forget
                ? $"Delete the conversation with {target.DisplayName}?"
                : $"Clear the history of {target.DisplayName}?");
            ImGui.TextDisabled(store.SavesHistory
                ? "The saved messages are deleted from disk. This cannot be undone."
                : "The messages are removed from this window. This cannot be undone.");
            ImGui.Dummy(new Vector2(1f, 4f * scale));

            if (ImGui.Button(forget ? "Delete" : "Clear", new Vector2(110f * scale, 0f)))
            {
                if (forget) store.Forget(target);
                else store.ClearHistory(target);
                confirmTarget = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110f * scale, 0f)))
            {
                confirmTarget = null;
                ImGui.CloseCurrentPopup();
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }
}
