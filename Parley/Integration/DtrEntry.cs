using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Parley.Core;
using Parley.Core.Settings;

namespace Parley.Integration;

/// <summary>
/// An unread counter in the game's server info bar, for anyone not using the
/// Umbra widget. Left-click toggles the window; right-click marks everything
/// read, the same as the widget.
/// </summary>
internal sealed class DtrEntry : IDisposable
{
    private const string Title = "Parley";

    private readonly Plugin plugin;
    private IDtrBarEntry? entry;
    private int shownRevision = -1;
    private int shownMask = -1;

    public DtrEntry(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Dispose()
    {
        entry?.Remove();
        entry = null;
    }

    /// <summary>Called every framework tick. Does nothing unless something it shows has changed.</summary>
    public void Update()
    {
        var config = plugin.Config;
        var wanted = plugin.Store.HasCharacter && config.DtrMode switch
        {
            DtrMode.Always => true,
            // Auto steps aside for a toolbar widget, which is polling for the
            // same numbers and would otherwise show them twice.
            DtrMode.Auto => !plugin.Ipc.RecentlyPolled,
            _ => false,
        };

        if (!wanted)
        {
            if (entry is { Shown: true }) entry.Shown = false;
            return;
        }

        if (entry == null)
        {
            entry = Services.DtrBar.Get(Title);
            entry.OnClick = OnClick;
            shownRevision = -1;
        }

        var mask = (config.DtrCountTells ? 1 : 0) | (config.DtrCountLinkshells ? 2 : 0) | (config.DtrCountCrossWorld ? 4 : 0)
                   | (config.DtrCountFreeCompany ? 8 : 0);
        if (shownRevision != plugin.Store.Revision || shownMask != mask)
        {
            shownRevision = plugin.Store.Revision;
            shownMask = mask;

            var store = plugin.Store;
            var summary = UnreadSummary.Describe(
                config.DtrCountTells ? store.Unread(ChannelGroup.Tell) : 0,
                config.DtrCountLinkshells ? store.Unread(ChannelGroup.Linkshell) : 0,
                config.DtrCountCrossWorld ? store.Unread(ChannelGroup.CrossWorld) : 0,
                config.DtrCountFreeCompany ? store.Unread(ChannelGroup.FreeCompany) : 0);

            entry.Text = new SeString(new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(summary.Length > 0 ? summary : "Chat"));
            entry.Tooltip = new SeString(new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(
                (summary.Length > 0 ? $"Parley: {summary} unread" : "Parley: no new messages")
                + "\nLeft-click: open or close the chat window\nRight-click: mark everything as read"));
        }

        if (!entry.Shown) entry.Shown = true;
    }

    private void OnClick(DtrInteractionEvent interaction)
    {
        try
        {
            if (interaction.ClickType == MouseClickType.Right) plugin.Store.MarkAllRead();
            else plugin.ToggleWindow();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not handle a click on the server info bar entry.");
        }
    }
}
