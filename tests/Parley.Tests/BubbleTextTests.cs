using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class BubbleTextTests
{
    private const ushort FreeCompany = 24;
    private const ushort TellOutgoing = 12;

    private static readonly byte[] Words = "Heading out now"u8.ToArray();

    [Fact]
    public void TheBubbleForAHiddenLineGetsItsWords()
    {
        var text = new BubbleText();
        text.Hold(FreeCompany, Words);

        Assert.Same(Words, text.Take(FreeCompany));
    }

    [Fact]
    public void WordsGoToOneBubbleOnly()
    {
        var text = new BubbleText();
        text.Hold(FreeCompany, Words);
        text.Take(FreeCompany);

        Assert.False(text.Holding);
        Assert.Null(text.Take(FreeCompany));
    }

    [Fact]
    public void WhoSentItDoesNotStopTheMatch()
    {
        // The game passes the whole line info for your own lines: the kind of
        // chat in the low seven bits, then who it is from and to.
        var text = new BubbleText();
        text.Hold(TellOutgoing, Words);

        Assert.Same(Words, text.Take((ushort)(TellOutgoing | (1 << 11))));
    }

    [Fact]
    public void ABubbleForAnotherKindOfChatIsLeftAloneAndTheWordsDropped()
    {
        var text = new BubbleText();
        text.Hold(FreeCompany, Words);

        Assert.Null(text.Take(10));
        Assert.False(text.Holding);
    }

    [Fact]
    public void TheNextLineDropsWordsThatGotNoBubble()
    {
        var text = new BubbleText();
        text.Hold(FreeCompany, Words);
        text.Forget();

        Assert.Null(text.Take(FreeCompany));
    }

    [Fact]
    public void NothingHeldMeansNothingGiven()
    {
        Assert.Null(new BubbleText().Take(FreeCompany));
    }
}
