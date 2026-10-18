namespace KovaaksCompanion.Core.Library;

/// <summary>
/// Advice drawn from one session's runs against the player's own history. Scores of different scenarios are never averaged together:
/// each run is first set against the average of the same scenario's other runs, and only those relative figures are combined.
/// </summary>
public static class SessionAdvice
{
    /// <summary>Other runs of a scenario needed before it has a baseline.</summary>
    const int MinBaseline = 5;
    /// <summary>Compared runs needed before the whole session, or its halves, are judged.</summary>
    const int MinCompared = 3;
    const int MinHalves = 8;

    /// <summary>Runs a session needs before its spread over scenarios is judged.</summary>
    const int MinSpread = 8;

    public static List<Advice> Build(IReadOnlyList<RunRecord> session, IReadOnlyList<RunRecord> all, int max = 7)
    {
        var list = new List<Advice>();
        void Add(Advice? a) { if (a != null) list.Add(a); }

        var rel = Relative(session, all);
        Add(Overall(rel));
        Add(ByScenario(rel));
        Add(Halves(rel));
        Add(Accuracy(session, all, rel.Count >= MinCompared ? rel.Average(x => x.Rel) : null));
        Add(Overshooting(session, all));
        Add(Spread(session));
        Add(Streak(session));
        return list.Take(max).ToList();
    }

