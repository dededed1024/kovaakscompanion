using KovaaksCompanion.Core.Trajectory;

namespace KovaaksCompanion.Core.Library;

/// <summary>One piece of advice: a format string (also the localization key) and the figures it is filled with.</summary>
public sealed record Advice(string Format, object[] Args);

/// <summary>
/// Advice drawn from the statistics of one scenario's own runs. Every rule compares the player with themselves (best third of runs
/// against worst third, one sensitivity against another) and stays silent when the data is too thin or the gap too small to mean anything.
/// Runs are ranked against the player's form at the time (<see cref="ByForm{T}"/>), not by raw score: raw scores rise with practice, so the
/// best third would otherwise be the latest runs and every habit that changed over time would look like what makes a good run.
/// </summary>
public static class ScenarioAdvice
{
    /// <summary>Runs needed before the best and worst thirds are compared.</summary>
    const int MinRuns = 12;
    /// <summary>Runs a sensitivity or hour block needs to count.</summary>
    const int MinGroup = 4;
    /// <summary>Records needed before a plateau is judged.</summary>
    const int MinPlateau = 40;

    /// <param name="speeds">Mouse-speed figures of the runs that have them, with each run's end and score.</param>
    public static List<Advice> Build(ScenarioHistory s, IReadOnlyList<(DateTime End, double Score, RunSpeed Speed)>? speeds = null, int max = 10)
    {
        var runs = s.Runs;
        var list = new List<Advice>();
        void Add(Advice? a) { if (a != null) list.Add(a); }
        var tracking = (MouseSpeedStats.KindFromName(s.Scenario) ?? (speeds is { Count: > 0 } ? MouseSpeedStats.KindFromRuns(speeds.Select(x => x.Speed).ToList()) : null)) == AimKind.Tracking;

        // Most urgent first: a slump is about the session in progress, a plateau about what to practise next.
        Add(Slump(s));
        Add(Plateau(s));
        if (runs.Count >= MinRuns)
        {
            var ordered = ByForm(runs, r => r.Score);
            var third = ordered.Count / 3;
            var top = ordered.Take(third).ToList();
            var bottom = ordered.TakeLast(third).ToList();
            Add(Sensitivity(runs));
            Add(AccuracyGap(top, bottom, tracking));
            Add(TtkGap(top, bottom));
            Add(Overshooting(runs));
        }
        Add(Consistency(s));
        var sittings = Sittings(runs);
        Add(ColdStart(sittings));
        Add(LongStreak(sittings));
        if (speeds is { Count: >= MinRuns }) foreach (var a in Mouse(s.Scenario, speeds)) list.Add(a);
        return list.Take(max).ToList();
    }

    /// <summary>Runs on each side whose mean is a run's form.</summary>
    const int FormWindow = 5;

