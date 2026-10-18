namespace KovaaksCompanion.Core.Library;

/// <summary>Figures over a set of runs of any scenarios (one session, or every run of the account). Accuracy and TTK are means weighted by shots and kills; other per-run means skip the runs that lack the figure; <see cref="Reloads"/> and <see cref="Kills"/> are per run; <see cref="OverShots"/> is per kill.</summary>
public sealed record RunSetSummary(int Runs, int Scenarios, int Days, TimeSpan TimePlayed, int PersonalBests, double? Accuracy, double? Kills, double? AvgTtk, double? OverShots, double? Reloads, double? Distance, int TotalKills);

/// <summary>One scenario within a run set: its runs there, best and average score, mean accuracy and TTK.</summary>
public sealed record ScenarioLine(string Scenario, int Runs, TimeSpan Time, double Best, double Average, double? Accuracy, double? AvgTtk, int PersonalBests);

/// <summary>The runs of one aim kind: runs, scenarios, time, personal bests, mean accuracy, TTK, overshots per kill and the most played sensitivity (cm per 360, null when no run knows it).</summary>
public sealed record KindLine(Trajectory.AimKind Kind, int Runs, int Scenarios, TimeSpan Time, int PersonalBests, double? Accuracy, double? AvgTtk, double? OverShots, double? Cm360);

/// <summary>Runs started in an hour block of the day: how many, and their mean accuracy.</summary>
public sealed record HourBlock(int Hour, int Runs, double? Accuracy);

/// <summary>Runs played at one sensitivity (cm per 360): how many, and their mean accuracy.</summary>
public sealed record SensLine(double Cm360, int Runs, double? Accuracy);

/// <summary>Statistics over a set of runs across scenarios, the counterpart of <see cref="ScenarioInsights"/> for a session or the whole account. Scores of different scenarios are never averaged together.</summary>
public static class RunSetInsights
{
    static double? Mean(IEnumerable<RunRecord> runs, Func<RunRecord, double?> get) =>
        runs.Select(get).OfType<double>().ToList() is { Count: > 0 } xs ? xs.Average() : null;

    /// <summary>Mean of the figure weighted by each run's weight, so a long run counts for more than a short one. Falls back to the plain mean when no run has a weight.</summary>
    static double? Weighted(IEnumerable<RunRecord> runs, Func<RunRecord, double?> get, Func<RunRecord, double> weight)
    {
        var list = runs.ToList();
        var pairs = list.Select(r => (V: get(r), W: weight(r))).Where(p => p.V.HasValue && p.W > 0).ToList();
        return pairs.Count > 0 ? pairs.Sum(p => p.V!.Value * p.W) / pairs.Sum(p => p.W) : Mean(list, get);
    }

    /// <summary>Shots at dead targets per kill over the runs that kept both, so the figure does not depend on run length.</summary>
    static double? OversPerKill(IEnumerable<RunRecord> runs)
    {
        var rs = runs.Where(r => r.OverShots.HasValue && r.Kills is > 0).ToList();
        var kills = rs.Sum(r => r.Kills!.Value);
        return kills > 0 ? (double)rs.Sum(r => r.OverShots!.Value) / kills : null;
    }

    static double ShotWeight(RunRecord r) => r.Shots ?? 0;
    static double KillWeight(RunRecord r) => r.Kills ?? 0;

    public static RunSetSummary? Summarize(IReadOnlyList<RunRecord> runs, IReadOnlySet<RunRecord> pbs)
    {
        if (runs.Count == 0) return null;
        return new RunSetSummary(runs.Count, runs.Select(r => r.Scenario).Distinct(StringComparer.OrdinalIgnoreCase).Count(), runs.Select(r => r.End.Date).Distinct().Count(),
            TimeSpan.FromTicks(runs.Sum(r => r.Duration.Ticks)), runs.Count(pbs.Contains),
            Weighted(runs, r => r.Accuracy, ShotWeight), Mean(runs, r => r.Kills), Weighted(runs, r => r.AvgTtk, KillWeight), OversPerKill(runs), Mean(runs, r => r.Reloads), Mean(runs, r => r.Distance),
            runs.Sum(r => r.Kills ?? 0));
    }

