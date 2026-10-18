using KovaaksCompanion.Core.Trajectory;

namespace KovaaksCompanion.Core.Library;

/// <summary>
/// Advice drawn from the statistics of one scenario's own runs. Every rule compares the player with themselves and speaks only when a
/// permutation test says the gap is unlikely to be luck (<see cref="AdviceTests"/>); at most one piece per <see cref="AdviceArea"/> is shown.
/// Runs are ranked against the player's form at the time (<see cref="FormRel{T}"/>), not by raw score: raw scores rise with practice, so the
/// best third would otherwise be the latest runs and every habit that changed over time would look like what makes a good run.
/// Only process figures (how the player aimed) are compared between good and bad runs. Figures the score is made of (kills, TTK, tracking
/// accuracy, time on the trigger, flick speed) always differ between them, so a gap there says nothing; speed against accuracy is judged
/// once, as the accuracy band the scores peak in (<see cref="Pace"/>).
/// </summary>
public static class ScenarioAdvice
{
    /// <summary>Runs needed before the best and worst thirds are compared.</summary>
    const int MinRuns = 12;
    /// <summary>Runs a sensitivity or hour block needs to count.</summary>
    const int MinGroup = 4;
    /// <summary>Records needed before a plateau is judged.</summary>
    const int MinPlateau = 40;
    /// <summary>Gap between the thirds, in standard deviations of the figure across all runs, below which a process figure is noise.</summary>
    const double MinEffectSize = 0.5;

    /// <param name="speeds">Mouse-speed figures of the runs that have them, with each run's end and score.</param>
    public static List<Advice> Build(ScenarioHistory s, IReadOnlyList<(DateTime End, double Score, RunSpeed Speed)>? speeds = null, int max = 4)
    {
        var runs = s.Runs;
        var t = new AdviceTests();
        var list = new List<Advice?>();
        var tracking = (MouseSpeedStats.KindFromName(s.Scenario) ?? (speeds is { Count: > 0 } ? MouseSpeedStats.KindFromRuns(speeds.Select(x => x.Speed).ToList()) : null)) == AimKind.Tracking;

        list.Add(Slump(s, t));
        list.Add(Plateau(s));
        list.Add(Sensitivity(runs, t));
        var sittings = Sittings(runs);
        list.Add(ColdStart(sittings, t));
        list.Add(LongStreak(sittings, t));
        if (!tracking)
        {
            list.Add(Pace(runs, t));
            list.Add(Overshooting(runs, t));
        }
        if (speeds != null) list.AddRange(Mouse(speeds.OrderBy(x => x.End).ToList(), tracking, t));
        return t.Pick(list, max);
    }

    /// <summary>Runs on each side whose mean is a run's form.</summary>
    const int FormWindow = 5;