    /// <summary>
    /// The runs (oldest first) ordered from furthest above to furthest below the player's form at the time: each score against the mean
    /// of the <see cref="FormWindow"/> runs on each side of it.
    /// </summary>
    static List<T> ByForm<T>(IReadOnlyList<T> chronological, Func<T, double> score)
    {
        var n = chronological.Count;
        var rel = new double[n];
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            var count = 0;
            for (var j = Math.Max(0, i - FormWindow); j <= Math.Min(n - 1, i + FormWindow); j++)
                if (j != i) { sum += score(chronological[j]); count++; }
            rel[i] = count > 0 && sum > 0 ? score(chronological[i]) * count / sum : 1;
        }
        return Enumerable.Range(0, n).OrderByDescending(i => rel[i]).Select(i => chronological[i]).ToList();
    }

    static double? Mean(IEnumerable<RunRecord> runs, Func<RunRecord, double?> get) =>
        runs.Select(get).OfType<double>().ToList() is { Count: > 0 } xs ? xs.Average() : null;

    /// <summary>
    /// The sensitivity with the best average score, when it beats the most played one by at least 3%. The most played one only counts its runs
    /// from the time the other was played, so a sensitivity tried later, after more practice, does not win on practice alone.
    /// </summary>
    static Advice? Sensitivity(IReadOnlyList<RunRecord> runs)
    {
        var groups = runs.Where(r => r.Cm360 is > 0).GroupBy(r => Math.Round(r.Cm360!.Value, 2)).Where(g => g.Count() >= MinGroup).ToList();
        if (groups.Count < 2) return null;
        var main = groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        var best = groups.OrderByDescending(g => g.Average(r => r.Score)).First();
        if (best.Key == main.Key) return null;
        var (from, to) = (best.Min(r => r.Start), best.Max(r => r.End));
        var same = main.Where(r => r.End >= from && r.Start <= to).ToList();
        if (same.Count < MinGroup) return null;
        var gain = best.Average(r => r.Score) / same.Average(r => r.Score) - 1;
        if (gain < 0.03) return null;
        // More cm per 360 is a lower sensitivity: it trades reach for fine control.
        return best.Key > main.Key
            ? new Advice("Your average is {0:0}% higher at {1} cm/360 ({2} runs) than at your usual {3} cm/360. This scenario rewards **fine control** more than reach, so try playing it at the lower sensitivity more often.", [gain * 100, best.Key, best.Count(), main.Key])
            : new Advice("Your average is {0:0}% higher at {1} cm/360 ({2} runs) than at your usual {3} cm/360. This scenario rewards **reaching targets quickly** more than fine control, so try playing it at the higher sensitivity more often.", [gain * 100, best.Key, best.Count(), main.Key]);
    }

    /// <summary>Whether the best runs are the accurate ones (then accuracy first) or the sloppy ones (then speed pays). In tracking, accuracy is the share of time spent on the target.</summary>
    static Advice? AccuracyGap(List<RunRecord> top, List<RunRecord> bottom, bool tracking)
    {
        if (Mean(top, r => r.Accuracy) is not { } a || Mean(bottom, r => r.Accuracy) is not { } b) return null;
        if (tracking) return a - b >= 0.02 ? new Advice("Your best runs stay on target {0:P0} of the time, against {1:P0} in your worst. Score here comes from **staying on the target**, so match the target's movement smoothly instead of chasing it with big corrections.", [a, b]) : null;
        if (a - b >= 0.02) return new Advice("Your best runs hit {0:P0} of their shots, against {1:P0} in your worst. High scores here come from **clean hits** rather than rushing, so aim to land each shot reliably before trying to go faster.", [a, b]);
        if (b - a >= 0.02) return new Advice("Your best runs hit {0:P0} of their shots, less than your worst ({1:P0}). Your high scores come from **speed rather than precision**, so it is worth taking the riskier shot here.", [a, b]);
        return null;
    }

    /// <summary>Shots at targets that were already dead, per kill.</summary>
    static Advice? Overshooting(IReadOnlyList<RunRecord> runs)
    {
        var rs = runs.Where(r => r.OverShots.HasValue && r.Kills is > 0).ToList();
        var kills = rs.Sum(r => r.Kills!.Value);
        if (kills == 0) return null;
        var perKill = (double)rs.Sum(r => r.OverShots!.Value) / kills;
        if (perKill >= 0.6) return new Advice("You fire {0:0.00} shots per kill at targets that are already dead, which points to **clicking in bursts** rather than once per target. Practise a calmer rhythm where every click is aimed at a live target.", [perKill]);
        return perKill >= 0.25 ? new Advice("You fire {0:0.00} shots per kill at targets that are **already dead**. Each of those shots costs time and lowers your accuracy, so try releasing the click the moment a target breaks.", [perKill]) : null;
    }

    /// <summary>The best runs kill clearly faster than the worst.</summary>
    static Advice? TtkGap(List<RunRecord> top, List<RunRecord> bottom)
    {
        if (Mean(top, r => r.AvgTtk) is not { } a || Mean(bottom, r => r.AvgTtk) is not { } b || b <= 0) return null;
        return (b - a) / b >= 0.1 ? new Advice("Your best runs kill a target in {0:0.00}s on average, your worst in {1:0.00}s. **Faster kills** are what set your good runs apart, so try to begin each target as soon as it appears.", [a, b]) : null;
    }

    /// <summary>Recent records the consistency gap is measured over.</summary>
    const int RecentRecords = 20;

    /// <summary>The recent average sits far below the best: the gap to close is consistency, not ability. Older scores are left out, so early practice does not widen the gap.</summary>
    static Advice? Consistency(ScenarioHistory s)
    {
        if (s.Records.Count < 10 || s.Best <= 0) return null;
        var gap = 1 - s.Records.TakeLast(RecentRecords).Average(r => r.Score) / s.Best;
        if (gap >= 0.25) return new Advice("Your recent average is {0:0}% below your best. A gap this wide means most runs fall apart somewhere, so **lower the pace** to one you can hold for the whole run and build speed back up from there.", [gap * 100]);
        return gap >= 0.12 ? new Advice("Your recent average is {0:0}% below your best. Since you have already reached that score, the gap is about **consistency** rather than ability, so focus on repeating your usual level instead of chasing new highs.", [gap * 100]) : null;
    }

    /// <summary>The latest five scores fell clearly below the five before.</summary>
    static Advice? Slump(ScenarioHistory s) => s.Trend(5) switch
    {
        { } t when t <= -0.12 => new Advice("Your last 5 runs average {0:0}% below the 5 before. A drop this large rarely recovers by playing on, so **stop this scenario for today**; practice while tired mostly trains tired habits.", [-t * 100]),
        { } t when t <= -0.05 => new Advice("Your last 5 runs average {0:0}% below the 5 before. Fatigue or lost focus often causes a dip like this, so take a **short break** or warm up on an easier scenario before continuing.", [-t * 100]),
        _ => null,
    };

    /// <summary>Many plays since the last personal best: the same drill has stopped moving the best.</summary>
    static Advice? Plateau(ScenarioHistory s)
    {
        var n = s.Records.Count;
        // A server best above every record has no known date, so the plays since it are unknown.
        if (n < MinPlateau || s.KnownBest > s.BestSoFar()[^1]) return null;
        var since = n - 1 - s.PersonalBestIndexes()[^1];
        return since >= 30 && since >= n / 4 ? new Advice("{0} plays since your last personal best. Repeating the same drill has stopped raising it, so spend a while on a **variant** of this scenario (smaller or faster targets, another sensitivity) and then come back.", [since]) : null;
    }

    /// <summary>The scenario's runs split wherever the gap between two of them exceeds the session gap: each part is one sitting, oldest first.</summary>
    static List<List<RunRecord>> Sittings(IReadOnlyList<RunRecord> runs)
    {
        var list = new List<List<RunRecord>>();
        foreach (var r in runs)
        {
            if (list.Count == 0 || !PlaySession.WithinGap(list[^1][^1].End, r.Start)) list.Add([]);
            list[^1].Add(r);
        }
        return list;
    }

    /// <summary>The first run of a sitting against the rest of that sitting.</summary>
    static Advice? ColdStart(List<List<RunRecord>> sittings)
    {
        var rel = sittings.Where(g => g.Count >= 3).Select(g => g[0].Score / g.Skip(1).Average(r => r.Score) - 1).ToList();
        if (rel.Count < MinGroup) return null;
        var m = rel.Average();
        return m <= -0.05 ? new Advice("Your first run of each sitting scores {0:0}% below the runs after it. The first attempt goes to getting used to the scenario again, so treat it as a **warm-up run** and judge yourself on the ones that follow.", [-m * 100]) : null;
    }

    /// <summary>Runs at the start of a sitting that count as fresh; the runs after them in a long sitting are compared with them.</summary>
    const int FreshRuns = 6;

    /// <summary>The runs past the first <see cref="FreshRuns"/> of a long sitting against the fresh ones (the first, a warm-up, left out).</summary>
    static Advice? LongStreak(List<List<RunRecord>> sittings)
    {
        var rel = sittings.Where(g => g.Count >= FreshRuns + 4).Select(g => g.Skip(FreshRuns).Average(r => r.Score) / g.Skip(1).Take(FreshRuns - 1).Average(r => r.Score) - 1).ToList();
        if (rel.Count < 3) return null;
        var m = rel.Average();
        return m <= -0.04 ? new Advice("After {0} runs in a row your scores drop {1:0}%. Long streaks on one scenario stop paying off, so **switch to another scenario** after about {0} runs and come back later.", [FreshRuns, -m * 100]) : null;
    }

    /// <summary>Mouse advice: the figures of the best third of plays against the worst third, for the figures that matter to the scenario's aim kind.</summary>
    static List<Advice> Mouse(string scenario, IReadOnlyList<(DateTime End, double Score, RunSpeed Speed)> speeds)
    {
        var ordered = ByForm(speeds.OrderBy(x => x.End).ToList(), x => x.Score).Select(x => x.Speed).ToList();
        var third = ordered.Count / 3;
        var top = ordered.Take(third).ToList();
        var bottom = ordered.TakeLast(third).ToList();
        var kind = MouseSpeedStats.KindFromName(scenario) ?? MouseSpeedStats.KindFromRuns(ordered);
        var tracking = kind == AimKind.Tracking;
        var list = new List<Advice>();

        // Means of a figure over the plays that have it; null unless both thirds have it.
        (double Top, double Bottom)? Pair(Func<RunSpeed, double?> get) =>
            top.Select(get).OfType<double>().ToList() is { Count: > 0 } a && bottom.Select(get).OfType<double>().ToList() is { Count: > 0 } b && b.Average() > 0 ? (a.Average(), b.Average()) : null;
        // Relative difference of the best plays from the worst: positive when the best are higher.
        static double Rel((double Top, double Bottom) p) => p.Top / p.Bottom - 1;

        if (Pair(r => tracking ? r.Mean : r.FlickPeak) is { } sp && Math.Abs(Rel(sp)) >= 0.1)
            list.Add(Rel(sp) > 0
                ? new Advice("Your best runs move the mouse {0:0}% faster than your worst. Hesitant, slow movements seem to cost you score here, so try committing to **faster movements**.", [Rel(sp) * 100])
                : new Advice("Your best runs move the mouse {0:0}% slower than your worst. Rushing seems to cost you control here, so **slow down** a little and aim before you move.", [-Rel(sp) * 100]));

        if (Pair(r => tracking ? r.SpeedCv : r.AccelStd) is { } sm && Rel(sm) <= -0.1)
            list.Add(new Advice("Your best runs have {0:0}% smoother mouse movement. Your good runs reach each target in **one clean motion**, so try not to stop and correct on the way.", [-Rel(sm) * 100]));

        if (tracking)
        {
            if (Pair(r => r.HoldRatio) is { } h && h.Top - h.Bottom >= 0.05)
                list.Add(new Advice("Your best runs hold the trigger {0:P0} of the time, against {1:P0} in your worst. Staying on target while firing keeps your score up, so when you lose the target briefly, **keep the click held** and re-acquire it instead of releasing.", [h.Top, h.Bottom]));
            if (Pair(r => r.ReversalsPerSec) is { } rv && Rel(rv) <= -0.15)
                list.Add(new Advice("Your best runs change direction {0:0}% less often. Frequent reversals usually mean **overcorrecting**, so try making smaller, calmer adjustments.", [-Rel(rv) * 100]));
        }
        else
        {
            if (Pair(r => r.StopToClick) is { } st && Rel(st) <= -0.15 && st.Bottom - st.Top >= 0.02)
                list.Add(new Advice("Your best runs click {0:0} ms sooner after the mouse stops. Waiting after you arrive costs time, so try **clicking as you land** on the target.", [(st.Bottom - st.Top) * 1000]));
            if (Pair(r => r.Efficiency) is { } ef && ef.Top - ef.Bottom >= 0.03)
                list.Add(new Advice("Your best runs take a straighter path to each target ({0:P0} direct, against {1:P0} in your worst). Curved or wandering paths waste time, so **aim in a straight line** at the target before you move.", [ef.Top, ef.Bottom]));
            if (Pair(r => r.Reaction) is { } re && Rel(re) <= -0.1)
                list.Add(new Advice("Your best runs react in {0:0.00}s, your worst in {1:0.00}s. Deciding on the next target **earlier** saves time, so look for it before you finish the current one.", [re.Top, re.Bottom]));
        }
        return list;
    }
}
