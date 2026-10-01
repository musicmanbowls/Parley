using System.Numerics;
using Parley.Core;

namespace Parley.UiHarness;

/// <summary>
/// The pictures for the install guide: Parley's own windows as they really
/// draw, and stand-ins for the Dalamud windows the guide walks through, at a
/// UI scale that prints sharply.
/// </summary>
internal static class Guide
{
    private const float Scale = 1.5f;

    /// <summary>The address people add in Dalamud to get Parley, as the README gives it.</summary>
    private const string RepoUrl = "https://raw.githubusercontent.com/musicmanbowls/Parley/main/repo.json";

    /// <summary>The version on the installer's button in the pictures.</summary>
    private const string Version = "1.0.0.0";

    public static int Run(string output, string assets)
    {
        var pictures = new List<string>();

        using (var stage = new Stage(output, assets, Scale, width: 1320, height: 900))
        {
            Demo.Seed(stage);
            stage.Plugin.Config.JumpToUnreadOnOpen = false;
            var main = stage.Plugin.MainWindow;

            main.Show(stage.Plugin.Store.FindTellByName("Mira Thorne"));
            stage.Frames(6);
            stage.MoveTo(new Vector2(stage.Width - 4f, stage.Height - 4f));
            stage.Frames(2);
            pictures.Add(stage.ShotOf("guide-parley", main));

            main.Show(stage.Plugin.Store.Find(ConversationKey.ForLinkshell(ChannelGroup.FreeCompany, "Lanternlight Society")));
            stage.Frames(6);
            pictures.Add(stage.ShotOf("guide-free-company", main));
            main.IsOpen = false;
            stage.Frames(2);

            var settings = stage.Plugin.SettingsWindow;
            stage.Place(settings, new Vector2(16f, 16f));
            stage.Plugin.Config.Alert(ChannelGroup.Tell).Sound = Core.Settings.AlertSound.Chime;
            stage.Plugin.Config.Alert(ChannelGroup.FreeCompany).Sound = Core.Settings.AlertSound.Game;
            stage.Plugin.Config.Alert(ChannelGroup.FreeCompany).GameSound = 3;
            stage.Plugin.OpenSettings();
            stage.Frames(4);
            stage.ClickText("Notifications");
            stage.MoveTo(new Vector2(stage.Width - 4f, stage.Height - 4f));
            stage.Frames(3);
            pictures.Add(stage.ShotOf("guide-notifications", settings));

            if (stage.Problems.Count > 0) return Fail(stage.Problems);
        }

        using (var stage = new Stage(output, assets, Scale, width: 1320, height: 1040))
        {
            var dalamud = new MockDalamudSettings { IsOpen = true };
            stage.Extra.Add(dalamud);
            stage.Place(dalamud, new Vector2(48f, 16f));
            stage.Frames(4);
            pictures.Add(stage.ShotOf("guide-dalamud-settings", dalamud, margin: 48f));

            dalamud.IsOpen = false;
            var installer = new MockPluginInstaller { IsOpen = true };
            stage.Extra.Add(installer);
            stage.Place(installer, new Vector2(48f, 16f));
            stage.Frames(4);
            pictures.Add(stage.ShotOf("guide-dalamud-installer", installer, margin: 48f));
            installer.IsOpen = false;

            // The same two windows for installing from Parley's plugin list on GitHub.
            var repo = new MockDalamudSettings(RepoUrl) { IsOpen = true };
            stage.Extra.Add(repo);
            stage.Place(repo, new Vector2(48f, 16f));
            stage.Frames(4);
            pictures.Add(stage.ShotOf("guide-repo-settings", repo, margin: 48f));
            repo.IsOpen = false;

            var install = new MockPluginInstaller(Version) { IsOpen = true };
            stage.Extra.Add(install);
            stage.Place(install, new Vector2(48f, 16f));
            stage.Frames(4);
            pictures.Add(stage.ShotOf("guide-repo-install", install, margin: 48f));

            if (stage.Problems.Count > 0) return Fail(stage.Problems);
        }

        // The plugin's icon, at the largest size Dalamud takes.
        using (var stage = new Stage(output, assets, 1f, width: 600, height: 600))
        {
            var icon = new IconWindow(512f) { IsOpen = true };
            stage.Extra.Add(icon);
            stage.Place(icon, new Vector2(40f, 40f));
            stage.Frames(3);
            pictures.Add(stage.ShotOf("icon", icon, margin: 0f));

            if (stage.Problems.Count > 0) return Fail(stage.Problems);
        }

        foreach (var picture in pictures) Console.WriteLine(picture);
        return 0;
    }

    private static int Fail(List<string> problems)
    {
        foreach (var problem in problems) Console.WriteLine($"FAIL {problem}");
        return 1;
    }
}
