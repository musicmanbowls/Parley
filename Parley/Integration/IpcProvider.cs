using System.Text.Json;
using Dalamud.Plugin.Ipc;
using Parley.Ipc;

namespace Parley.Integration;

/// <summary>
/// Publishes Parley's state and a few actions over Dalamud IPC. The Umbra
/// companion is the consumer this was written for, but nothing here is
/// specific to it; see <see cref="ParleyIpc"/> for the contract.
/// </summary>
internal sealed class IpcProvider : IDisposable
{
    /// <summary>A status poll within this long means something is showing unread counts on Parley's behalf.</summary>
    private const long PollFreshMs = 5000;

    private readonly Plugin plugin;
    private readonly List<ICallGateProvider> gates = [];

    private string cachedStatus = "{}";
    private int cachedRevision = -1;
    private bool cachedOpen;
    private long lastPolled = -PollFreshMs;

    public IpcProvider(Plugin plugin)
    {
        this.plugin = plugin;

        try
        {
            var pi = Services.PluginInterface;
            Register(pi.GetIpcProvider<int>(ParleyIpc.ApiVersion), gate => gate.RegisterFunc(() => ParleyIpc.Version));
            Register(pi.GetIpcProvider<string>(ParleyIpc.GetStatus), gate => gate.RegisterFunc(GetStatus));
            Register(pi.GetIpcProvider<bool>(ParleyIpc.ToggleWindow), gate => gate.RegisterFunc(() => Guard(plugin.ToggleWindow, false)));
            Register(pi.GetIpcProvider<bool>(ParleyIpc.OpenWindow), gate => gate.RegisterFunc(() => Guard(plugin.OpenWindow, false)));
            Register(pi.GetIpcProvider<int>(ParleyIpc.MarkAllRead), gate => gate.RegisterFunc(() => Guard(plugin.Store.MarkAllRead, 0)));
            Register(pi.GetIpcProvider<long>(ParleyIpc.GetThemeToken), gate => gate.RegisterFunc(() => plugin.Theme.UmbraToken));
            Register(pi.GetIpcProvider<string, bool>(ParleyIpc.SetTheme), gate => gate.RegisterFunc(SetTheme));
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not publish Parley's IPC gates.");
        }
    }

    /// <summary>Whether another plugin has asked for status recently, which is how a toolbar widget shows itself to be present.</summary>
    public bool RecentlyPolled => Environment.TickCount64 - lastPolled < PollFreshMs;

    public void Dispose()
    {
        // Unregistered rather than left dangling: a gate still pointing at a
        // disposed plugin is an exception in whoever calls it next.
        foreach (var gate in gates)
        {
            try { gate.UnregisterFunc(); }
            catch (Exception) { /* already gone */ }
        }
        gates.Clear();
    }

    private void Register<T>(T gate, Action<T> register) where T : ICallGateProvider
    {
        register(gate);
        gates.Add(gate);
    }

    /// <summary>
    /// Never throws: this runs inside another plugin's call, and an exception
    /// here would surface there as a fault in their code.
    /// </summary>
    private string GetStatus()
    {
        lastPolled = Environment.TickCount64;
        try
        {
            var open = plugin.MainWindow.IsOpen;
            var revision = plugin.Store.Revision;
            if (revision != cachedRevision || open != cachedOpen)
            {
                // Rebuilt only when something changed. A poller that gets the
                // same string instance back knows there is nothing to parse.
                cachedStatus = JsonSerializer.Serialize(plugin.Store.BuildStatus(open), ParleyIpcJson.Default.StatusSnapshot);
                cachedRevision = revision;
                cachedOpen = open;
            }
            return cachedStatus;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not build status for an IPC caller.");
            return "{}";
        }
    }

    private bool SetTheme(string json)
    {
        try
        {
            var theme = JsonSerializer.Deserialize(json, ParleyIpcJson.Default.UmbraTheme);
            if (theme?.Colours == null || theme.Colours.Count == 0) return false;
            plugin.Theme.SetUmbra(theme.Colours, theme.Token);
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Ignored an Umbra theme that could not be read.");
            return false;
        }
    }

    private static T Guard<T>(Func<T> action, T fallback)
    {
        try { return action(); }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "An IPC call into Parley failed.");
            return fallback;
        }
    }
}
