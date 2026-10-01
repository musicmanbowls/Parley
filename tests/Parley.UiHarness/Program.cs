using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Parley.UiHarness;

/// <summary>
/// Usage: Parley.UiHarness [output directory] [scenario name filter | guide]
///
/// Runs every scenario, writes a PNG for each screenshot a scenario takes, and
/// exits non-zero if any check failed, any window threw, or ImGui raised an
/// assertion. With "guide" it draws the pictures for the install guide instead.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var dalamud = Environment.GetEnvironmentVariable("DALAMUD_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "addon", "Hooks", "dev");
        if (!File.Exists(Path.Combine(dalamud, "Dalamud.dll")))
        {
            Console.Error.WriteLine($"Dalamud was not found in {dalamud}. Set DALAMUD_HOME to the folder containing Dalamud.dll.");
            return 2;
        }

        // Dalamud and everything it depends on are loaded from where Dalamud
        // lives, rather than copied. This has to be in place before any
        // method that mentions one of their types is compiled, which is why
        // the real work is in another method.
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(dalamud, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };

        var output = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "shots");
        var filter = args.Length > 1 ? args[1] : null;
        return Run(output, FindAssets(dalamud), filter);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string output, string assets, string? filter) =>
        filter == "guide" ? Guide.Run(output, assets) : Scenarios.RunAll(output, assets, filter);

    /// <summary>Dalamud's fonts live in a versioned folder beside the one Dalamud itself is in.</summary>
    private static string FindAssets(string dalamud)
    {
        var overridden = Environment.GetEnvironmentVariable("DALAMUD_ASSETS");
        if (!string.IsNullOrEmpty(overridden)) return overridden;

        var root = Path.GetFullPath(Path.Combine(dalamud, "..", "..", "..", "dalamudAssets"));
        if (Directory.Exists(root))
        {
            foreach (var directory in Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                var candidate = Path.Combine(directory, "UIRes");
                if (File.Exists(Path.Combine(candidate, "NotoSansCJKjp-Medium.otf"))) return candidate;
            }
        }

        throw new DirectoryNotFoundException($"Dalamud's assets were not found under {root}. Set DALAMUD_ASSETS to the UIRes folder.");
    }
}