    /// <summary>Each run's score (oldest first) against the player's form at the time, the mean of the <see cref="FormWindow"/> runs on each side of it: 1 is on form.</summary>
    static double[] FormRel<T>(IReadOnlyList<T> chronological, Func<T, double> score)
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
        return rel;
    }

    /// <summary>
    /// A process figure of the runs that have it, the best third (against form) against the worst third. Speaks when the best third is on the
    /// good side by at least <see cref="MinEffectSize"/> and by <paramref name="big"/>, and the split is unlikely to be luck. The expected effect
    /// is the score change that moving the figure one standard deviation the good way went with across all the runs (correlation times spread).
    /// </summary>
    static Advice? Process<T>(IReadOnlyList<T> chronological, Func<T, double> score, Func<T, double?> get, bool lowerIsBetter,
        Func<double, double, bool> big, Func<double, double, Advice> text, AdviceTests t)
    {
        var rel = FormRel(chronological, score);
        var pts = chronological.Select((x, i) => (Rel: rel[i], V: get(x))).Where(x => x.V.HasValue).Select(x => (x.Rel, V: x.V!.Value)).ToList();
        if (pts.Count < MinRuns) return null;
        double Good(double v) => lowerIsBetter ? -v : v;
        var ordered = pts.OrderByDescending(x => x.Rel).ToList();
        var third = ordered.Count / 3;
        var top = ordered.Take(third).Select(x => x.V).ToList();
        var bottom = ordered.TakeLast(third).Select(x => x.V).ToList();
        var p = t.SplitP(top.Select(Good).ToList(), bottom.Select(Good).ToList());
        var (a, b) = (top.Average(), bottom.Average());
        var sd = AdviceTests.Std(pts.Select(x => x.V).ToList());
        if (sd <= 0 || (Good(a) - Good(b)) / sd < MinEffectSize || !big(a, b)) return null;
        var r = AdviceTests.Correlation(pts.Select(x => Good(x.V)).ToList(), pts.Select(x => x.Rel).ToList());
        if (r <= 0) return null;
        return text(a, b) with { Area = AdviceArea.Movement, Effect = r * AdviceTests.Std(pts.Select(x => x.Rel).ToList()), P = p };
    }

    /// <summary>Runs needed before the speed-accuracy curve is read.</summary>
    const int MinPace = 30;
    /// <summary>Latest runs whose accuracy is set against the best band.</summary>
    const int RecentPace = 10;

    /// <summary>
    /// The speed-accuracy balance of a click scenario: runs sorted by accuracy, the band of neighbouring accuracies (a fifth of the runs) whose
    /// scores stand furthest above form, against where the latest runs sit. Silent when the curve is flat, when the band could be luck, or when
    /// the latest runs are already in it.
    /// </summary>
    static Advice? Pace(IReadOnlyList<RunRecord> runs, AdviceTests t)
    {
        var rel = FormRel(runs, r => r.Score);
        var pts = runs.Select((r, i) => (Rel: rel[i], Acc: r.Accuracy)).Where(x => x.Acc.HasValue).Select(x => (x.Rel, Acc: x.Acc!.Value)).ToList();
        if (pts.Count < MinPace) return null;
        var recent = pts.TakeLast(RecentPace).Average(x => x.Acc);
        var sorted = pts.OrderBy(x => x.Acc).ToList();
        var width = Math.Max(MinGroup, sorted.Count / 5);
        var values = sorted.Select(x => x.Rel).ToList();
        var (start, gap) = AdviceTests.BestWindow(values, width);
        if (gap < 0.03) return null;
        var (lo, hi) = (sorted[start].Acc, sorted[start + width - 1].Acc);
        // Half a percent of slack, so a figure that rounds into the band is not told to leave it.
        if (recent >= lo - 0.005 && recent <= hi + 0.005) return null;
        var p = t.BestWindowP(values, width, gap);
        var a = recent < lo
            ? new Advice("Your scores are highest when you hit {0:P0}–{1:P0} of your shots. Your last 10 runs average {2:P0}, so take **a little more care** with each shot.", [lo, hi, recent])
            : new Advice("Your scores are highest when you hit {0:P0}–{1:P0} of your shots. Your last 10 runs average {2:P0}, so **push the pace** and take shots sooner, even if a few more miss.", [lo, hi, recent]);
        return a with { Area = AdviceArea.Pace, Effect = gap, P = p };
    }

    /// <summary>
    /// The sensitivity with the best average score, when it beats the most played one by at least 3%. The most played one only counts its runs
    /// from the time the other was played, so a sensitivity tried later, after more practice, does not win on practice alone.
    /// </summary>
    static Advice? Sensitivity(IReadOnlyList<RunRecord> runs, AdviceTests t)
    {
        var groups = runs.Where(r => r.Cm360 is > 0).GroupBy(r => Math.Round(r.Cm360!.Value, 2)).Where(g => g.Count() >= MinGroup).ToList();
        if (groups.Count < 2) return null;
        var main = groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        var best = groups.OrderByDescending(g => g.Average(r => r.Score)).First();
        if (best.Key == main.Key) return null;
        var (from, to) = (best.Min(r => r.Start), best.Max(r => r.End));
        var same = main.Where(r => r.End >= from && r.Start <= to).ToList();
        if (same.Count < MinGroup) return null;
        var p = t.SplitP(best.Select(r => r.Score).ToList(), same.Select(r => r.Score).ToList());
        var gain = best.Average(r => r.Score) / same.Average(r => r.Score) - 1;
        if (gain < 0.03) return null;
        // More cm per 360 is a lower sensitivity: it trades reach for fine control.
        var a = best.Key > main.Key
            ? new Advice("Your average is {0:0}% higher at {1} cm/360 ({2} runs) than at your usual {3} cm/360. This scenario rewards **fine control** more than reach, so try playing it at the lower sensitivity more often.", [gain * 100, best.Key, best.Count(), main.Key])
            : new Advice("Your average is {0:0}% higher at {1} cm/360 ({2} runs) than at your usual {3} cm/360. This scenario rewards **reaching targets quickly** more than fine control, so try playing it at the higher sensitivity more often.", [gain * 100, best.Key, best.Count(), main.Key]);
        return a with { Area = AdviceArea.Practice, Effect = gain, P = p };
    }

    /// <summary>Shots per kill at targets that were already dead, the best runs against the worst.</summary>
    static Advice? Overshooting(IReadOnlyList<RunRecord> runs, AdviceTests t) =>
        Process(runs, r => r.Score, r => r.OverShots.HasValue && r.Kills is > 0 ? (double)r.OverShots.Value / r.Kills.Value : null, true,
            (a, b) => b - a >= 0.1 && a <= b * 0.85,
            (a, b) => new Advice("Your best runs fire {0:0.00} shots per kill at targets that are already dead, your worst {1:0.00}. Those extra shots cost time, so **release the click** the moment a target breaks.", [a, b]), t);

    /// <summary>The latest five scores fell clearly below the five before.</summary>
    static Advice? Slump(ScenarioHistory s, AdviceTests t)
    {
        if (s.Trend(5) is not { } trend) return null;
        var last = s.Records.TakeLast(10).Select(r => r.Score).ToList();
        var p = t.SplitP(last.Take(5).ToList(), last.Skip(5).ToList());
        var a = trend switch
        {
            <= -0.12 => new Advice("Your last 5 runs average {0:0}% below the 5 before. A drop this large rarely recovers by playing on, so **stop this scenario for today**; practice while tired mostly trains tired habits.", [-trend * 100]),
            <= -0.05 => new Advice("Your last 5 runs average {0:0}% below the 5 before. Fatigue or lost focus often causes a dip like this, so take a **short break** or warm up on an easier scenario before continuing.", [-trend * 100]),
            _ => null,
        };
        return a is null ? null : a with { Area = AdviceArea.State, Effect = -trend, P = p };
    }

    /// <summary>Many plays since the last personal best: the same drill has stopped moving the best.</summary>
    static Advice? Plateau(ScenarioHistory s)
    {
        var n = s.Records.Count;
        // A server best above every record has no known date, so the plays since it are unknown.
        if (n < MinPlateau || s.KnownBest > s.BestSoFar()[^1]) return null;
        var since = n - 1 - s.PersonalBestIndexes()[^1];
        return since >= 30 && since >= n / 4
            ? new Advice("{0} plays since your last personal best. Repeating the same drill has stopped raising it, so spend a while on a **variant** of this scenario (smaller or faster targets, another sensitivity) and then come back.", [since]) { Area = AdviceArea.Practice, Effect = AdviceTests.NominalEffect }
            : null;
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
    static Advice? ColdStart(List<List<RunRecord>> sittings, AdviceTests t)
    {
        var rel = sittings.Where(g => g.Count >= 3).Select(g => g[0].Score / g.Skip(1).Average(r => r.Score) - 1).ToList();
        if (rel.Count < MinGroup) return null;
        var p = t.SignFlipP(rel);
        var m = rel.Average();
        return m <= -0.05
            ? new Advice("Your first run of each sitting scores {0:0}% below the runs after it. The first attempt goes to getting used to the scenario again, so treat it as a **warm-up run** and judge yourself on the ones that follow.", [-m * 100]) { Area = AdviceArea.Practice, Effect = -m, P = p }
            : null;
    }

    /// <summary>Runs at the start of a sitting that count as fresh; the runs after them in a long sitting are compared with them.</summary>
    const int FreshRuns = 6;

    /// <summary>The runs past the first <see cref="FreshRuns"/> of a long sitting against the fresh ones (the first, a warm-up, left out).</summary>
    static Advice? LongStreak(List<List<RunRecord>> sittings, AdviceTests t)
    {
        var rel = sittings.Where(g => g.Count >= FreshRuns + 4).Select(g => g.Skip(FreshRuns).Average(r => r.Score) / g.Skip(1).Take(FreshRuns - 1).Average(r => r.Score) - 1).ToList();
        if (rel.Count < 3) return null;
        var p = t.SignFlipP(rel);
        var m = rel.Average();
        return m <= -0.04
            ? new Advice("After {0} runs in a row your scores drop {1:0}%. Long streaks on one scenario stop paying off, so **switch to another scenario** after about {0} runs and come back later.", [FreshRuns, -m * 100]) { Area = AdviceArea.State, Effect = -m, P = p }
            : null;
    }

    /// <summary>Mouse advice: the process figures that matter to the scenario's aim kind, the best third of plays against the worst third.</summary>
    static IEnumerable<Advice?> Mouse(List<(DateTime End, double Score, RunSpeed Speed)> speeds, bool tracking, AdviceTests t)
    {
        static double Rel(double a, double b) => b > 0 ? a / b - 1 : 0;
        Advice? Fig(Func<RunSpeed, double?> get, bool lowerIsBetter, Func<double, double, bool> big, Func<double, double, Advice> text) =>
            Process(speeds, x => x.Score, x => get(x.Speed), lowerIsBetter, big, text, t);

        yield return Fig(r => tracking ? r.SpeedCv : r.AccelStd, true, (a, b) => Rel(a, b) <= -0.1,
            (a, b) => new Advice("Your best runs have {0:0}% smoother mouse movement. Your good runs reach each target in **one clean motion**, so try not to stop and correct on the way.", [-Rel(a, b) * 100]));
        if (tracking)
        {
            yield return Fig(r => r.ReversalsPerSec, true, (a, b) => Rel(a, b) <= -0.15,
                (a, b) => new Advice("Your best runs change direction {0:0}% less often. Frequent reversals usually mean **overcorrecting**, so try making smaller, calmer adjustments.", [-Rel(a, b) * 100]));
            yield break;
        }
        yield return Fig(r => r.StopToClick, true, (a, b) => Rel(a, b) <= -0.15 && b - a >= 0.02,
            (a, b) => new Advice("Your best runs click {0:0} ms sooner after the mouse stops. Waiting after you arrive costs time, so try **clicking as you land** on the target.", [(b - a) * 1000]));
        yield return Fig(r => r.Efficiency, false, (a, b) => a - b >= 0.03,
            (a, b) => new Advice("Your best runs take a straighter path to each target ({0:P0} direct, against {1:P0} in your worst). Curved or wandering paths waste time, so **aim in a straight line** at the target before you move.", [a, b]));
        yield return Fig(r => r.Reaction, true, (a, b) => Rel(a, b) <= -0.1,
            (a, b) => new Advice("Your best runs react in {0:0.00}s, your worst in {1:0.00}s. Deciding on the next target **earlier** saves time, so look for it before you finish the current one.", [a, b]));
    }
}
