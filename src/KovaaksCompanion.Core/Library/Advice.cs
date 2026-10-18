namespace KovaaksCompanion.Core.Library;

/// <summary>What a piece of advice is about. At most one piece per area is shown, so two rules judging the same thing never both speak.</summary>
public enum AdviceArea
{
    /// <summary>The player right now: slumps, fatigue, long streaks.</summary>
    State,
    /// <summary>The balance of speed and accuracy.</summary>
    Pace,
    /// <summary>How the mouse moves: the process figures, never the ones the score is made of.</summary>
    Movement,
    /// <summary>How practice is laid out: warm-ups, plateaus, sensitivity, spread over scenarios.</summary>
    Practice,
}

/// <summary>
/// One piece of advice: a format string (also the localization key) and the figures it is filled with. <see cref="Effect"/> is the score
/// change (a fraction, 0.05 = 5%) following it is expected to bring; <see cref="P"/> the chance the pattern behind it is luck, null for rules
/// that count rather than compare.
/// </summary>
public sealed record Advice(string Format, object[] Args)
{
    public AdviceArea Area { get; init; }
    public double Effect { get; init; }
    public double? P { get; init; }
}

/// <summary>
/// The statistics the advice rules share: permutation tests that tell a real gap from luck, and the final pick. Every test run is counted,
/// whether its rule speaks or not, so the correction for the number of tests (Benjamini–Hochberg) covers all of them.
/// </summary>
public sealed class AdviceTests
{
    /// <summary>Random reshuffles per test.</summary>
    const int Permutations = 1000;
    /// <summary>Share of the advice shown that may be luck (the false discovery rate).</summary>
    const double FalseDiscovery = 0.1;
    /// <summary>Effect of rules that count something rather than measure a score change: enough to show when nothing else in the area speaks.</summary>
    public const double NominalEffect = 0.02;

    readonly Random _rng = new(7919);

    /// <summary>Tests run so far.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// The chance that splitting the values at random gives a gap between the means of <paramref name="a"/> and <paramref name="b"/>
    /// at least as large as the real one: one-sided (a above b) unless <paramref name="twoSided"/>.
    /// </summary>
    public double SplitP(IReadOnlyList<double> a, IReadOnlyList<double> b, bool twoSided = false)
    {
        Count++;
        var all = a.Concat(b).ToArray();
        var (n, k) = (all.Length, a.Count);
        if (k == 0 || k == n) return 1;
        var total = all.Sum();
        double Gap(double sumA) => sumA / k - (total - sumA) / (n - k);
        var observed = Gap(a.Sum());
        if (twoSided) observed = Math.Abs(observed);
        var hits = 0;
        for (var p = 0; p < Permutations; p++)
        {
            // A partial Fisher-Yates shuffle: the first k slots are a random draw for a.
            double sum = 0;
            for (var i = 0; i < k; i++)
            {
                var j = _rng.Next(i, n);
                (all[i], all[j]) = (all[j], all[i]);
                sum += all[i];
            }
            var g = Gap(sum);
            if ((twoSided ? Math.Abs(g) : g) >= observed - 1e-12) hits++;
        }
        return (hits + 1.0) / (Permutations + 1);
    }

    /// <summary>The chance that values whose signs are flipped at random have a mean at least as far from zero as the real one (two-sided).</summary>
    public double SignFlipP(IReadOnlyList<double> values)
    {
        Count++;
        if (values.Count == 0) return 1;
        var observed = Math.Abs(values.Average());
        var hits = 0;
        for (var p = 0; p < Permutations; p++)
        {
            double sum = 0;
            foreach (var v in values) sum += _rng.Next(2) == 0 ? v : -v;
            if (Math.Abs(sum / values.Count) >= observed - 1e-12) hits++;
        }
        return (hits + 1.0) / (Permutations + 1);
    }

    /// <summary>
    /// The chance that some window of <paramref name="width"/> neighbouring points stands at least <paramref name="observed"/> above the rest
    /// when the values are shuffled over the points: the best window is searched again on every shuffle, so picking it is paid for.
    /// </summary>
    public double BestWindowP(IReadOnlyList<double> values, int width, double observed)
    {
        Count++;
        var v = values.ToArray();
        var hits = 0;
        for (var p = 0; p < Permutations; p++)
        {
            for (var i = v.Length - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (v[i], v[j]) = (v[j], v[i]);
            }
            if (BestWindow(v, width).Gap >= observed - 1e-12) hits++;
        }
        return (hits + 1.0) / (Permutations + 1);
    }

    /// <summary>The window of <paramref name="width"/> neighbouring values whose mean stands furthest above the mean of the rest.</summary>
    public static (int Start, double Gap) BestWindow(IReadOnlyList<double> v, int width)
    {
        var n = v.Count;
        var total = v.Sum();
        double sum = 0;
        for (var i = 0; i < width; i++) sum += v[i];
        var best = (Start: 0, Gap: double.NegativeInfinity);
        for (var s = 0; ; s++)
        {
            var gap = sum / width - (total - sum) / (n - width);
            if (gap > best.Gap) best = (s, gap);
            if (s + width >= n) break;
            sum += v[s + width] - v[s];
        }
        return best;
    }

    /// <summary>
    /// The advice to show: candidates whose p survives the correction for every test run, then the strongest of each area (expected effect
    /// times the confidence that it is not luck), strongest first. Fewer than <paramref name="max"/> is normal.
    /// </summary>
    public List<Advice> Pick(IEnumerable<Advice?> candidates, int max)
    {
        var list = candidates.OfType<Advice>().ToList();
        var tested = list.Where(a => a.P.HasValue).OrderBy(a => a.P).ToList();
        var m = Math.Max(Count, tested.Count);
        var adjusted = new Dictionary<Advice, double>(ReferenceEqualityComparer.Instance);
        // Benjamini–Hochberg step-up: each p scaled by the tests over its rank, kept monotone from the largest down.
        var running = 1.0;
        for (var i = tested.Count - 1; i >= 0; i--)
        {
            running = Math.Min(running, tested[i].P!.Value * m / (i + 1));
            adjusted[tested[i]] = running;
        }
        return list.Select(a => (Advice: a, Q: a.P.HasValue ? adjusted[a] : 0))
            .Where(x => x.Q <= FalseDiscovery)
            .Select(x => (x.Advice, Strength: x.Advice.Effect * (1 - x.Q)))
            .GroupBy(x => x.Advice.Area).Select(g => g.MaxBy(x => x.Strength))
            .OrderByDescending(x => x.Strength).Take(max).Select(x => x.Advice).ToList();
    }

    /// <summary>Pearson correlation; 0 when either side does not vary.</summary>
    public static double Correlation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var (mx, my) = (x.Average(), y.Average());
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < x.Count; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }
        return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0;
    }

    /// <summary>Standard deviation of a population.</summary>
    public static double Std(IReadOnlyList<double> x)
    {
        var m = x.Average();
        return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / x.Count);
    }
}
