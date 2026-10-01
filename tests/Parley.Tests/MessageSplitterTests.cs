using System.Text;
using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class MessageSplitterTests
{
    [Fact]
    public void Text_within_the_budget_is_one_untouched_part()
    {
        var result = MessageSplitter.Split("hello  there", 100);

        Assert.False(result.TooLong);
        Assert.Equal(["hello  there"], result.Parts);
    }

    [Fact]
    public void Line_breaks_and_tabs_become_single_spaces()
    {
        Assert.Equal("one two three", MessageSplitter.Normalise("  one\r\ntwo\t\tthree\n"));
    }

    [Fact]
    public void Empty_text_gives_no_parts()
    {
        var result = MessageSplitter.Split(" \r\n ", 100);

        Assert.Empty(result.Parts);
        Assert.False(result.TooLong);
    }

    [Fact]
    public void Long_text_is_cut_between_words_and_loses_nothing()
    {
        var words = Enumerable.Range(0, 60).Select(i => $"word{i}").ToArray();
        var text = string.Join(' ', words);

        var result = MessageSplitter.Split(text, 80);

        Assert.False(result.TooLong);
        Assert.True(result.Parts.Count > 1);
        Assert.All(result.Parts, part => Assert.True(Encoding.UTF8.GetByteCount(part) <= 80));
        Assert.Equal(text, string.Join(' ', result.Parts));
    }

    [Fact]
    public void Budget_is_measured_in_bytes_not_characters()
    {
        // Three bytes each in UTF-8, so ten characters is thirty bytes.
        var text = "こんにちは世界こんにちは世界";

        var result = MessageSplitter.Split(text, 10);

        Assert.All(result.Parts, part => Assert.True(Encoding.UTF8.GetByteCount(part) <= 10));
        Assert.Equal(text, string.Concat(result.Parts));
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 12));

        var result = MessageSplitter.Split(text, 10);

        Assert.All(result.Parts, part =>
        {
            Assert.True(Encoding.UTF8.GetByteCount(part) <= 10);
            Assert.DoesNotContain('�', part);
            Assert.Equal(0, part.Length % 2);
        });
        Assert.Equal(text, string.Concat(result.Parts));
    }

    [Fact]
    public void One_unbroken_run_longer_than_a_line_is_cut_inside_the_run()
    {
        var text = "see " + new string('x', 250) + " ok";

        var result = MessageSplitter.Split(text, 100);

        Assert.All(result.Parts, part => Assert.True(Encoding.UTF8.GetByteCount(part) <= 100));
        Assert.Equal(250, result.Parts.Sum(part => part.Count(ch => ch == 'x')));
        Assert.StartsWith("see", result.Parts[0]);
        Assert.EndsWith("ok", result.Parts[^1]);
    }

    [Fact]
    public void More_parts_than_the_limit_is_reported_as_too_long()
    {
        var text = string.Join(' ', Enumerable.Repeat("abcdefghi", 40));

        var result = MessageSplitter.Split(text, 20);

        Assert.True(result.Parts.Count > MessageSplitter.MaxParts);
        Assert.True(result.TooLong);
    }

    [Fact]
    public void A_budget_too_small_for_any_character_is_refused_rather_than_looped_on()
    {
        var result = MessageSplitter.Split("hello", 2);

        Assert.True(result.TooLong);
        Assert.Empty(result.Parts);
    }
}
