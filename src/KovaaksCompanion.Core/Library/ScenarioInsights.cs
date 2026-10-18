namespace KovaaksCompanion.Core.Library;

/// <summary>One column of the summary table: the figures over a set of records (and, for accuracy, time played, TTK and overshots, the local runs among them). Overshots are the mean per run.</summary>
public sealed record ScoreSummary(int Plays, double Best, double Average, double? Accuracy, TimeSpan TimePlayed, double? AvgTtk = null, double? OverShots = null);

/// <summary>Local runs played at one sensitivity (cm per 360, the mean of the grouped runs): their count, average and best score.</summary>
public sealed record SensGroup(double Cm360, int Runs, double Average, double Best);

/// <summary>
/// Statistics of one scenario that go beyond the server's: they need every score (distribution, consistency, personal-best
/// history) or the local runs' accuracy, start times and session membership.
/// </summary>
public static class ScenarioInsights
{
    /// <summary>Figures over the given records; the local runs among them supply accuracy and time played.</summary>
    public static ScoreSummary? Summarize(IReadOnlyList<ScoreRecord> records)
    {
        if (records.Count == 0) return null;
        var scores = records.Select(r => r.Score).ToList();
        var runs = records.Select(r => r.Run).OfType<RunRecord>().ToList();
        return new ScoreSummary(records.Count, scores.Max(), scores.Average(),
            runs.Count == 0 ? null : runs.Average(r => r.Accuracy),
            TimeSpan.FromTicks(runs.Sum(r => r.Duration.Ticks)),
            runs.Select(r => r.AvgTtk).OfType<double>().ToList() is { Count: > 0 } t ? t.Average() : null,
            runs.Select(r => r.OverShots).OfType<int>().ToList() is { Count: > 0 } o ? o.Average() : null);
    }

    /// <summary>Local runs grouped by the sensitivity they were played at (runs whose cm/360 is the same after rounding to two decimals count as the same), lowest sens first. Runs without a known sens are left out.</summary>
    public static List<SensGroup> BySensitivity(IReadOnlyList<RunRecord> runs) =>
        runs.Where(r => r.Cm360 is > 0)
            .GroupBy(r => Math.Round(r.Cm360!.Value, 2))
            .OrderByDescending(g => g.Max(r => r.Score))
            .Take(5)
            .Select(g => new SensGroup(g.Key, g.Count(), g.Average(r => r.Score), g.Max(r => r.Score)))
            .ToList();
}
