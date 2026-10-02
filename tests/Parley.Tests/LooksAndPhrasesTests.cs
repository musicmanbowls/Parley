using Parley.Core;
using Parley.Core.Settings;
using Xunit;

namespace Parley.Tests;

public class LooksAndPhrasesTests
{
    private static readonly AutoTranslatePhrase[] Phrases =
    [
        new(2, 1, "Thank you."),
        new(2, 2, "No, thank you."),
        new(8, 1, "Stack up!"),
        new(8, 2, "Pull together!"),
        new(49, 1, "Company Chocobo"),
    ];

    [Fact]
    public void SearchPutsPhrasesThatStartWithTheWordFirst()
    {
        var found = AutoTranslateSearch.Find(Phrases, "thank", 10);

        Assert.Equal(["Thank you.", "No, thank you."], found.Select(phrase => phrase.Text));
    }

    [Fact]
    public void SearchFindsWordsAndPartsOfWordsAndStopsAtTheLimit()
    {
        Assert.Equal(["Company Chocobo"], AutoTranslateSearch.Find(Phrases, "choco", 10).Select(phrase => phrase.Text));
        Assert.Single(AutoTranslateSearch.Find(Phrases, "u", 1));
        Assert.Empty(AutoTranslateSearch.Find(Phrases, "  ", 10));
    }

    [Fact]
    public void EverySectionSharesOneLookUntilToldOtherwise()
    {
        var config = new Configuration { MessageStyle = MessageStyle.Log, Timestamps = TimestampStyle.None, UiScale = 0.8f };

        var tells = config.LookFor(LookSection.Tells);
        Assert.Equal((MessageStyle.Log, TimestampStyle.None, 0.8f), (tells.Layout, tells.Timestamps, tells.Scale));
        Assert.Equal(0.8f, config.LookFor(LookSection.General).Scale);
    }

    [Fact]
    public void EachSectionStartsFromTheSharedLookAndThenKeepsItsOwn()
    {
        var config = new Configuration { MessageStyle = MessageStyle.Log, SameLookEverywhere = false };

        var tells = config.LookFor(LookSection.Tells);
        Assert.Equal(MessageStyle.Log, tells.Layout);

        tells.Layout = MessageStyle.Bubbles;
        tells.Scale = 1.3f;
        Assert.Equal(MessageStyle.Bubbles, config.LookFor(LookSection.Tells).Layout);
        Assert.Equal(MessageStyle.Log, config.LookFor(LookSection.FreeCompany).Layout);
        Assert.Equal(1f, config.LookFor(LookSection.FreeCompany).Scale);
    }

    [Fact]
    public void SizesAreKeptWithinBounds()
    {
        var config = new Configuration { UiScale = 9f, SameLookEverywhere = false };
        config.LookFor(LookSection.Tells).Scale = 0.1f;

        config.Clamp();

        Assert.Equal(Configuration.MaxSectionScale, config.UiScale);
        Assert.Equal(Configuration.MinSectionScale, config.LookFor(LookSection.Tells).Scale);
    }

    [Fact]
    public void TheMainWindowStartsWithTabsAcrossAndAWindowOfItsOwnWithAList()
    {
        var look = new Configuration().LookFor(LookSection.Tells);

        Assert.Equal(TabDirection.Horizontal, look.TabsIn(poppedOut: false));
        Assert.Equal(TabDirection.Vertical, look.TabsIn(poppedOut: true));
    }

    [Fact]
    public void TabsFollowTheSharedLookUntilEachSectionHasItsOwn()
    {
        var config = new Configuration { MainWindowTabs = TabDirection.Vertical, PopOutTabs = TabDirection.Horizontal };
        Assert.Equal(TabDirection.Vertical, config.LookFor(LookSection.Linkshells).MainWindowTabs);
        Assert.Equal(TabDirection.Horizontal, config.LookFor(LookSection.CrossWorld).PopOutTabs);

        config.SameLookEverywhere = false;
        var tells = config.LookFor(LookSection.Tells);
        Assert.Equal(TabDirection.Vertical, tells.MainWindowTabs);

        tells.MainWindowTabs = TabDirection.Horizontal;
        Assert.Equal(TabDirection.Horizontal, config.LookFor(LookSection.Tells).MainWindowTabs);
        Assert.Equal(TabDirection.Vertical, config.LookFor(LookSection.Linkshells).MainWindowTabs);
        Assert.Equal(TabDirection.Horizontal, tells.Copy().MainWindowTabs);
    }

    [Fact]
    public void UnknownTabDirectionsFallBackToTheDefaults()
    {
        var config = new Configuration { MainWindowTabs = (TabDirection)7, PopOutTabs = (TabDirection)9, SameLookEverywhere = false };
        config.LookFor(LookSection.Tells).PopOutTabs = (TabDirection)5;

        config.Clamp();

        Assert.Equal(TabDirection.Horizontal, config.MainWindowTabs);
        Assert.Equal(TabDirection.Vertical, config.PopOutTabs);
        Assert.Equal(TabDirection.Vertical, config.LookFor(LookSection.Tells).PopOutTabs);
    }

    [Fact]
    public void SettingsWrittenAsTextReadBackTheSame()
    {
        var path = Path.Combine(Path.GetTempPath(), $"parley-config-{Guid.NewGuid():N}.json");
        try
        {
            var config = new Configuration { MainWindowTabs = TabDirection.Vertical, TabIconsOnly = true, SameLookEverywhere = false };
            config.LookFor(LookSection.Linkshells).PopOutTabs = TabDirection.Horizontal;

            ConfigurationFile.Write(path, ConfigurationFile.Serialize(config));
            var read = ConfigurationFile.Load(path);

            Assert.Equal(TabDirection.Vertical, read.MainWindowTabs);
            Assert.True(read.TabIconsOnly);
            Assert.Equal(TabDirection.Horizontal, read.LookFor(LookSection.Linkshells).PopOutTabs);
            Assert.Contains("\"Vertical\"", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConversationKindsMapToTheirSections()
    {
        Assert.Equal(LookSection.Tells, Configuration.SectionOf(ChannelGroup.Tell));
        Assert.Equal(LookSection.FreeCompany, Configuration.SectionOf(ChannelGroup.FreeCompany));
        Assert.Equal(LookSection.Linkshells, Configuration.SectionOf(ChannelGroup.Linkshell));
        Assert.Equal(LookSection.CrossWorld, Configuration.SectionOf(ChannelGroup.CrossWorld));
    }
}
