namespace KovaaksCompanion.Core.Benchmarks;

/// <summary>How hard a playlist is meant to be, read from its difficulty name.</summary>
public enum PlaylistLevel
{
    Beginner,
    Intermediate,
    Advanced,
}

public static class PlaylistLevels
{
    static readonly string[] BeginnerWords = ["novice", "beginner", "easy", "easier", "entry", "newcomer", "basic", "intro", "简单", "fundamentals"];
    static readonly string[] IntermediateWords = ["intermediate", "medium", "main", "normal", "experienced", "中等"];
    static readonly string[] AdvancedWords = ["advanced", "advance", "hard", "harder", "expert", "elite", "ultimate", "veteran", "boss", "evil", "demonic", "困难"];

    /// <summary>The level of a difficulty, or null when its name says nothing ("All", a season, a person's name).</summary>
    public static PlaylistLevel? Of(string difficultyName)
    {
        var n = difficultyName.Trim().ToLowerInvariant();
        if (n.StartsWith("pre-", StringComparison.Ordinal)) return PlaylistLevel.Intermediate;
        if (Has(n, BeginnerWords)) return PlaylistLevel.Beginner;
        if (Has(n, AdvancedWords)) return PlaylistLevel.Advanced;
        if (Has(n, IntermediateWords)) return PlaylistLevel.Intermediate;
        return null;
    }

    static bool Has(string name, string[] words) => words.Any(w => name.Contains(w, StringComparison.Ordinal));
}
