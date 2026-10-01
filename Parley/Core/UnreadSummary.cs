namespace Parley.Core;

public static class UnreadSummary
{
    /// <summary>"2 Tells, 1 FC, 5 LS, 1 CWLS". Empty when there is nothing unread.</summary>
    public static string Describe(int tells, int linkshells, int crossWorld, int freeCompany = 0)
    {
        var parts = new List<string>(4);
        if (tells > 0) parts.Add(tells == 1 ? "1 Tell" : $"{tells} Tells");
        if (freeCompany > 0) parts.Add($"{freeCompany} FC");
        if (linkshells > 0) parts.Add($"{linkshells} LS");
        if (crossWorld > 0) parts.Add($"{crossWorld} CWLS");
        return string.Join(", ", parts);
    }
}
