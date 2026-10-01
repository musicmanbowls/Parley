using System.Text.Json;
using Dalamud.Plugin.Ipc;
using Parley.Ipc;
using Umbra.Common;
using Una.Drawing;

namespace Umbra.Parley;

/// <summary>
/// Hands Umbra's colour profile to Parley, so its chat window can match the
/// toolbar. Runs whether or not a Parley widget is on a toolbar.
///
/// Parley cannot read these itself: Umbra's colours live inside Umbra, and the
/// only code that can see them is code Umbra has loaded, which is this.
/// </summary>
[Service]
public sealed class ThemePublisher
{
    private ICallGateSubscriber<long>? tokenGate;
    private ICallGateSubscriber<string, bool>? themeGate;

    /// <summary>
    /// Once a second, compares a fingerprint of Umbra's colours with the one
    /// Parley says it last received, and sends the colours if they differ.
    /// That covers a changed profile and a reloaded Parley with one check.
    /// </summary>
    /// <remarks>
    /// This has to be an instance method on a service. Umbra's scheduler looks
    /// for tick handlers among instance methods only, and calls them on the
    /// instance its service container made.
    /// </remarks>
    [OnTick(interval: 1000)]
    private void Publish()
    {
        // Nothing may escape: Umbra calls every tick handler from one loop, and
        // an exception from this one would cut that loop short every second.
        try
        {
            tokenGate ??= Framework.DalamudPlugin.GetIpcSubscriber<long>(ParleyIpc.GetThemeToken);
            themeGate ??= Framework.DalamudPlugin.GetIpcSubscriber<string, bool>(ParleyIpc.SetTheme);
            if (!tokenGate.HasFunction || !themeGate.HasFunction) return;

            // Each colour is hashed on its own and the hashes added up, so the
            // fingerprint does not depend on the order Umbra lists them in.
            var names = Color.GetAssignedNames();
            var fingerprint = 0UL;
            var count = 0;
            foreach (var name in names)
            {
                var hash = 14695981039346656037UL;
                foreach (var ch in name) hash = (hash ^ ch) * 1099511628211UL;
                hash = (hash ^ Color.GetNamedColor(name)) * 1099511628211UL;

                fingerprint += hash;
                count++;
            }

            if (count == 0) return;

            // Zero is what Parley reports when it has been sent nothing.
            var token = (long)(fingerprint == 0 ? 1 : fingerprint);
            if (tokenGate.InvokeFunc() == token) return;

            var theme = new UmbraTheme { Token = token };
            foreach (var name in names) theme.Colours[name] = Color.GetNamedColor(name);

            themeGate.InvokeFunc(JsonSerializer.Serialize(theme, ParleyIpcJson.Default.UmbraTheme));
        }
        catch (Exception)
        {
            // Parley unloaded between the check and the call. Next tick will
            // find the gates gone and wait for it to come back.
        }
    }
}
