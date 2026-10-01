using Parley.Core;
using Parley.Core.Settings;
using Xunit;

namespace Parley.Tests;

public class ConfigurationTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string ConfigPath => temp.File("config.json");

    [Fact]
    public void A_missing_file_gives_defaults()
    {
        var configuration = ConfigurationFile.Load(ConfigPath);

        Assert.True(configuration.CaptureTells);
        Assert.True(configuration.SaveHistory);
        Assert.Equal(ThemeMode.Dalamud, configuration.Theme);
        Assert.Equal(MessageStyle.Bubbles, configuration.MessageStyle);
        Assert.Equal(DtrMode.Auto, configuration.DtrMode);
    }

    [Fact]
    public void Settings_round_trip_including_a_false_that_overrides_a_true_default()
    {
        var saved = new Configuration
        {
            CaptureTells = false,
            FocusInputOnOpen = false,
            SaveHistory = false,
            OpenOnIncomingTell = true,
            Theme = ThemeMode.Umbra,
            MessageStyle = MessageStyle.Log,
            Timestamps = TimestampStyle.EveryMessage,
            Font = FontChoice.GameAxis,
            DtrMode = DtrMode.Off,
            FontScale = 1.25f,
            SidebarWidth = 260f,
            LastGroup = ChannelGroup.CrossWorld,
            CycleWithAltR = false,
            CustomColours = { ["Accent"] = "#112233FF" },
            NameColours = { ["Mira Thorne@Jenova"] = "#FF8800FF" },
        };
        saved.Alert(ChannelGroup.FreeCompany).Sound = AlertSound.Bell;
        saved.Alert(ChannelGroup.FreeCompany).Volume = 35;
        saved.Alert(ChannelGroup.Linkshell).Sound = AlertSound.File;
        saved.Alert(ChannelGroup.Linkshell).File = @"C:\Sounds\ping.wav";
        saved.Alert(ChannelGroup.Tell).Sound = AlertSound.Game;
        saved.Alert(ChannelGroup.Tell).GameSound = 7;
        saved.Alert(ChannelGroup.Tell).Toast = true;

        ConfigurationFile.Save(ConfigPath, saved);
        var loaded = ConfigurationFile.Load(ConfigPath);

        Assert.False(loaded.CaptureTells);
        Assert.False(loaded.FocusInputOnOpen);
        Assert.False(loaded.SaveHistory);
        Assert.True(loaded.OpenOnIncomingTell);
        Assert.Equal(ThemeMode.Umbra, loaded.Theme);
        Assert.Equal(MessageStyle.Log, loaded.MessageStyle);
        Assert.Equal(TimestampStyle.EveryMessage, loaded.Timestamps);
        Assert.Equal(FontChoice.GameAxis, loaded.Font);
        Assert.Equal(DtrMode.Off, loaded.DtrMode);
        Assert.Equal(1.25f, loaded.FontScale);
        Assert.Equal(260f, loaded.SidebarWidth);
        Assert.Equal(ChannelGroup.CrossWorld, loaded.LastGroup);
        Assert.False(loaded.CycleWithAltR);
        Assert.Equal("#112233FF", loaded.CustomColours["Accent"]);
        Assert.Equal("#FF8800FF", loaded.NameColours["Mira Thorne@Jenova"]);

        Assert.Equal(AlertSound.Bell, loaded.Alert(ChannelGroup.FreeCompany).Sound);
        Assert.Equal(35, loaded.Alert(ChannelGroup.FreeCompany).Volume);
        Assert.Equal(AlertSound.File, loaded.Alert(ChannelGroup.Linkshell).Sound);
        Assert.Equal(@"C:\Sounds\ping.wav", loaded.Alert(ChannelGroup.Linkshell).File);
        Assert.Equal(AlertSound.Game, loaded.Alert(ChannelGroup.Tell).Sound);
        Assert.Equal(7, loaded.Alert(ChannelGroup.Tell).GameSound);
        Assert.True(loaded.Alert(ChannelGroup.Tell).Toast);
        Assert.Equal(AlertSound.None, loaded.Alert(ChannelGroup.CrossWorld).Sound);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Alerts_are_saved_by_name_and_the_old_tell_settings_are_not_written_again()
    {
        var saved = new Configuration();
        saved.Alert(ChannelGroup.FreeCompany).Sound = AlertSound.Chime;
        ConfigurationFile.Save(ConfigPath, saved);

        var text = File.ReadAllText(ConfigPath);

        Assert.Contains("\"FreeCompany\"", text);
        Assert.Contains("\"Sound\": \"Chime\"", text);
        Assert.DoesNotContain("TellSound", text);
        Assert.DoesNotContain("ToastOnTell", text);
    }

    [Fact]
    public void The_tell_sound_and_notification_of_an_older_file_carry_over()
    {
        File.WriteAllText(ConfigPath, "{ \"Version\": 1, \"TellSound\": 5, \"ToastOnTell\": true }");

        var loaded = ConfigurationFile.Load(ConfigPath);
        var tell = loaded.Alert(ChannelGroup.Tell);

        Assert.Equal(AlertSound.Game, tell.Sound);
        Assert.Equal(5, tell.GameSound);
        Assert.True(tell.Toast);
        Assert.Equal(0, loaded.TellSound);
        Assert.False(loaded.ToastOnTell);
        Assert.Equal(AlertSound.None, loaded.Alert(ChannelGroup.Linkshell).Sound);
    }

    [Fact]
    public void Every_kind_of_conversation_has_alert_settings_after_loading()
    {
        var loaded = ConfigurationFile.Load(ConfigPath);

        foreach (var group in ChannelGroups.All)
        {
            Assert.True(loaded.Alerts.ContainsKey(group));
            Assert.Equal(AlertSound.None, loaded.Alerts[group].Sound);
        }
    }

    [Fact]
    public void Changing_a_name_colour_is_noticed()
    {
        var configuration = new Configuration();
        var before = configuration.NameColoursVersion;

        configuration.SetNameColour(Configuration.NameKey("Mira Thorne", "Jenova"), "#FF0000FF");
        Assert.Equal("#FF0000FF", configuration.NameColours["Mira Thorne@Jenova"]);
        Assert.NotEqual(before, configuration.NameColoursVersion);

        var picked = configuration.NameColoursVersion;
        configuration.SetNameColour("Mira Thorne@Jenova", null);
        Assert.Empty(configuration.NameColours);
        Assert.NotEqual(picked, configuration.NameColoursVersion);
    }

    [Fact]
    public void The_file_is_something_a_person_can_read_and_edit()
    {
        ConfigurationFile.Save(ConfigPath, new Configuration { Theme = ThemeMode.Game, LastGroup = ChannelGroup.Linkshell });

        var text = File.ReadAllText(ConfigPath);

        Assert.Contains("\"Theme\": \"Game\"", text);
        Assert.Contains("\"LastGroup\": \"Linkshell\"", text);
        Assert.Contains("\"CaptureTells\": true", text);
        Assert.DoesNotContain("$type", text);
    }

    [Fact]
    public void Settings_missing_from_an_older_file_take_their_defaults()
    {
        File.WriteAllText(ConfigPath, "{ \"Version\": 1, \"SidebarPreviews\": false, // hand-edited\n }");

        var loaded = ConfigurationFile.Load(ConfigPath);

        Assert.False(loaded.SidebarPreviews);
        Assert.True(loaded.CaptureLinkshells);
        Assert.True(loaded.CaptureFreeCompany);
        Assert.True(loaded.CycleWithAltR);
        Assert.Equal(200f, loaded.SidebarWidth);
        Assert.NotNull(loaded.CustomColours);
    }

    [Theory]
    [InlineData("{ \"Theme\": \"Neon\" }")]
    [InlineData("{ this is not json")]
    [InlineData("")]
    public void An_unreadable_file_gives_defaults_and_is_set_aside_not_overwritten(string content)
    {
        File.WriteAllText(ConfigPath, content);
        var problems = new List<string>();

        var loaded = ConfigurationFile.Load(ConfigPath, (what, _) => problems.Add(what));

        Assert.Equal(ThemeMode.Dalamud, loaded.Theme);
        Assert.NotEmpty(problems);
        Assert.Equal(content, File.ReadAllText(ConfigPath + ".bad"));
    }

    [Fact]
    public void Out_of_range_values_are_pulled_back_in()
    {
        var configuration = new Configuration
        {
            SplitDelayMs = 5,
            RetentionDays = -4,
            PageSize = 1,
            MaxLoadedMessages = 10_000_000,
            FontScale = float.NaN,
            WindowOpacity = 0f,
            SidebarWidth = 9999f,
            Theme = (ThemeMode)42,
            LastGroup = (ChannelGroup)9,
            CustomColours = null!,
            NameColours = null!,
            Alerts = null!,
        };
        configuration.Alert(ChannelGroup.Tell).GameSound = 99;
        configuration.Alert(ChannelGroup.Tell).Volume = -5;
        configuration.Alert(ChannelGroup.Linkshell).Sound = (AlertSound)77;
        configuration.Alert(ChannelGroup.Linkshell).File = null!;

        configuration.Clamp();

        Assert.Equal(300, configuration.SplitDelayMs);
        Assert.Equal(16, configuration.Alert(ChannelGroup.Tell).GameSound);
        Assert.Equal(0, configuration.Alert(ChannelGroup.Tell).Volume);
        Assert.Equal(AlertSound.None, configuration.Alert(ChannelGroup.Linkshell).Sound);
        Assert.Equal(string.Empty, configuration.Alert(ChannelGroup.Linkshell).File);
        Assert.NotNull(configuration.NameColours);
        Assert.Equal(0, configuration.RetentionDays);
        Assert.Equal(20, configuration.PageSize);
        Assert.Equal(5000, configuration.MaxLoadedMessages);
        Assert.Equal(1f, configuration.FontScale);
        Assert.Equal(0.3f, configuration.WindowOpacity);
        Assert.Equal(480f, configuration.SidebarWidth);
        Assert.Equal(ThemeMode.Dalamud, configuration.Theme);
        Assert.Equal(ChannelGroup.Tell, configuration.LastGroup);
        Assert.NotNull(configuration.CustomColours);
    }
}
