using Dalamud.Game.Gui.ContextMenu;
using Parley.Core;

namespace Parley.Integration;

/// <summary>Adds "Message in Parley" to the game's right-click menu on a player.</summary>
internal sealed class ContextMenuIntegration : IDisposable
{
    private readonly Plugin plugin;

    public ContextMenuIntegration(Plugin plugin)
    {
        this.plugin = plugin;
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => Services.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            if (!plugin.Config.ContextMenuEntry || !plugin.Store.HasCharacter) return;
            if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault target) return;

            var name = target.TargetName;
            var worldId = target.TargetHomeWorld.RowId;

            // The same menu opens for retainers, NPCs, housing items and
            // everything else. A home world players can be from is what marks
            // the target as a player.
            if (string.IsNullOrEmpty(name) || !PlayerName.IsPlausible(name) || !plugin.Worlds.IsPlayable(worldId)) return;
            if (worldId == plugin.LocalWorldId && string.Equals(name, plugin.LocalName, StringComparison.Ordinal)) return;

            args.AddMenuItem(new MenuItem
            {
                Name = "Message in Parley",
                PrefixChar = 'P',
                OnClicked = _ => plugin.OpenTell(name, (ushort)worldId),
            });
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not add to the context menu.");
        }
    }
}
