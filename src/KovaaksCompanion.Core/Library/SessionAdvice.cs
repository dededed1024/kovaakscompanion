namespace KovaaksCompanion.Core.Library;

/// <summary>
/// Advice drawn from one session's runs against the player's recent form. Scores of different scenarios are never averaged together:
/// each run is first set against the same scenario's last <see cref="BaselineRuns"/> runs before the session, and only those relative figures
/// are combined. The baseline is recent rather than all time, so a player who is improving is not told every session that they beat their
/// average. Rules speak only when a permutation test says the gap is unlikely to be luck (<see cref="AdviceTests"/>).
/// </summary>
public static class SessionAdvice
{
    /// <summary>Runs of a scenario before the session that make its baseline.</summary>
    const int BaselineRuns = 20;
    /// <summary>Runs before the session a scenario needs to have a baseline.</summary>
    const int MinBaseline = 5;
    /// <summary>Compared runs needed before the whole session, or its halves, are judged.</summary>
    const int MinCompared = 3;
    const int MinHalves = 8;

    /// <summary>Runs a session needs before its spread over scenarios is judged.</summary>
    const int MinSpread = 8;

    public static List<Advice> Build(IReadOnlyList<RunRecord> session, IReadOnlyList<RunRecord> all, int max = 4)
    {
        var t = new AdviceTests();
        var baseline = Baseline(session, all);
        var rel = Relative(session, baseline);
        return t.Pick([
            Overall(rel, t),
            ByScenario(rel, t),
            Halves(rel, t),
            Overshooting(session, baseline, t),
            Spread(session),
            Streak(session, t),
        ], max);
    }

