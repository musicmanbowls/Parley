using System.Text;
using System.Text.Json;

namespace Parley.Core.Settings;

/// <summary>
/// Reads and writes the configuration as plain JSON in the plugin's own
/// directory.
///
/// Dalamud's built-in plugin config is not used. It stores a type name in the
/// file and resolves it on load, which during a dev-plugin reload can resolve
/// to the previous, not yet collected copy of the assembly; the cast then
/// fails and the plugin starts from defaults and saves them over the real
/// settings. A file with no type names in it has nothing to resolve.
/// </summary>
public static class ConfigurationFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Never throws. A missing file gives defaults; an unreadable one is set
    /// aside as ".bad" so it is not silently overwritten, and gives defaults.
    /// </summary>
    public static Configuration Load(string path, Action<string, Exception>? onError = null)
    {
        Configuration? configuration = null;
        try
        {
            if (File.Exists(path))
                configuration = JsonSerializer.Deserialize(File.ReadAllBytes(path), CoreJson.Documents.Configuration);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            onError?.Invoke("read configuration", ex);
            try { File.Copy(path, path + ".bad", overwrite: true); }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                onError?.Invoke("set aside unreadable configuration", copy);
            }
        }

        configuration ??= new Configuration();
        configuration.Clamp();
        return configuration;
    }

    public static void Save(string path, Configuration configuration)
    {
        var json = JsonSerializer.Serialize(configuration, CoreJson.Documents.Configuration);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Written beside the target and renamed over it, so a crash mid-save
        // leaves the previous settings rather than half a file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, Utf8);
        File.Move(temp, path, overwrite: true);
    }
}
