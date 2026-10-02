using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class ChatLogFilterTests
{
    [Fact]
    public void PacksKindTargetAndSourceTheWayTheGameDoes()
    {
        Assert.Equal(24, ChatLogFilter.Pack(24));
        Assert.Equal(41 | (1 << 11), ChatLogFilter.Pack(41, source: 1));
        Assert.Equal(45 | (4 << 7) | (2 << 11), ChatLogFilter.Pack(45, target: 4, source: 2));
    }

    [Fact]
    public void ASetBitLeavesThatKindOut()
    {
        var hidden = new byte[4096];
        var party = ChatLogFilter.Pack(14);
        hidden[party >> 3] |= (byte)(1 << (party & 7));

        Assert.False(ChatLogFilter.Shows(hidden, party));
        Assert.True(ChatLogFilter.Shows(hidden, ChatLogFilter.Pack(10)));
        Assert.True(ChatLogFilter.Shows(hidden, ChatLogFilter.Pack(15)));
    }

    [Fact]
    public void TheBitDependsOnWhoTheLineIsFrom()
    {
        var hidden = new byte[4096];
        var yourDamage = ChatLogFilter.Pack(41, source: 1);
        hidden[yourDamage >> 3] |= (byte)(1 << (yourDamage & 7));

        Assert.False(ChatLogFilter.Shows(hidden, yourDamage));
        Assert.True(ChatLogFilter.Shows(hidden, ChatLogFilter.Pack(41, source: 2)));
    }

    [Fact]
    public void AnUnreadableOrShortTableShowsEverything()
    {
        Assert.True(ChatLogFilter.Shows(null, ChatLogFilter.Pack(14)));
        Assert.True(ChatLogFilter.Shows(new byte[16], ChatLogFilter.Pack(41, source: 1)));
    }
}