    /// <summary>For each scenario of the session, its last <see cref="BaselineRuns"/> runs that ended before the session began; scenarios with too few are left out.</summary>
    static Dictionary<string, List<RunRecord>> Baseline(IReadOnlyList<RunRecord> session, IReadOnlyList<RunRecord> all)
    {
        if (session.Count == 0) return [];
        var start = session.Min(r => r.Start);
        var names = session.Select(r => r.Scenario).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(r => r.End < start && names.Contains(r.Scenario)).GroupBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Runs: g.OrderBy(r => r.End).TakeLast(BaselineRuns).ToList())).Where(x => x.Runs.Count >= MinBaseline)
            .ToDictionary(x => x.Key, x => x.Runs, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Each session run's score against its scenario's baseline mean (0.05 = 5% above), in play order; runs of a scenario without a baseline are left out.</summary>
    static List<(RunRecord Run, double Rel)> Relative(IReadOnlyList<RunRecord> session, Dictionary<string, List<RunRecord>> baseline)
    {
        var means = baseline.ToDictionary(kv => kv.Key, kv => kv.Value.Average(r => r.Score), StringComparer.OrdinalIgnoreCase);
        return session.OrderBy(r => r.End).Where(r => means.TryGetValue(r.Scenario, out var b) && b > 0)
            .Select(r => (r, r.Score / means[r.Scenario] - 1)).ToList();
    }

    /// <summary>The session as a whole against the player's recent scores.</summary>
    static Advice? Overall(List<(RunRecord Run, double Rel)> rel, AdviceTests t)
    {
        if (rel.Count < MinCompared) return null;
        var p = t.SignFlipP(rel.Select(x => x.Rel).ToList());
        var m = rel.Average(x => x.Rel);
        var a = m switch
        {
            >= 0.03 => new Advice("Your scores this session are {0:0}% above your recent average. Whatever you did differently today is **worth repeating**.", [m * 100]),
            <= -0.03 => new Advice("Your scores this session are {0:0}% below your recent average. Tired or unfocused days happen, so consider ending on an **easier scenario** or taking a break.", [-m * 100]),
            _ => null,
        };
        return a is null ? null : a with { Area = AdviceArea.State, Effect = Math.Abs(m), P = p };
    }

    /// <summary>The scenario that fell furthest below the player's recent scores, or else the one that rose furthest above them (at least three runs each).</summary>
    static Advice? ByScenario(List<(RunRecord Run, double Rel)> rel, AdviceTests t)
    {
        var lines = rel.GroupBy(x => x.Run.Scenario, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= MinCompared)
            .Select(g => (Name: g.Key, Rels: g.Select(x => x.Rel).ToList())).ToList();
        if (lines.Count == 0) return null;
        // Every scenario is tested, since the one reported is picked from among them.
        var ps = lines.Select(l => t.SignFlipP(l.Rels)).ToList();
        var worst = Enumerable.Range(0, lines.Count).MinBy(i => lines[i].Rels.Average());
        var w = lines[worst].Rels.Average();
        if (w <= -0.05) return new Advice("{0} is {1:0}% below your recent average this session. It is the scenario holding the session back, so give it a **few extra warm-up runs**.", [lines[worst].Name, -w * 100]) { Area = AdviceArea.State, Effect = -w, P = ps[worst] };
        var best = Enumerable.Range(0, lines.Count).MaxBy(i => lines[i].Rels.Average());
        var b = lines[best].Rels.Average();
        return b >= 0.05 ? new Advice("{0} is {1:0}% above your recent average this session. You are in rhythm on it, so this is a good time to **push for a personal best**.", [lines[best].Name, b * 100]) { Area = AdviceArea.State, Effect = b, P = ps[best] } : null;
    }

    /// <summary>The second half of the session against the first, once the session is long enough to split.</summary>
    static Advice? Halves(List<(RunRecord Run, double Rel)> rel, AdviceTests t)
    {
        if (rel.Count < MinHalves) return null;
        var half = rel.Count / 2;
        var first = rel.Take(half).Select(x => x.Rel).ToList();
        var second = rel.Skip(rel.Count - half).Select(x => x.Rel).ToList();
        var p = t.SplitP(second, first, twoSided: true);
        var diff = second.Average() - first.Average();
        var a = diff switch
        {
            <= -0.04 => new Advice("The second half of your session scored {0:0}% lower than the first. Focus tends to fade after a while, so try a **short break** halfway or end the session sooner.", [-diff * 100]),
            >= 0.04 => new Advice("The first half of your session scored {0:0}% lower than the second. You seem to need time to settle in, so **warm up** on an easier scenario before the ones that count.", [diff * 100]),
            _ => null,
        };
        return a is null ? null : a with { Area = AdviceArea.State, Effect = Math.Abs(diff), P = p };
    }

    /// <summary>Shots at dead targets per kill this session against the same scenarios' baseline runs.</summary>
    static Advice? Overshooting(IReadOnlyList<RunRecord> session, Dictionary<string, List<RunRecord>> baseline, AdviceTests t)
    {
        static List<double> PerKill(IEnumerable<RunRecord> runs) =>
            runs.Where(r => r.OverShots.HasValue && r.Kills is > 0).Select(r => (double)r.OverShots!.Value / r.Kills!.Value).ToList();
        var nowRuns = session.Where(r => baseline.ContainsKey(r.Scenario)).ToList();
        var beforeRuns = baseline.Values.SelectMany(r => r).ToList();
        var (now, before) = (PerKill(nowRuns), PerKill(beforeRuns));
        if (now.Count < MinCompared || before.Count < MinBaseline) return null;
        // The figures shown are weighted by kills, as everywhere else; the test runs on the runs' own figures.
        var none = new HashSet<RunRecord>();
        if (RunSetInsights.Summarize(nowRuns, none)?.OverShots is not { } a || RunSetInsights.Summarize(beforeRuns, none)?.OverShots is not { } b) return null;
        var p = t.SplitP(now, before);
        return a >= 0.2 && a - b >= 0.1 && a >= b * 1.15
            ? new Advice("You fired {0:0.00} shots per kill at targets already dead this session, against {1:0.00} recently. Tiredness or impatience often causes this, so **release the click** the moment a target breaks.", [a, b]) { Area = AdviceArea.Movement, Effect = AdviceTests.NominalEffect, P = p }
            : null;
    }

    /// <summary>A session spread so thin that most scenarios got a single run.</summary>
    static Advice? Spread(IReadOnlyList<RunRecord> session)
    {
        if (session.Count < MinSpread) return null;
        var counts = session.GroupBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase).Select(g => g.Count()).ToList();
        var once = counts.Count(c => c == 1);
        return counts.Count >= 4 && once >= counts.Count * 0.6
            ? new Advice("{0} of the {1} scenarios this session got only one run. A single attempt mostly goes to getting used to the scenario, so play **fewer scenarios a few runs each**.", [once, counts.Count]) { Area = AdviceArea.Practice, Effect = AdviceTests.NominalEffect }
            : null;
    }

    /// <summary>Runs of one scenario in a row that count as fresh; a longer streak's later runs are compared with them.</summary>
    const int FreshRuns = 6;

    /// <summary>The longest streak of one scenario played back to back this session, when its runs past the fresh ones fell below them.</summary>
    static Advice? Streak(IReadOnlyList<RunRecord> session, AdviceTests t)
    {
        var streaks = new List<List<RunRecord>>();
        foreach (var r in session.OrderBy(r => r.End))
        {
            if (streaks.Count == 0 || !string.Equals(streaks[^1][0].Scenario, r.Scenario, StringComparison.OrdinalIgnoreCase)) streaks.Add([]);
            streaks[^1].Add(r);
        }
        var longest = streaks.MaxBy(s => s.Count);
        if (longest is not { Count: >= FreshRuns + 3 }) return null;
        // One scenario, so raw scores compare directly; a scenario without a baseline still counts.
        var fresh = longest.Take(FreshRuns).Select(r => r.Score).ToList();
        var later = longest.Skip(FreshRuns).Select(r => r.Score).ToList();
        var p = t.SplitP(fresh, later);
        var drop = later.Average() / fresh.Average() - 1;
        return drop <= -0.04
            ? new Advice("You played {0} {1} times in a row, and the runs after the first {2} scored {3:0}% lower. Returns fade on a long streak, so **move on** to another scenario sooner and come back later.", [longest[0].Scenario, longest.Count, FreshRuns, -drop * 100]) { Area = AdviceArea.State, Effect = -drop, P = p }
            : null;
    }
}
