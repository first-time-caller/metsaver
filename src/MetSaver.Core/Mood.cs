namespace MetSaver;

/// <summary>
/// Cheap, explainable "does this suit right now?" scoring from a painting's title and medium:
/// night scenes after dark, snow in winter, sunrise in the morning. Returns a weight around 1 (higher = better fit).
/// </summary>
public static class Mood
{
    static readonly string[] Night = { "night", "moon", "nocturne", "candle", "lamp", "starry", "stars" };
    static readonly string[] Dusk = { "evening", "sunset", "dusk", "twilight", "sunrise", "dawn", "daybreak" };
    static readonly string[] Morning = { "morning", "dawn", "sunrise", "daybreak", "breakfast" };
    static readonly string[] Day = { "noon", "midday", "sunny", "sunlight", "beach" };
    static readonly string[] Winter = { "winter", "snow", "ice", "frost", "skating", "sleigh" };
    static readonly string[] Spring = { "spring", "blossom", "orchard", "may", "easter" };
    static readonly string[] Summer = { "summer", "bathing", "beach", "haymaking", "sunny", "harvest" };
    static readonly string[] Autumn = { "autumn", "fall", "harvest", "vintage", "grape" };

    public static double Weight(string title, string medium, DateTime now)
    {
        var text = $"{title} {medium}".ToLowerInvariant();
        double w = 1;

        bool Has(string[] words) => words.Any(text.Contains);
        int h = now.Hour;
        bool night = h >= 21 || h < 5, morning = h >= 5 && h < 10, day = h >= 10 && h < 17, dusk = h >= 17 && h < 21;

        if (Has(Night)) w *= night ? 3 : dusk ? 1.3 : 0.35;                 // night scenes: lovely at night, odd at noon
        if (Has(Dusk)) w *= dusk ? 2.5 : morning && Has(Morning) ? 2 : 0.8;
        if (Has(Morning)) w *= morning ? 2.5 : 1;
        if (Has(Day)) w *= day ? 1.6 : night ? 0.8 : 1.2;

        // Northern-hemisphere seasons.
        int m = now.Month;
        string season = m is 12 or 1 or 2 ? "winter" : m is >= 3 and <= 5 ? "spring" : m is >= 6 and <= 8 ? "summer" : "autumn";
        void Season(string name, string[] words) { if (Has(words)) w *= season == name ? 2.2 : name == "winter" || season == "winter" ? 0.5 : 0.9; }
        Season("winter", Winter); Season("spring", Spring); Season("summer", Summer); Season("autumn", Autumn);
        return w;
    }
}
