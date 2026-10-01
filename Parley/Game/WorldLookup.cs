using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Parley.Game;

/// <summary>World names and ids, read once from the game's World sheet.</summary>
internal sealed class WorldLookup
{
    private readonly Dictionary<uint, string> names = [];
    private readonly Dictionary<string, ushort> ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> playable = [];

    public WorldLookup(IDataManager data)
    {
        foreach (var world in data.GetExcelSheet<World>())
        {
            var name = world.Name.ExtractText();
            if (name.Length == 0 || world.RowId > ushort.MaxValue) continue;

            names[world.RowId] = name;
            // The sheet also holds test and internal worlds. Only ones players
            // can be on are offered when a name has to be turned back into an
            // id, and the first of any duplicate name wins.
            if (world.IsPublic && ids.TryAdd(name, (ushort)world.RowId)) playable.Add(name);
        }

        playable.Sort(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Worlds a player can be from, alphabetically.</summary>
    public IReadOnlyList<string> Playable => playable;

    public string Name(uint id) => names.GetValueOrDefault(id, string.Empty);

    /// <summary>0 when the name is not a world players can be on.</summary>
    public ushort Id(string name) => ids.GetValueOrDefault(name.Trim());

    public bool IsPlayable(uint id) => names.TryGetValue(id, out var name) && ids.TryGetValue(name, out var known) && known == id;
}
