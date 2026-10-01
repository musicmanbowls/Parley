using System.Numerics;
using Dalamud.Bindings.ImGui;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;

namespace Parley.Ui;

internal sealed partial class MainWindow
{
    private const string NameColourPopup = "Name colour##parleynamecolour";

    private bool openNameColour;
    private string nameColourKey = string.Empty;
    private string nameColourLabel = string.Empty;
    private Vector3 nameColourPick;

    /// <summary>
    /// The colour someone's name is drawn in: the one picked for them if there
    /// is one, otherwise a colour of their own worked out from their name, or
    /// the channel's colour if that option is off.
    /// </summary>
    private Vector4 NameColour(Conversation conversation, string name, string world, bool outgoing)
    {
        if (config.NameColours.Count > 0
            && config.NameColours.TryGetValue(Configuration.NameKey(name, world), out var hex)
            && ColourMath.TryParseHex(hex, out var picked))
            return picked with { W = 1f };

        return AutomaticNameColour(conversation, name, outgoing);
    }

    private Vector4 AutomaticNameColour(Conversation conversation, string name, bool outgoing)
    {
        if (outgoing) return palette.Accent;
        return config.ColourNames ? ColourMath.ForName(name, palette.IsDark) : theme.ColourFor(conversation);
    }

    /// <summary>
    /// <see cref="NameColour"/> for the sender of a message, with the lookup
    /// of a picked colour kept on the message so it is not repeated every frame.
    /// </summary>
    private Vector4 SenderColour(Conversation conversation, ChatMessage message)
    {
        if (message.NameColourStamp != config.NameColoursVersion)
        {
            message.NameColourStamp = config.NameColoursVersion;
            message.HasNameColour = false;
            if (config.NameColours.Count > 0)
            {
                var key = message.IsOutgoing
                    ? Configuration.NameKey(plugin.LocalName, plugin.Worlds.Name(plugin.LocalWorldId))
                    : Configuration.NameKey(message.Sender, plugin.Worlds.Name(message.SenderWorld));
                if (config.NameColours.TryGetValue(key, out var hex) && ColourMath.TryParseHex(hex, out var picked))
                {
                    message.HasNameColour = true;
                    message.NameColour = picked with { W = 1f };
                }
            }
        }

        return message.HasNameColour ? message.NameColour : AutomaticNameColour(conversation, message.Sender, message.IsOutgoing);
    }

    /// <summary>Opens the colour picker for one person's name. The change shows as it is made.</summary>
    private void AskForNameColour(Conversation conversation, string name, string world, bool outgoing)
    {
        nameColourKey = Configuration.NameKey(name, world);
        nameColourLabel = outgoing ? "Your name" : nameColourKey;
        var current = NameColour(conversation, name, world, outgoing);
        nameColourPick = new Vector3(current.X, current.Y, current.Z);
        openNameColour = true;
    }

    private void DrawNameColourPopup()
    {
        if (openNameColour)
        {
            openNameColour = false;
            ImGui.OpenPopup(NameColourPopup);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, normalPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, normalSpacing);
        try
        {
            if (!ImGui.BeginPopup(NameColourPopup)) return;
            try
            {
                ImGui.TextUnformatted(nameColourLabel);
                ImGui.Dummy(new Vector2(1f, 2f * scale));

                ImGui.SetNextItemWidth(220f * scale);
                if (ImGui.ColorPicker3("##namepick", ref nameColourPick,
                        ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.PickerHueBar))
                {
                    config.SetNameColour(nameColourKey, ColourMath.ToHex(new Vector4(nameColourPick, 1f)));
                    plugin.SaveConfig();
                }

                var picked = config.NameColours.ContainsKey(nameColourKey);
                ImGui.BeginDisabled(!picked);
                if (ImGui.Button("Automatic"))
                {
                    config.SetNameColour(nameColourKey, null);
                    plugin.SaveConfig();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(config.ColourNames ? "Go back to the colour worked out from the name." : "Go back to the channel's colour.");

                ImGui.SameLine();
                if (ImGui.Button("Done")) ImGui.CloseCurrentPopup();
            }
            finally
            {
                ImGui.EndPopup();
            }
        }
        finally
        {
            ImGui.PopStyleVar(2);
        }
    }
}
