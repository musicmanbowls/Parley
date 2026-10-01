namespace Parley.Core.Text;

/// <summary>
/// Characters for the symbol picker beside the reply box. Every one of them is
/// in the game's own chat font (checked against AXIS_12), so other players see
/// them as they are; colour emoji are not, and would arrive as blanks.
/// </summary>
public static class Symbols
{
    public readonly record struct Symbol(string Text, string Name);

    public sealed record Group(string Name, Symbol[] Items);

    public static readonly Group[] Groups =
    [
        new("Symbols",
        [
            new("♥", "Heart"), new("♡", "Heart outline"), new("★", "Star"), new("☆", "Star outline"),
            new("♪", "Music note"), new("♯", "Sharp"), new("♭", "Flat"), new("✓", "Tick"),
            new("☀", "Sun"), new("☁", "Cloud"), new("☂", "Umbrella"), new("☃", "Snowman"),
            new("♠", "Spade"), new("♣", "Club"), new("♦", "Diamond"), new("♤", "Spade outline"),
            new("♧", "Club outline"), new("♢", "Diamond outline"), new("♂", "Male"), new("♀", "Female"),
            new("●", "Circle"), new("○", "Circle outline"), new("◎", "Bullseye"), new("◯", "Large circle"),
            new("■", "Square"), new("□", "Square outline"), new("◆", "Diamond shape"), new("◇", "Diamond shape outline"),
            new("▲", "Triangle up"), new("△", "Triangle up outline"), new("▼", "Triangle down"), new("▽", "Triangle down outline"),
            new("→", "Arrow right"), new("←", "Arrow left"), new("↑", "Arrow up"), new("↓", "Arrow down"),
            new("⇒", "Double arrow"), new("⇔", "Both ways"), new("※", "Note mark"), new("∞", "Infinity"),
            new("°", "Degree"), new("†", "Dagger"), new("‡", "Double dagger"), new("§", "Section"),
            new("…", "Ellipsis"), new("〜", "Wave"), new("「", "Corner bracket"), new("」", "Corner bracket"),
            new("『", "Double corner bracket"), new("』", "Double corner bracket"), new("【", "Thick bracket"), new("】", "Thick bracket"),
        ]),
        new("Numbers",
        [
            new("①", "1"), new("②", "2"), new("③", "3"), new("④", "4"), new("⑤", "5"),
            new("⑥", "6"), new("⑦", "7"), new("⑧", "8"), new("⑨", "9"), new("⑩", "10"),
            new("❶", "1"), new("❷", "2"), new("❸", "3"), new("❹", "4"), new("❺", "5"),
            new("❻", "6"), new("❼", "7"), new("❽", "8"), new("❾", "9"),
            new("Ⅰ", "I"), new("Ⅱ", "II"), new("Ⅲ", "III"), new("Ⅳ", "IV"), new("Ⅴ", "V"),
            new("Ⅵ", "VI"), new("Ⅶ", "VII"), new("Ⅷ", "VIII"), new("Ⅸ", "IX"), new("Ⅹ", "X"),
        ]),
        new("Game icons", BuildGameIcons()),
    ];

    private static Symbol[] BuildGameIcons()
    {
        var icons = new List<Symbol>
        {
            new("", "High quality"), new("", "Collectable"), new("", "Glamoured"), new("", "Glamoured and dyed"),
            new("", "Dice"), new("", "Clock"), new("", "Gil"), new("", "Another world"),
            new("", "Buff"), new("", "Debuff"), new("", "Hexagon"), new("", "Prohibited"),
            new("", "Circle button"), new("", "Square button"), new("", "Cross button"), new("", "Triangle button"),
            new("", "Boxed star"), new("", "Boxed plus"), new("", "Boxed question mark"),
        };

        for (var i = 0; i < 9; i++) icons.Add(new(((char)(0xE0B1 + i)).ToString(), $"Instance {i + 1}"));
        for (var i = 0; i < 26; i++) icons.Add(new(((char)(0xE071 + i)).ToString(), $"Boxed {(char)('A' + i)}"));
        for (var i = 0; i < 10; i++) icons.Add(new(((char)(0xE08F + i)).ToString(), $"Boxed {i}"));
        for (var i = 0; i < 10; i++) icons.Add(new(((char)(0xE0E0 + i)).ToString(), $"Outlined {i}"));
        for (var i = 0; i < 6; i++) icons.Add(new(((char)(0xE0C1 + i)).ToString(), $"Boxed roman {i + 1}"));
        return [.. icons];
    }
}
