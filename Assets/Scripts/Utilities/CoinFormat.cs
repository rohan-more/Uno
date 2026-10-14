/// <summary>
/// How coin amounts read everywhere: in full up to 9,999, then shortened with
/// K, M and B ("12.5K", "125K", "1.2M", "3.4B").
/// </summary>
public static class CoinFormat
{
    /// <summary>
    /// Rounds down, so a balance never looks bigger than it is: 9,999,999 is
    /// "9.9M", not "10M".
    /// </summary>
    public static string Short(long coins)
    {
        if (coins < 0)
            return "-" + Short(-coins);
        if (coins < 10_000)
            return coins.ToString("N0");
        if (coins < 1_000_000)
            return Shorten(coins, 1_000, "K");
        if (coins < 1_000_000_000)
            return Shorten(coins, 1_000_000, "M");
        return Shorten(coins, 1_000_000_000, "B");
    }

    private static string Shorten(long coins, long unit, string suffix)
    {
        long whole = coins / unit;
        if (whole >= 100)
            return whole + suffix; // "125K": no room for a decimal

        long tenth = coins % unit * 10 / unit;
        return tenth == 0 ? whole + suffix : $"{whole}.{tenth}{suffix}";
    }
}
