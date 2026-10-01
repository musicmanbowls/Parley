using System.Text.Json;
using Parley.Ipc;

namespace Umbra.Parley;

/// <summary>
/// Keeps the latest status from Parley, and a reason to show when there is
/// none. IPC failures and reloads on either side end up here rather than in
/// the widget's drawing code.
/// </summary>
public sealed class ParleyConnection
{
    public const string NotRunning = "Parley is not running. Install and enable the Parley plugin in Dalamud.";
    public const string VersionMismatch = "Parley and this widget are different versions. Update both.";

    private readonly Func<string?> readStatus;
    private string? lastJson;

    /// <param name="readStatus">Asks Parley for its status JSON. Returns null, or throws, when Parley is not there to ask.</param>
    public ParleyConnection(Func<string?> readStatus)
    {
        this.readStatus = readStatus;
    }

    /// <summary>The most recent status, or null while Parley is unavailable.</summary>
    public StatusSnapshot? Snapshot { get; private set; }

    public string UnavailableReason { get; private set; } = NotRunning;

    /// <summary>Goes up whenever <see cref="Snapshot"/> or <see cref="UnavailableReason"/> changes.</summary>
    public int Generation { get; private set; }

    public void Refresh()
    {
        string? json;
        try
        {
            json = readStatus();
        }
        catch (Exception)
        {
            // Parley unloaded between the check and the call, or is reloading.
            json = null;
        }

        if (json == null)
        {
            lastJson = null;
            Fail(NotRunning);
            return;
        }

        // Parley hands back the same string until something changes, so this
        // is nearly always a reference comparison and nothing more.
        if (string.Equals(json, lastJson, StringComparison.Ordinal)) return;
        lastJson = json;

        StatusSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(json, ParleyIpcJson.Default.StatusSnapshot);
        }
        catch (JsonException)
        {
            snapshot = null;
        }

        if (snapshot == null || snapshot.Version != ParleyIpc.Version)
        {
            Fail(VersionMismatch);
            return;
        }

        snapshot.Conversations ??= [];
        Snapshot = snapshot;
        Generation++;
    }

    private void Fail(string reason)
    {
        if (Snapshot == null && UnavailableReason == reason) return;
        Snapshot = null;
        UnavailableReason = reason;
        Generation++;
    }
}
