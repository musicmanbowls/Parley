using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using Parley.Core;
using OnlineStatus = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData.OnlineStatus;

namespace Parley.Game;

internal readonly record struct Friend(string Name, ushort WorldId, bool Online);

/// <summary>
/// The friend list as the client holds it: used to suggest people when
/// starting a tell, and to say which tells are with friends and whether they
/// are around. Framework thread only.
///
/// The client keeps the list from the last time it was fetched, which the game
/// does when its friend list window opens, and is not told when a friend's
/// status changes. With the setting to keep statuses up to date on, Parley
/// fetches it every few seconds while it is showing tells, except while the
/// game's own friend list window is open (see <see cref="GameWindowOpen"/>).
/// </summary>
internal static unsafe class FriendList
{
    /// <summary>The game caps a friend list at 200.</summary>
    private const uint MaxEntries = 200;

    /// <summary>The client's copy is read this often. Reading it is cheap and asks nothing of the server.</summary>
    private const long ReadMs = 1000;

    /// <summary>Asking more often than this is never done, whatever the setting says.</summary>
    public const int MinRequestSeconds = 5;

    /// <summary>Opening a friend's tell asks straight away, but not more often than this.</summary>
    private const long OnDemandMs = MinRequestSeconds * 1000;

    /// <summary>Until a list has arrived at all, asking again comes round this soon, a few times.</summary>
    private const long EmptyRetryMs = 20 * 1000;
    private const int EmptyRetries = 3;

    /// <summary>
    /// Statuses as last read. A fetch empties the client's copy for a moment
    /// before it fills again; the book keeps everyone's last status through
    /// that, so nothing flickers off and on.
    /// </summary>
    private static readonly FriendStatusBook Book = new();
    private static readonly List<(string Name, ushort World, FriendStatus Status)> Reading = [];
    private static readonly Dictionary<ushort, string> ZoneNames = [];

    /// <summary>Every zone a duty takes place in, with that duty's name. Read from the game's data once.</summary>
    private static Dictionary<ushort, string>? dutyZones;

    // Times are Environment.TickCount64 values to act at or after. Zero means
    // straight away; the tick count is never zero or less.
    private static long nextRead;
    private static long nextRequest;
    private static long lastRequest;
    private static int emptyRetries;

    public static List<Friend> Read()
    {
        var friends = new List<Friend>();
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null) return friends;

        var count = Math.Min(proxy->EntryCount, MaxEntries);
        for (var i = 0u; i < count; i++)
        {
            var entry = proxy->GetEntry(i);
            if (entry == null || entry->HomeWorld == 0) continue;

            var name = entry->NameString;
            if (string.IsNullOrEmpty(name)) continue;

            var online = (entry->State & OnlineStatus.Online) != 0;
            friends.Add(new Friend(name, entry->HomeWorld, online));
        }

        friends.Sort((a, b) =>
        {
            if (a.Online != b.Online) return a.Online ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return friends;
    }

    /// <summary>What the friend list says about this person, or <see cref="FriendStatus.None"/> if they are not on it.</summary>
    public static FriendStatus Status(string name, ushort homeWorld, WorldLookup worlds)
    {
        var now = Environment.TickCount64;
        if (now >= nextRead)
        {
            nextRead = now + ReadMs;
            Refresh(worlds, now);
        }

        return Book.Get(name, homeWorld);
    }

    /// <summary>
    /// Asks the server for the friend list, as opening the game's friend list
    /// window does: every <paramref name="intervalSeconds"/>, and a little
    /// sooner while none has arrived yet. Not while that window is open.
    /// </summary>
    public static void RequestIfStale(int intervalSeconds)
    {
        var now = Environment.TickCount64;
        if (now < nextRequest) return;

        var intervalMs = Math.Max(MinRequestSeconds, intervalSeconds) * 1000L;
        try
        {
            // Looked at again shortly, so the first fetch after it closes is not long in coming.
            if (GameWindowOpen())
            {
                nextRequest = now + ReadMs;
                return;
            }

            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null)
            {
                nextRequest = now + EmptyRetryMs;
                return;
            }

            var empty = proxy->EntryCount == 0;
            nextRequest = now + (empty && emptyRetries++ < EmptyRetries ? Math.Min(EmptyRetryMs, intervalMs) : intervalMs);
            lastRequest = now;
            proxy->RequestData();
        }
        catch (Exception ex)
        {
            nextRequest = now + intervalMs;
            Services.Log.Verbose(ex, "Could not ask for the friend list.");
        }
    }