    /// <summary>Each session run's score against the mean of the other runs of its scenario (0.05 = 5% above), in play order; runs of a scenario without a baseline are left out.</summary>
    static List<(RunRecord Run, double Rel)> Relative(IReadOnlyList<RunRecord> session, IReadOnlyList<RunRecord> all)
    {
        var inSession = new HashSet<RunRecord>(session, ReferenceEqualityComparer.Instance);
        var baseline = all.Where(r => !inSession.Contains(r)).GroupBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinBaseline).ToDictionary(g => g.Key, g => g.Average(r => r.Score), StringComparer.OrdinalIgnoreCase);
        return session.OrderBy(r => r.End).Where(r => baseline.TryGetValue(r.Scenario, out var b) && b > 0)
            .Select(r => (r, r.Score / baseline[r.Scenario] - 1)).ToList();
    }

    /// <summary>The session as a whole against the player's usual scores.</summary>
    static Advice? Overall(List<(RunRecord Run, double Rel)> rel)
    {
        if (rel.Count < MinCompared) return null;
        var m = rel.Average(x => x.Rel);
        if (m >= 0.03) return new Advice("Your scores this session are {0:0}% above your usual average. Whatever you did differently today is **worth repeating**.", [m * 100]);
        if (m <= -0.03) return new Advice("Your scores this session are {0:0}% below your usual average. Tired or unfocused days happen, so consider ending on an **easier scenario** or taking a break.", [-m * 100]);
        return null;
    }

    /// <summary>The scenario that fell furthest below the player's usual, or else the one that rose furthest above it (at least two runs each).</summary>
    static Advice? ByScenario(List<(RunRecord Run, double Rel)> rel)
    {
        var lines = rel.GroupBy(x => x.Run.Scenario, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2)
            .Select(g => (Name: g.Key, Rel: g.Average(x => x.Rel))).ToList();
        if (lines.Count == 0) return null;
        var worst = lines.MinBy(l => l.Rel);
        if (worst.Rel <= -0.05) return new Advice("{0} is {1:0}% below your usual average this session. It is the scenario holding the session back, so give it a **few extra warm-up runs**.", [worst.Name, -worst.Rel * 100]);
        var best = lines.MaxBy(l => l.Rel);
        return best.Rel >= 0.05 ? new Advice("{0} is {1:0}% above your usual average this session. You are in rhythm on it, so this is a good time to **push for a personal best**.", [best.Name, best.Rel * 100]) : null;
    }

    /// <summary>The second half of the session against the first, once the session is long enough to split.</summary>
    static Advice? Halves(List<(RunRecord Run, double Rel)> rel)
    {
        if (rel.Count < MinHalves) return null;
        var half = rel.Count / 2;
        var diff = rel.Skip(rel.Count - half).Average(x => x.Rel) - rel.Take(half).Average(x => x.Rel);
        if (diff <= -0.04) return new Advice("The second half of your session scored {0:0}% lower than the first. Focus tends to fade after a while, so try a **short break** halfway or end the session sooner.", [-diff * 100]);
        if (diff >= 0.04) return new Advice("The first half of your session scored {0:0}% lower than the second. You seem to need time to settle in, so **warm up** on an easier scenario before the ones that count.", [diff * 100]);
        return null;
    }

    /// <summary>Accuracy this session against all time, both weighted by shots; read against the session's scores (<paramref name="score"/>, its mean relative score), since speed and accuracy trade off.</summary>
    static Advice? Accuracy(IReadOnlyList<RunRecord> session, IReadOnlyList<RunRecord> all, double? score)
    {
        var none = new HashSet<RunRecord>();
        if (RunSetInsights.Summarize(session, none)?.Accuracy is not { } a || RunSetInsights.Summarize(all, none)?.Accuracy is not { } b) return null;
        if (a - b <= -0.02 && score >= 0.02) return new Advice("Your accuracy this session is {0:P1}, below your usual {1:P1}, yet your scores are up. Trading a little precision for **speed** is paying off, so keep the pace as long as accuracy does not slip further.", [a, b]);
        if (a - b >= 0.02 && score <= -0.02) return new Advice("Your accuracy this session is {0:P1}, above your usual {1:P1}, yet your scores are down. You are playing **too safe**, so let yourself take faster shots even if a few more miss.", [a, b]);
        if (a - b <= -0.02) return new Advice("Your accuracy this session is {0:P1}, below your usual {1:P1}. You are probably **rushing**, so ease off the speed until the hits come back.", [a, b]);
        if (a - b >= 0.02) return new Advice("Your accuracy this session is {0:P1}, above your usual {1:P1}. Clean hits are paying off, so you can start **pushing speed** a little.", [a, b]);
        return null;
    }

    /// <summary>Shots at dead targets per kill this session against all time.</summary>
    static Advice? Overshooting(IReadOnlyList<RunRecord> session, IReadOnlyList<RunRecord> all)
    {
        var none = new HashSet<RunRecord>();
        if (RunSetInsights.Summarize(session, none)?.OverShots is not { } a || RunSetInsights.Summarize(all, none)?.OverShots is not { } b) return null;
        return a >= 0.2 && a - b >= 0.1 ? new Advice("You fired {0:0.00} shots per kill at targets already dead this session, against {1:0.00} usually. Tiredness or impatience often causes this, so **release the click** the moment a target breaks.", [a, b]) : null;
    }

    /// <summary>A session spread so thin that most scenarios got a single run.</summary>
    static Advice? Spread(IReadOnlyList<RunRecord> session)
    {
        if (session.Count < MinSpread) return null;
        var counts = session.GroupBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase).Select(g => g.Count()).ToList();
        var once = counts.Count(c => c == 1);
        return counts.Count >= 4 && once >= counts.Count * 0.6 ? new Advice("{0} of the {1} scenarios this session got only one run. A single attempt mostly goes to getting used to the scenario, so play **fewer scenarios a few runs each**.", [once, counts.Count]) : null;
    }

    /// <summary>Runs of one scenario in a row that count as fresh; a longer streak's later runs are compared with them.</summary>
    const int FreshRuns = 6;

    /// <summary>The longest streak of one scenario played back to back this session, when its runs past the fresh ones fell below them.</summary>
    static Advice? Streak(IReadOnlyList<RunRecord> session)
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
        var drop = longest.Skip(FreshRuns).Average(r => r.Score) / longest.Take(FreshRuns).Average(r => r.Score) - 1;
        return drop <= -0.04 ? new Advice("You played {0} {1} times in a row, and the runs after the first {2} scored {3:0}% lower. Returns fade on a long streak, so **move on** to another scenario sooner and come back later.", [longest[0].Scenario, longest.Count, FreshRuns, -drop * 100]) : null;
    }
}
