using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class PlayerNameTests
{
    [Fact]
    public void Splits_name_and_world_and_tidies_the_name()
    {
        Assert.True(PlayerName.TrySplit("  alice   smith@Gilgamesh ", out var name, out var world));
        Assert.Equal("Alice Smith", name);
        Assert.Equal("Gilgamesh", world);
    }

    [Theory]
    [InlineData("Alice Smith")]
    [InlineData("Alice Smith@")]
    [InlineData("@Gilgamesh")]
    public void Without_both_parts_there_is_no_split(string input)
    {
        Assert.False(PlayerName.TrySplit(input, out _, out _));
    }

    [Fact]
    public void A_name_with_no_world_is_still_tidied()
    {
        PlayerName.TrySplit("y'shtola rhul", out var name, out _);
        Assert.Equal("Y'shtola Rhul", name);
    }

    [Theory]
    [InlineData("Alice Smith", true)]
    [InlineData("Y'shtola Rhul", true)]
    [InlineData("A", false)]
    [InlineData("/shout hello", false)]
    [InlineData("Alice@Smith", false)]
    [InlineData("Alice\nSmith", false)]
    [InlineData("Alice <t>", false)]
    public void Plausibility_keeps_out_what_would_change_the_tell_line(string name, bool expected)
    {
        Assert.Equal(expected, PlayerName.IsPlausible(name));
    }

    [Theory]
    [InlineData("Alice Smith", "AS")]
    [InlineData("y'shtola rhul", "YR")]
    [InlineData("Hunts", "Hu")]
    [InlineData("The Hunt Club", "TH")]
    [InlineData("", "?")]
    [InlineData("  ", "?")]
    public void Initials(string name, string expected)
    {
        Assert.Equal(expected, PlayerName.Initials(name));
    }
}

public class ConversationKeyTests
{
    [Fact]
    public void Keys_name_the_kind_of_conversation()
    {
        Assert.Equal("t:Alice Smith@63", ConversationKey.ForTell("Alice Smith", 63));
        Assert.Equal("l:Hunts", ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "Hunts"));
        Assert.Equal("c:Hunts", ConversationKey.ForLinkshell(ChannelGroup.CrossWorld, "Hunts"));
    }

    [Fact]
    public void A_slot_placeholder_can_never_collide_with_a_linkshell_name()
    {
        var placeholder = ConversationKey.ForSlot(ChannelGroup.Linkshell, 3);

        Assert.NotEqual(placeholder, ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "3"));
        Assert.NotEqual(placeholder, ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "#3"));
        Assert.NotEqual(placeholder, ConversationKey.ForSlot(ChannelGroup.CrossWorld, 3));
        Assert.NotEqual(placeholder, ConversationKey.ForSlot(ChannelGroup.Linkshell, 4));
    }

    [Fact]
    public void Files_are_grouped_by_kind()
    {
        Assert.Equal("tells/Alice Smith@Gilgamesh.jsonl", ConversationKey.FileFor(ChannelGroup.Tell, "Alice Smith", "Gilgamesh"));
        Assert.Equal("linkshells/Hunts.jsonl", ConversationKey.FileFor(ChannelGroup.Linkshell, "Hunts", ""));
        Assert.Equal("crossworld/Hunts.jsonl", ConversationKey.FileFor(ChannelGroup.CrossWorld, "Hunts", ""));
    }

    [Theory]
    [InlineData("Alice Smith@Gilgamesh")]
    [InlineData("J'mhal Tia")]
    [InlineData("Hunt-Train 2")]
    public void Ordinary_names_are_left_alone(string name)
    {
        Assert.Equal(name, ConversationKey.SafeFileName(name));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("star*")]
    [InlineData("trailing.")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("")]
    public void Unsafe_names_become_legal_file_names(string name)
    {
        var safe = ConversationKey.SafeFileName(name);

        Assert.NotEqual(name, safe);
        Assert.True(safe.Length > 0);
        Assert.Equal(-1, safe.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']));
        Assert.False(safe.EndsWith('.') || safe.EndsWith(' '));
        Assert.Contains('~', safe);
    }

    [Fact]
    public void Names_that_differ_only_in_a_stripped_character_stay_distinct()
    {
        Assert.NotEqual(ConversationKey.SafeFileName("a/b"), ConversationKey.SafeFileName("a?b"));
    }

    [Fact]
    public void Very_long_names_are_shortened()
    {
        var safe = ConversationKey.SafeFileName(new string('x', 300));
        Assert.True(safe.Length < 100);
    }
}