    /// <summary>
    /// Asks now rather than on the schedule, for when a friend's tell has just
    /// been opened and their status should be current. Never more often than
    /// every few seconds.
    /// </summary>
    public static void RequestSoon(int intervalSeconds)
    {
        if (lastRequest == 0 || Environment.TickCount64 - lastRequest >= OnDemandMs) nextRequest = 0;
        RequestIfStale(intervalSeconds);
    }

    /// <summary>
    /// Whether the game's own friend list window is on screen. A fetch
    /// empties that window's list and fills it again, so while it is open
    /// Parley asks for nothing and reads whatever the window shows, which
    /// spares it a refresh every few seconds.
    /// </summary>
    public static bool GameWindowOpen()
    {
        var module = RaptureAtkModule.Instance();
        if (module == null) return false;

        var addon = module->RaptureAtkUnitManager.GetAddonByName("FriendList");
        return addon != null && addon->IsVisible;
    }

    /// <summary>How many people the client has on the list right now, and how many of them are online. For /parley friends.</summary>
    public static (int Friends, int Online) Count()
    {
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null) return (0, 0);

        var count = (int)Math.Min(proxy->EntryCount, MaxEntries);
        var online = 0;
        for (var i = 0u; i < count; i++)
        {
            var entry = proxy->GetEntry(i);
            if (entry != null && (entry->State & OnlineStatus.Online) != 0) online++;
        }
        return (count, online);
    }

    public static void Forget()
    {
        Book.Clear();
        nextRead = 0;
        nextRequest = 0;
        lastRequest = 0;
        emptyRetries = 0;
    }

    /// <summary>
    /// Zones that are duty instances, from the duty finder's list of duties:
    /// dungeons, trials, raids, PvP, deep dungeons, field operations and the
    /// rest. Open-world zones and cities are in none of them.
    /// </summary>
    private static Dictionary<ushort, string> Duties()
    {
        if (dutyZones != null) return dutyZones;
        dutyZones = [];
        try
        {
            foreach (var duty in Services.Data.GetExcelSheet<ContentFinderCondition>())
            {
                var zone = duty.TerritoryType.RowId;
                if (zone is 0 or > ushort.MaxValue) continue;

                // Duty names are written to sit mid-sentence ("the Praetorium").
                var name = duty.Name.ExtractText();
                if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..];
                if (!dutyZones.TryGetValue((ushort)zone, out var known) || known.Length == 0) dutyZones[(ushort)zone] = name;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not read the list of duties; friends in duties will show as online.");
        }
        return dutyZones;
    }

    private static string ZoneName(ushort zone)
    {
        if (zone == 0) return string.Empty;
        if (ZoneNames.TryGetValue(zone, out var name)) return name;

        name = string.Empty;
        try
        {
            if (Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(zone, out var row) && row.PlaceName.IsValid)
                name = row.PlaceName.Value.Name.ExtractText();
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, $"Could not look up zone {zone}.");
        }

        ZoneNames[zone] = name;
        return name;
    }

    private static void Refresh(WorldLookup worlds, long now)
    {
        Reading.Clear();
        try
        {
            var proxy = InfoProxyFriendList.Instance();
            if (proxy == null) return;

            var count = Math.Min(proxy->EntryCount, MaxEntries);
            for (var i = 0u; i < count; i++)
            {
                var entry = proxy->GetEntry(i);
                if (entry == null || entry->HomeWorld == 0) continue;

                var name = entry->NameString;
                if (string.IsNullOrEmpty(name)) continue;

                var state = entry->State;
                var online = (state & OnlineStatus.Online) != 0;
                var current = online ? entry->CurrentWorld : (ushort)0;

                // The game's friend list works "in a duty" out from where
                // someone is rather than being sent it as a status, so the
                // status flag alone misses it. Their zone being one a duty
                // takes place in says the same thing.
                var zone = online ? entry->Location : (ushort)0;
                var dutyName = string.Empty;
                var inDutyZone = zone != 0 && Duties().TryGetValue(zone, out dutyName);
                const OnlineStatus dutyFlags = OnlineStatus.InDuty | OnlineStatus.SharingDuty | OnlineStatus.SimilarDuty | OnlineStatus.PvP;

                Reading.Add((name, entry->HomeWorld, new FriendStatus(
                    IsFriend: true,
                    Online: online,
                    Busy: online && (state & OnlineStatus.Busy) != 0,
                    Away: online && (state & OnlineStatus.AwayFromKeyboard) != 0,
                    InDuty: online && ((state & dutyFlags) != 0 || inDutyZone),
                    CurrentWorld: current,
                    CurrentWorldName: current != 0 ? worlds.Name(current) : string.Empty)
                {
                    Place = inDutyZone && dutyName!.Length > 0 ? dutyName : ZoneName(zone),
                }));
            }
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not read the friend list.");
        }
        finally
        {
            Book.Update(Reading, now);
        }
    }
}