    /// <summary>One line per scenario, the most played first.</summary>
    public static List<ScenarioLine> ByScenario(IReadOnlyList<RunRecord> runs, IReadOnlySet<RunRecord> pbs) =>
        runs.GroupBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ScenarioLine(g.Key, g.Count(), TimeSpan.FromTicks(g.Sum(r => r.Duration.Ticks)), g.Max(r => r.Score), g.Average(r => r.Score),
                Weighted(g, r => r.Accuracy, ShotWeight), Weighted(g, r => r.AvgTtk, KillWeight), g.Count(pbs.Contains)))
            .OrderByDescending(l => l.Runs).ThenByDescending(l => l.Time).ToList();

    /// <summary>The aim kind of a run: from the playlist category or playlist name of its scenario, else from the scenario name; null when neither says.</summary>
    public static Trajectory.AimKind? KindOf(RunRecord r) => KindOf(r.Scenario);

    /// <summary>The aim kind of a scenario: from its playlist category or playlist name, else from its name; null when neither says.</summary>
    public static Trajectory.AimKind? KindOf(string scenario) =>
        Benchmarks.ScenarioKinds.Of(scenario) ?? Trajectory.MouseSpeedStats.KindFromName(scenario);

    /// <summary>One line per aim kind (tracking, flick, switching) that has runs; runs whose kind is unknown are left out.</summary>
    public static List<KindLine> ByKind(IReadOnlyList<RunRecord> runs, IReadOnlySet<RunRecord> pbs) =>
        runs.Select(r => (Kind: KindOf(r), Run: r)).Where(x => x.Kind != null).GroupBy(x => x.Kind!.Value).OrderBy(g => g.Key switch { Trajectory.AimKind.Tracking => 0, Trajectory.AimKind.Flick => 1, _ => 2 })
            .Select(g =>
            {
                var rs = g.Select(x => x.Run).ToList();
                return new KindLine(g.Key, rs.Count, rs.Select(r => r.Scenario).Distinct(StringComparer.OrdinalIgnoreCase).Count(), TimeSpan.FromTicks(rs.Sum(r => r.Duration.Ticks)), rs.Count(pbs.Contains),
                    Weighted(rs, r => r.Accuracy, ShotWeight), Weighted(rs, r => r.AvgTtk, KillWeight), OversPerKill(rs), MainSens(rs));
            }).ToList();

    /// <summary>The sensitivity most of the runs were played at, grouped like <see cref="BySensitivity"/>; the lower one on a tie.</summary>
    static double? MainSens(IEnumerable<RunRecord> runs) =>
        runs.Where(r => r.Cm360 is > 0).GroupBy(r => Math.Round(r.Cm360!.Value, 2)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => (double?)g.Key).FirstOrDefault();

    /// <summary>Runs by the hour block they started in, only blocks with runs. Empty for fewer than 5 runs.</summary>
    public static List<HourBlock> ByTimeOfDay(IReadOnlyList<RunRecord> runs, int blockHours = 3)
    {
        if (runs.Count < 5) return [];
        return runs.GroupBy(r => r.Start.Hour / blockHours * blockHours).OrderBy(g => g.Key)
            .Select(g => new HourBlock(g.Key, g.Count(), Weighted(g, r => r.Accuracy, ShotWeight))).ToList();
    }

    /// <summary>The five most played sensitivities, lowest first. Runs without a known sens are left out.</summary>
    public static List<SensLine> BySensitivity(IReadOnlyList<RunRecord> runs) =>
        runs.Where(r => r.Cm360 is > 0).GroupBy(r => Math.Round(r.Cm360!.Value, 2))
            .OrderByDescending(g => g.Count()).Take(5).OrderBy(g => g.Key)
            .Select(g => new SensLine(g.Key, g.Count(), Weighted(g, r => r.Accuracy, ShotWeight))).ToList();
}
