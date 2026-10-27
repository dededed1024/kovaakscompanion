using System.Text.RegularExpressions;

namespace KovaaksCompanion.Core.Benchmarks;

/// <summary>The overall tier of a playlist plus the fill of its progress bar (0..1).</summary>
internal readonly record struct TierResult(int Tier, double Progress);

/// <summary>
/// Works out a benchmark playlist's overall tier and progress bar from the player's scenario scores.
/// Every playlist "method" has its own rule set; this class dispatches on <see cref="TierSetup.Method"/>.
/// </summary>
internal static class TierCalculationEngine
{
    private static readonly TierResult Zero = new(0, 0);

    /// <summary>Computes the final tier and progress for a playlist.</summary>
    public static TierResult Compute(TierSetup setup, IReadOnlyList<string> rankNames, IReadOnlyList<SubcategoryProgress> parts)
        => new Run(setup, rankNames, parts).Compute();

    // ------------------------------------------------------------------------------------------------
    // Small numeric helpers
    // ------------------------------------------------------------------------------------------------

    private static double RoundHalfUp(double x) => Math.Floor(x + 0.5);
    private static double Round1(double x) => RoundHalfUp(10 * x) / 10;
    private static double Floor2(double x) => Math.Floor(100 * x) / 100;
    private static double Clamp01(double x) => Math.Clamp(x, 0, 1);

    /// <summary>Converts a double to an int tier; NaN becomes 0 and huge values saturate.</summary>
    private static int ToTier(double x)
    {
        if (double.IsNaN(x)) return 0;
        if (x >= int.MaxValue) return int.MaxValue;
        if (x <= int.MinValue) return int.MinValue;
        return (int)x;
    }

    /// <summary>Reads list[i], or 0 when the index is out of range.</summary>
    private static double At(IReadOnlyList<double> list, int i) => i >= 0 && i < list.Count ? list[i] : 0;

    /// <summary>Builds <paramref name="n"/> evenly spaced values beginning at <paramref name="start"/>.</summary>
    private static List<double> Steps(int n, double start = 100, double step = 100)
    {
        var result = new List<double>();
        for (int i = 0; i < n; i++) result.Add(start + i * step);
        return result;
    }

    /// <summary>Half-open slice [from, to) of a list, clipped to the list bounds.</summary>
    private static List<double> Slice(IReadOnlyList<double> list, int from, int to)
    {
        var result = new List<double>();
        for (int i = Math.Max(from, 0); i < Math.Min(to, list.Count); i++) result.Add(list[i]);
        return result;
    }

    /// <summary>Smallest value above zero, or null when there is none.</summary>
    private static double? MinPositive(IReadOnlyList<double> list)
    {
        double? best = null;
        foreach (var v in list)
            if (v > 0 && (best is null || v < best)) best = v;
        return best;
    }

    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

    private static double Mean(IReadOnlyList<double> v)
    {
        double sum = 0;
        foreach (var x in v) sum += x;
        return v.Count == 0 ? 0 : sum / v.Count;
    }

    /// <summary>Harmonic mean of a list (no zero checks).</summary>
    private static double HarmonicMean(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return 0;
        double inv = 0;
        foreach (var x in v) inv += 1 / x;
        return v.Count / inv;
    }

    /// <summary>k / sum(1/v); zero when the count differs from k or any value is zero.</summary>
    private static double Harmonic(IReadOnlyList<double> values, int k)
    {
        if (values.Count != k) return 0;
        double inv = 0;
        foreach (var x in values)
        {
            if (x == 0) return 0;
            inv += 1 / x;
        }
        return k / inv;
    }

    // ------------------------------------------------------------------------------------------------
    // Positions and ladders
    // ------------------------------------------------------------------------------------------------

    /// <summary>Where a score sits relative to its tier thresholds.</summary>
    private readonly record struct Pos(int Base, double Precise, double Frac, bool Maxed);

    private static Pos Position(double e, IReadOnlyList<double> t)
    {
        int n = t.Count;
        if (e <= 0 || n == 0) return new Pos(0, 0, 0, false);

        int reached = 0;
        for (int i = 0; i < n; i++)
            if (e >= t[i]) reached = i + 1;

        if (reached == 0)
        {
            var mp = MinPositive(t);
            double frac = mp is null ? 0 : Math.Min(e / mp.Value, 0.99);
            return new Pos(0, 0, frac, false);
        }

        if (reached == n)
        {
            double last = t[n - 1];
            double prev = n >= 2 ? t[n - 2] : 0;
            double span = Math.Abs(last - prev);
            if (span == 0) span = 1;
            double u = Math.Max(0, (e - last) / span);
            return new Pos(n, n + u, u - Math.Floor(u), true);
        }

        double lo = t[reached - 1];
        double w = t[reached] - lo;
        double f = w > 0 ? (e - lo) / w : 0;
        return new Pos(reached, reached + f, Math.Clamp(f, 0, 1), false);
    }

    private static Pos PositionOf(ScenarioProgress s) => Position(s.Score, s.TierThresholds);

    /// <summary>Progress (0..1, two decimals) inside the band that follows tier <paramref name="r"/> on the ladder.</summary>
    private static double Band(double x, IReadOnlyList<double> ladder, int r)
    {
        if (ladder.Count == 0) return 0;
        if (r <= 0)
        {
            var mp = MinPositive(ladder);
            return mp is null ? 0 : Floor2(Math.Clamp(x / mp.Value, 0, 1));
        }
        if (r >= ladder.Count) return 1;
        double lo = ladder[r - 1];
        double w = ladder[r] - lo;
        if (!double.IsFinite(w) || w <= 0) return 0;
        return Floor2(Math.Clamp((x - lo) / w, 0, 1));
    }

    /// <summary>Tier = one past the highest ladder entry that x reaches.</summary>
    private static TierResult Climb(double x, IReadOnlyList<double> ladder)
    {
        int r = 0;
        for (int i = 0; i < ladder.Count; i++)
            if (x >= ladder[i]) r = i + 1;
        return new TierResult(r, Band(x, ladder, r));
    }

    /// <summary>Tier = how many ladder entries are at or below x.</summary>
    private static TierResult RankByCount(double x, IReadOnlyList<double> ladder)
    {
        int r = 0;
        foreach (var v in ladder)
            if (v <= x) r++;
        return new TierResult(r, Band(x, ladder, r));
    }

    /// <summary>
    /// Converts a scenario's position into points on an energy ladder. Below the first tier the points are
    /// interpolated from the tier thresholds; above it they follow the ladder, with <paramref name="upper"/>
    /// controlling how far past the top the value may keep growing.
    /// </summary>
    private static double ScenarioEnergy(Pos p, double score, IReadOnlyList<double> t, IReadOnlyList<double> ladder,
        double lower, double upper, bool truncate)
    {
        double value;
        int a = ladder.Count;
        if (p.Base == 0 && p.Precise == 0)
        {
            if (score <= 0 || t.Count < 2) return 0;
            double m = t[0];
            double f = t[1] - m;
            double b0 = m - f;
            double pts = ladder[0] - lower;
            value = score < b0
                ? score / b0 * pts
                : pts + (score - b0) / (m - b0) * (ladder[0] - pts);
        }
        else
        {
            double first = ladder[0] - lower;
            double top = ladder[a - 1];
            double step = a > 1 ? top - ladder[a - 2] : 0;
            if (step == 0 || double.IsNaN(step)) step = 100;
            double h = top + step;
            double pr = p.Precise;
            if (pr <= 0) value = 0;
            else if (pr < 1) value = first + pr * (ladder[0] - first);
            else if (pr < a)
            {
                int d = (int)Math.Floor(pr);
                double from = d == 0 ? first : ladder[d - 1];
                value = from + (pr - d) * (ladder[d] - from);
            }
            else if (pr < a + upper) value = top + (pr - a) * (h - top);
            else value = h;
        }
        return truncate ? Math.Truncate(value) : value;
    }

    // ------------------------------------------------------------------------------------------------
    // Per-evaluation state
    // ------------------------------------------------------------------------------------------------

    private sealed record Cat(string Name, List<SubcategoryProgress> Subs);

    private sealed record Best(string Category, string Name, Pos P, double Score, IReadOnlyList<double> T);

    private sealed class Run
    {
        private readonly TierSetup setup;
        private readonly IReadOnlyList<string> rankNames;
        private readonly IReadOnlyList<SubcategoryProgress> parts;
        private readonly string key;
        private readonly IReadOnlyList<string> tiers;
        private readonly List<ScenarioProgress> all = new();
        private readonly List<Cat> cats = new();
        private readonly int n;

        public Run(TierSetup setup, IReadOnlyList<string> rankNames, IReadOnlyList<SubcategoryProgress> parts)
        {
            this.setup = setup;
            this.rankNames = rankNames;
            this.parts = parts;
            key = Norm(setup.Difficulty);
            tiers = setup.Tiers;
            foreach (var p in parts) all.AddRange(p.Scenarios);
            n = all.Count;
            foreach (var g in parts.GroupBy(p => p.Parent))
                cats.Add(new Cat(g.Key, g.ToList()));
        }

        private IEnumerable<SubcategoryProgress> OrderedSubs() => cats.SelectMany(c => c.Subs);

        // -------------------------------------------------------------------------------------------
        // Top-level combination
        // -------------------------------------------------------------------------------------------

        public TierResult Compute()
        {
            int complete = CompleteTier();
            TierResult? custom = n == 0 ? null : Custom();

            bool useCustom = custom is not null && (setup.Method == "selectable-top-n" || custom.Value.Tier > complete);
            int tier = useCustom ? custom!.Value.Tier : complete;

            double progress;
            if (custom is not null) progress = custom.Value.Progress;
            else if (n == 0) progress = 0;
            else
            {
                int above = 0;
                foreach (var s in all)
                    if (s.Tier > tier) above++;
                progress = (double)above / n;
            }

            tier = Math.Clamp(tier, 0, Math.Max(rankNames.Count - 1, 0));
            if (double.IsNaN(progress)) progress = 0;
            progress = Clamp01(progress);
            return new TierResult(tier, progress);
        }

        /// <summary>The tier every scenario has at least reached; 0 if any scenario is unplayed or unranked.</summary>
        private int CompleteTier()
        {
            if (n == 0) return 0;
            int lowest = int.MaxValue;
            foreach (var s in all)
            {
                if (s.Score <= 0 || s.Tier <= 0) return 0;
                lowest = Math.Min(lowest, s.Tier);
            }
            return lowest >= rankNames.Count ? 0 : lowest;
        }

        private TierResult? Custom()
        {
            switch (setup.Method)
            {
                case "basic": return Basic();
                case "ca-s1": return Energy(Steps(7, 1500, 50), NoStrafe, 50, 2, null, false).Result;
                case "sa-s2": return Energy(Steps(5, 1200), NoStrafe, 50, 1, null, false).Result;
                case "mira":
                    return Energy(key == "easy" ? Steps(5) : Steps(5, 600), AllSubs, 50, 1, null, false).Result;
                case "val-energy": return Energy(ValLadder(), AllSubs, 100, 1, null, false).Result;
                case "generic-energy":
                {
                    var slice = SiblingSlice();
                    if (slice.Count == 0) return Zero;
                    var ladder = slice.GetRange(0, slice.Count - 1);
                    return Energy(ladder, AllSubs, 100, 1, slice[^1], false).Result;
                }
                case "generic-energy-uncapped":
                {
                    var slice = SiblingSlice();
                    if (slice.Count == 0) return Zero;
                    var ladder = slice.GetRange(0, slice.Count - 1);
                    return Energy(ladder, AllSubs, 100, 9999, null, false).Result;
                }
                case "vt-energy": return VtEnergy();

                case "Avasive-S2":
                {
                    double? cap = key switch { "easier" => 600, "medium" => 1000, "hard" => 1400, _ => null };
                    return PairEnergy(ContinuationLadder(), cap);
                }
                case "mh-tracking":
                case "mh-reactive":
                case "mh-precise":
                    return PairEnergy(ContinuationLadder(), null);
                case "snakbox":
                {
                    var ladder = key == "medium"
                        ? new List<double> { 400, 500, 600, 700, 800, 900, 1100 }
                        : ContinuationLadder();
                    if (ladder.Count == 0) return Zero;
                    return PairEnergy(ladder, ladder[^1]);
                }

                case "tpt": return CountRule(5, true);
                case "asb": return CountRule(8, true);
                case "e1se": return CountRule(6, true);
                case "dojo": return CountRule(4, false);
                case "dojo2": return CountRule(3, false);
                case "dojo3": return CountRule(5, false);
                case "rbe": return CountRule(9, false);

                case "hewchy": return Hewchy();
                case "routine": return Routine();
                case "selectable-top-n": return SelectableTopN();
                case "mh": return PointsHarmonic(MhTable());
                case "avasive": return PointsHarmonic(AvasiveTable());
                case "ssb2": return Ssb2();
                case "aplus-alt": return AplusAlt();
                case "RXZU": return Rxzu();
                case "MIYU": return Miyu();
                case "generic-energy-alt": return GenericEnergyAlt();
                case "mira-apex": return MiraApex();
                case "dm": return Dm();
                case "dm-s3": return DmS3();
                case "ra-s5": return RaS5();
                case "33":
                case "iris":
                    return ThirtyThree();
                case "jade-palace": return JadeLike(null, "easy", false);
                case "Good-Energy": return JadeLike(3, "normal", true);
                case "ra-s4": return RaS4();
                case "cb-s1": return CbS1();
                case "aplus-s1": return AplusS1();
                case "tsk": return Tsk();
                case "xyz": return Xyz();
                case "xyz2": return Xyz2();
                case "xyz-smoothness-v2": return XyzSmoothnessV2();
                case "aoi": return Aoi();
                case "真・葵Aim": return Aoi2();
                case "aimbeast": return Aimbeast();
                case "aimbeast-partial": return AimbeastPartial();
                default: return null; // includes "complete"
            }
        }

        // -------------------------------------------------------------------------------------------
        // Shared procedures
        // -------------------------------------------------------------------------------------------

        private static bool AllSubs(string name) => true;
        private static bool NoStrafe(string name) => !name.ToLowerInvariant().Contains("strafe");

        /// <summary>For every subcategory, the single scenario that represents it (see section 6.1 of the rules).</summary>
        private List<Best> BestPerSub(IReadOnlyList<double>? ladder, double lower, double upper)
        {
            var result = new List<Best>();
            foreach (var cat in cats)
            foreach (var sub in cat.Subs)
            {
                Best? best = null;
                foreach (var s in sub.Scenarios)
                {
                    if (s.Score == 0) continue;
                    var cand = new Best(cat.Name, sub.Name, PositionOf(s), s.Score, s.TierThresholds);
                    if (best is null || Beats(cand, best, ladder, lower, upper)) best = cand;
                }
                result.Add(best ?? new Best(cat.Name, sub.Name, new Pos(0, 0, 0, false), 0, Array.Empty<double>()));
            }
            return result;
        }

        private static bool Beats(Best c, Best b, IReadOnlyList<double>? ladder, double lower, double upper)
        {
            if (c.P.Base > 0 && b.P.Base == 0) return true;
            if (c.P.Base > 0 && b.P.Base > 0) return c.P.Precise > b.P.Precise;
            if (c.P.Base == 0 && b.P.Base == 0)
            {
                if (ladder is not null)
                    return ScenarioEnergy(c.P, c.Score, c.T, ladder, lower, upper, true)
                         > ScenarioEnergy(b.P, b.Score, b.T, ladder, lower, upper, true);
                return Ratio(c) > Ratio(b);
            }
            return false;
        }

        private static double Ratio(Best b) => b.T.Count > 0 ? b.Score / b.T[0] : 0;

        /// <summary>The energy-style total (harmonic mean of per-subcategory energy) and the resulting tier.</summary>
        private (double Total, TierResult Result) Energy(IReadOnlyList<double> ladder, Func<string, bool> filter,
            double lower, double upper, double? cap, bool uncapped)
        {
            if (ladder.Count == 0) return (0, Zero);

            double limit = cap ?? (key == "advanced" && !uncapped ? 1200 : double.PositiveInfinity);
            var energies = new List<double>();
            int filtered = 0;
            foreach (var b in BestPerSub(ladder, lower, upper))
            {
                if (!filter(b.Name)) continue;
                filtered++;
                double e = ScenarioEnergy(b.P, b.Score, b.T, ladder, lower, upper, true);
                e = Math.Min(e, limit);
                if (e > 0) energies.Add(e);
            }

            double total = Round1(Harmonic(energies, filtered));
            var result = Climb(total, ladder);

            if (uncapped && result.Tier >= ladder.Count)
            {
                double last = ladder[^1];
                double prev = ladder.Count >= 2 ? ladder[^2] : last - 100;
                double step = last - prev;
                if (step == 0) step = 100;
                result = result with { Progress = 1 + Math.Max(0, (total - last) / step) };
            }
            return (total, result);
        }

        /// <summary>Energy where each subcategory is judged by its two best scenarios.</summary>
        private TierResult PairEnergy(IReadOnlyList<double> ladder, double? cap)
        {
            if (ladder.Count == 0) return Zero;
            var values = new List<double>();
            foreach (var sub in OrderedSubs())
            {
                var energies = new List<double>();
                foreach (var s in sub.Scenarios)
                {
                    if (!(s.Score > 0)) continue;
                    double e = ScenarioEnergy(PositionOf(s), s.Score, s.TierThresholds, ladder, 100,
                        double.PositiveInfinity, false);
                    if (cap.HasValue) e = Math.Min(e, cap.Value);
                    energies.Add(e);
                }
                var sorted = energies.OrderByDescending(v => v).ToList();
                values.Add(sorted.Count == 0 ? 0 : sorted.Count == 1 ? sorted[0] / 2 : (sorted[0] + sorted[1]) / 2);
            }
            double total = values.Count == 0 ? 0 : Round1(Harmonic(values, values.Count));
            return RankByCount(total, ladder);
        }

        /// <summary>Slice of a running 100-step ladder that belongs to this difficulty among its siblings.</summary>
        private List<double> SiblingSlice()
        {
            int total = 0;
            foreach (var s in setup.Siblings) total += s.Tiers.Count;
            var full = Steps(total + 1);

            int skip = 0;
            bool found = false;
            foreach (var s in setup.Siblings)
            {
                if (s.Name == setup.Difficulty) { found = true; break; }
                skip += s.Tiers.Count;
            }
            if (!found) skip = total;

            return Slice(full, skip, skip + tiers.Count + 1);
        }

        /// <summary>Ladder that continues numbering after the easier siblings (adjacent shared tiers count once).</summary>
        private List<double> ContinuationLadder()
        {
            var sibs = setup.Siblings;
            int idx = 0;
            for (int i = 0; i < sibs.Count; i++)
                if (Norm(sibs[i].Name) == key) { idx = i; break; }

            int before = 0;
            for (int i = 0; i < idx; i++) before += sibs[i].Tiers.Count;

            int shared = 0;
            for (int u = 0; u < idx; u++)
            {
                if (u + 1 >= sibs.Count) continue;
                var cur = sibs[u].Tiers;
                var next = sibs[u + 1].Tiers;
                if (cur.Count == 0 || next.Count == 0) continue;
                if (Norm(cur[^1]) == Norm(next[0])) shared++;
            }
            return Steps(tiers.Count, (before - shared + 1) * 100.0);
        }

        /// <summary>Tier = highest tier held by at least <paramref name="required"/> scenarios.</summary>
        private TierResult CountRule(int required, bool exactNext)
        {
            var positive = all.Where(s => s.Tier > 0).Select(s => s.Tier).ToList();
            if (positive.Count == 0) return Zero;
            int tier = 0;
            foreach (var r in positive.Distinct().OrderByDescending(v => v))
                if (positive.Count(v => v >= r) >= required) { tier = r; break; }
            int next = exactNext ? positive.Count(v => v == tier + 1) : positive.Count(v => v >= tier + 1);
            double progress = tier > 0 ? Math.Min((double)next / required, 1) : 0;
            return new TierResult(tier, progress);
        }

        /// <summary>Per-subcategory maximum of a value computed for each played scenario (never below 0).</summary>
        private List<double> PerSubMax(Func<ScenarioProgress, Pos, double> value)
        {
            var result = new List<double>();
            foreach (var sub in parts)
            {
                double max = 0;
                foreach (var s in sub.Scenarios)
                {
                    if (!(s.Score > 0)) continue;
                    max = Math.Max(max, value(s, PositionOf(s)));
                }
                result.Add(max);
            }
            return result;
        }

        // -------------------------------------------------------------------------------------------
        // Methods
        // -------------------------------------------------------------------------------------------

        private TierResult Basic()
        {
            var bests = BestPerSub(null, 0, 0);
            int s = 0;
            if (bests.Count > 0 && !bests.Any(b => b.P.Base == 0)) s = bests.Min(b => b.P.Base);

            var m = new List<double>();
            foreach (var b in bests)
            {
                double pr = b.P.Precise;
                if (b.P.Base == 0 || pr == 0) m.Add(0.001);
                else if (pr < s) m.Add(0);
                else if (pr >= s + 1) m.Add(1);
                else m.Add(pr - s);
            }
            double progress = s >= tiers.Count ? 1 : Mean(m);
            return new TierResult(s, progress);
        }

        private List<double> ValLadder()
        {
            var s15 = Steps(15);
            return key switch
            {
                "easy" => Slice(s15, 0, 4),
                "medium" => Slice(s15, 4, 8),
                "hard" => Slice(s15, 8, 12),
                _ => s15,
            };
        }

        private List<double> VtLadder()
        {
            var s15 = Steps(15);
            switch (key)
            {
                case "novice": return Slice(s15, 0, 4);
                case "intermediate": return Slice(s15, 4, 8);
                case "advanced": return Slice(s15, 8, 12);
                case "elite (unofficial)":
                {
                    string? first = tiers.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && !IsPlaceholder(t));
                    if (first is null)
                        first = rankNames.Select(r => (r ?? "").Trim())
                                         .FirstOrDefault(r => r.Length > 0 && !IsPlaceholder(r));
                    return AsciiLetters(first).StartsWith("grandmaster", StringComparison.Ordinal)
                        ? Slice(s15, 8, 14)
                        : Slice(s15, 9, 15);
                }
                default: return s15;
            }
        }

        /// <summary>Lower-cased text reduced to its ASCII letters a-z.</summary>
        private static string AsciiLetters(string? s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in (s ?? "").ToLowerInvariant())
                if (ch >= 'a' && ch <= 'z') sb.Append(ch);
            return sb.ToString();
        }

        private static bool IsPlaceholder(string name)
        {
            var norm = AsciiLetters(name);
            return norm is "unranked" or "norank" or "nodata" or "notcomplete";
        }

        private double VtLower() => key == "novice" ? 0 : 100;

        private TierResult VtEnergy()
        {
            var ladder = VtLadder();
            double lower = VtLower();
            var result = Energy(ladder, NoStrafe, lower, 1, null, false).Result;
            if (key == "advanced" && tiers.Count > 0 && result.Tier >= tiers.Count)
                result = Energy(ladder, NoStrafe, lower, double.PositiveInfinity, null, true).Result;
            return result;
        }

        private TierResult Hewchy()
        {
            var ts = all.Select(s => s.Tier).ToList();
            int tier = 0;
            for (int s = ts.Max(); s >= 1; s--)
                if (ts.Count(v => v >= s) >= 12) { tier = s; break; }
            double progress = tier > 0 ? Math.Min(ts.Count(v => v >= tier + 1) / 12.0, 1) : 0;
            return new TierResult(tier, progress);
        }

        private TierResult Routine()
        {
            int need = (int)Math.Ceiling(n / 2.0);
            var positive = all.Where(s => s.Tier > 0).Select(s => s.Tier).ToList();
            int tier = 0;
            foreach (var r in positive.Distinct().OrderByDescending(v => v))
                if (positive.Count(v => v >= r) >= need) { tier = r; break; }
            int highest = positive.Count == 0 ? 0 : positive.Max();
            if (tier >= highest) return new TierResult(tier, 1);
            return new TierResult(tier, Math.Min((double)positive.Count(v => v >= tier + 1) / need, 1));
        }

        private TierResult SelectableTopN()
        {
            var sel = setup.Selection;
            int baseCount = sel?.BaseTierScoreCount ?? 12;
            int required = sel?.FullPoolTierScoreCount ?? baseCount;
            int minCat = sel?.MinPerCategory ?? 0;
            int minSub = sel?.MinPerSubcategory ?? 0;

            var scored = new List<(double Base, double Precise)>();
            foreach (var s in all)
            {
                if (!(s.Score > 0)) continue;
                if (s.TierThresholds.Count > 0)
                {
                    var p = PositionOf(s);
                    scored.Add((p.Base, p.Precise));
                }
                else scored.Add((s.Tier, s.Tier));
            }

            foreach (var c in cats)
                if (c.Subs.Sum(sub => sub.Scenarios.Count) < minCat) return Zero;
            foreach (var sub in parts)
                if (sub.Scenarios.Count < minSub) return Zero;
            if (scored.Count < required) return Zero;

            var taken = scored.OrderByDescending(v => v.Precise).Take(required).ToList();
            if (taken.Count == 0) return Zero;

            int x = ToTier(Math.Max(0, Math.Floor(taken[^1].Base)));
            double sum = 0;
            foreach (var t in taken)
                sum += t.Precise >= x + 1 ? 1 : t.Precise < x ? 0 : t.Precise - x;
            return new TierResult(x, Clamp01(sum / required));
        }

        private List<double>? MhTable() => key switch
        {
            "medium" => new List<double> { 500, 600, 700, 800 },
            "hard" => new List<double> { 900, 1000, 1100, 1200, 1300 },
            _ => new List<double> { 100, 200, 300, 400 }, // "easy" and the fallback for unknown keys
        };

        private List<double>? AvasiveTable() => key switch
        {
            "genesis" => Steps(5),
            "ascension" => Steps(5, 600),
            "enlightenment" => Steps(5, 1100),
            "wallhack" => Steps(9),
            _ => null, // this table has no "easy" entry
        };

        /// <summary>Per-scenario point values combined with a harmonic mean, then placed on the point table.</summary>
        private TierResult PointsHarmonic(List<double>? t)
        {
            if (t is null || t.Count == 0) return Zero;
            var values = new List<double>();
            foreach (var s in all)
            {
                var p = PositionOf(s);
                var th = s.TierThresholds;
                double v;
                if (p.Base == 0)
                {
                    double d = th.Count > 0 && th[0] != 0 ? th[0] : 1;
                    v = Math.Truncate(s.Score / d * t[0]);
                }
                else if (p.Maxed)
                {
                    double r = th[^1];
                    double f = th.Count >= 2 ? th[^2] : 0;
                    double w = r - f;
                    if (w == 0) w = 1;
                    v = Math.Truncate(t[^1] + (s.Score - r) / w * 100);
                }
                else
                {
                    double k = At(t, p.Base - 1);
                    double m = p.Base < t.Count ? t[p.Base] : double.NaN;
                    v = Math.Truncate(k + p.Frac * (m - k));
                }
                values.Add(v);
            }
            if (values.Any(v => v == 0)) return Zero;
            double total = Round1(Harmonic(values, n));
            return Climb(total, t);
        }

        private TierResult Ssb2()
        {
            var ladder = Steps(10);
            var values = new List<double>();
            foreach (var s in all)
            {
                var p = PositionOf(s);
                double v;
                if (p.Base == 0 && s.Score == 0) v = 0;
                else
                {
                    v = 100.0 * p.Base + 100 * p.Frac;
                    if (p.Maxed) v = Math.Min(v, 1050);
                }
                if (v > 0) values.Add(v);
            }
            double total = Math.Truncate(Harmonic(values, n));
            return Climb(total, ladder);
        }

        private TierResult AplusAlt()
        {
            var ladder = tiers.Count == 0 ? Steps(10) : Steps(tiers.Count);
            double share = 100.0 / n;
            double sum = 0;
            foreach (var s in all)
            {
                var p = PositionOf(s);
                if (p.Base == 0)
                {
                    var t = s.TierThresholds;
                    double d = t.Count == 0 || t[0] == 0 ? 1 : t[0];
                    sum += s.Score / d * share;
                }
                else sum += (p.Base + p.Frac) * share;
            }
            double total = Math.Round(sum * 100) / 100; // banker's rounding on purpose
            return Climb(total, ladder);
        }

        private TierResult Rxzu()
        {
            double[] ladder;
            if (key == "easy") ladder = new double[] { 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200 };
            else if (key == "hard") ladder = new double[] { 400, 500, 600, 700, 800, 850, 900, 950, 1000, 1050, 1100, 1150, 1200 };
            else return Zero;

            double sum = 0;
            foreach (var s in all)
                if (s.Tier > 0) sum += ladder[Math.Min(s.Tier, ladder.Length) - 1];
            return RankByCount(sum / n, ladder);
        }

        private TierResult Miyu()
        {
            var ladder = new double[] { 16, 24, 32, 40, 48, 56, 63 };
            double total = 0;
            foreach (var s in all)
                total += s.Tier <= 0 ? 0 : s.Tier + 1;
            return RankByCount(total, ladder);
        }

        private TierResult GenericEnergyAlt()
        {
            int k = tiers.Count > 0 ? tiers.Count : all[0].TierThresholds.Count;
            var ladder = Steps(k);
            if (ladder.Count == 0) return Zero;
            double cap = ladder[^1] + 50;

            double sum = 0;
            foreach (var s in all)
            {
                if (s.Score <= 0) continue;
                var p = PositionOf(s);
                var t = s.TierThresholds;
                double v;
                if (p.Base == 0)
                {
                    double d = t.Count == 0 || t[0] == 0 ? 1 : t[0];
                    v = s.Score / d * ladder[0];
                }
                else if (p.Maxed)
                {
                    double nn = t[^1];
                    double prev = t.Count >= 2 ? t[^2] : 0;
                    double span = Math.Abs(nn - prev);
                    if (span == 0) span = 1;
                    v = ladder[^1] + Math.Min((s.Score - nn) / span, 0.5) * 100;
                }
                else
                {
                    double lo = At(ladder, p.Base - 1);
                    double hi = p.Base < ladder.Count && ladder[p.Base] != 0 ? ladder[p.Base] : cap;
                    v = lo + p.Frac * (hi - lo);
                }
                sum += Math.Min(Math.Truncate(v), cap);
            }
            double total = Round1(sum / n);
            return RankByCount(total, ladder);
        }

        private TierResult MiraApex()
        {
            if (tiers.Count == 0) return Zero;
            var ladder = Steps(tiers.Count, 10, 10);
            var values = PerSubMax((s, p) =>
            {
                var t = s.TierThresholds;
                double v;
                if (t.Count == 0) v = 0;
                else if (p.Base == 0) v = t[0] > 0 ? Math.Min(s.Score / t[0], 1) * ladder[0] : 0;
                else if (p.Maxed) v = ladder[^1];
                else
                {
                    double lo = At(ladder, p.Base - 1);
                    double hi = p.Base < ladder.Count ? ladder[p.Base] : ladder[^1];
                    v = lo + p.Frac * (hi - lo);
                }
                return Math.Max(0, Math.Truncate(v));
            });
            if (values.Count == 0) return Zero;

            double u;
            if (values.All(v => v > 0)) u = HarmonicMean(values);
            else if (values.Any(v => v > 0))
            {
                double inv = 0;
                foreach (var v in values) inv += 1 / (v == 0 ? 0.1 : v);
                u = values.Count / inv;
            }
            else u = 0;
            return RankByCount(Round1(u), ladder);
        }

        private TierResult Dm()
        {
            int r = tiers.Count;
            if (r == 0) return Zero;
            double start = 100;
            if ((key == "boss" && r == 8) || key == "boss+") start = 500;
            else if (key == "boss++") start = 900;

            var ladder = new List<double> { start };
            for (int i = 1; i < r; i++)
                ladder.Add(ladder[i - 1] + (i == r - 1 ? 10 : 100));

            var values = PerSubMax((s, p) =>
            {
                var t = s.TierThresholds;
                double v;
                if (p.Base == 0) v = 0;
                else if (p.Maxed)
                {
                    double last = t[^1];
                    double prev = t.Count >= 2 ? t[^2] : 0;
                    double w = Math.Abs(last - prev);
                    if (w == 0) w = 1;
                    v = ladder[^1] + Math.Max(0, (s.Score - last) / w) * 10;
                }
                else
                {
                    double lo = At(ladder, p.Base - 1);
                    double hi = p.Base < r ? ladder[p.Base] : ladder[^1];
                    v = lo + p.Frac * (hi - lo);
                }
                return Math.Max(0, Math.Truncate(v));
            });

            double h = values.All(v => v > 0) ? HarmonicMean(values) : 0;
            return RankByCount(Round1(h), ladder);
        }

        private TierResult DmS3()
        {
            double[] ladder;
            if (key.Contains("boss")) ladder = new double[] { 1300, 1400, 1500, 1510 };
            else
            {
                var m = Regex.Match(key, @"level\s*([1-4])");
                if (m.Success)
                {
                    double s0 = (m.Groups[1].Value[0] - '1') * 300 + 100;
                    ladder = new[] { s0, s0 + 100, s0 + 200, 1510 };
                }
                else ladder = new double[] { 100, 200, 300, 1510 };
            }

            var values = PerSubMax((s, p) =>
            {
                var t = s.TierThresholds;
                double v;
                if (p.Base == 0)
                {
                    double d = t.Count == 0 || t[0] == 0 ? 1 : t[0];
                    v = Math.Min(s.Score / d * ladder[0], ladder[0]);
                }
                else if (p.Maxed) v = ladder[^1];
                else
                {
                    double lo = At(ladder, p.Base - 1);
                    double hi = p.Base < 4 ? ladder[p.Base] : ladder[^1];
                    v = lo + p.Frac * (hi - lo);
                }
                return Math.Max(0, Math.Truncate(v));
            });

            double h = values.All(v => v > 0) ? HarmonicMean(values) : 0;
            return RankByCount(Round1(h), ladder);
        }

        private TierResult RaS5()
        {
            var ladder = SiblingSlice();
            if (ladder.Count > 0) ladder.RemoveAt(ladder.Count - 1);
            if (ladder.Count == 0) return Zero;

            var subEnergies = new List<double>();
            foreach (var sub in parts)
            {
                bool reactive = sub.Name.ToLowerInvariant().Contains("reactive");
                var values = new List<double>();
                foreach (var s in sub.Scenarios)
                {
                    if (s.Score == 0) { values.Add(0); continue; }
                    var p = PositionOf(s);
                    var t = s.TierThresholds;
                    double q;
                    if (p.Base == 0)
                    {
                        double h = t.Count == 0 || t[0] == 0 ? 1 : t[0];
                        double g = reactive ? (key == "entry" ? 800 : 830) : 0;
                        int z = reactive ? 2 : 0;
                        double scale = Math.Pow(10, z);
                        double d = h - g;
                        double j = d > 0 ? RoundHalfUp(Math.Max(0, (s.Score - g) / d) * 100 * scale) / scale : 0;
                        q = ladder[0] * j / 100;
                    }
                    else if (p.Maxed) q = ladder[^1];
                    else
                    {
                        double lo = At(ladder, p.Base - 1);
                        double hi = p.Base < ladder.Count && ladder[p.Base] != 0 ? ladder[p.Base] : ladder[^1];
                        double zz = RoundHalfUp(p.Frac * 100);
                        q = lo + (hi - lo) * zz / 100;
                    }
                    values.Add(Math.Max(0, RoundHalfUp(q * 100) / 100));
                }

                double e;
                if (values.Count == 0) e = 0;
                else if (reactive)
                    e = values.Count == 4 ? (Math.Max(values[0], values[1]) + Math.Max(values[2], values[3])) / 2 : 0;
                else
                {
                    var sorted = values.OrderByDescending(v => v).ToList();
                    e = sorted.Count == 1 ? sorted[0] / 2 : (sorted[0] + sorted[1]) / 2;
                }
                subEnergies.Add(RoundHalfUp(e));
            }

            double total = subEnergies.All(v => v > 0) ? HarmonicMean(subEnergies) : 0;
            return RankByCount(RoundHalfUp(total), ladder);
        }

        private TierResult ThirtyThree()
        {
            int k = tiers.Count > 0 ? tiers.Count : all[0].TierThresholds.Count;
            double start;
            switch (key)
            {
                case "novice":
                case "beginner":
                case "easy":
                case "intermediate":
                    start = 100; break;
                case "adv":
                case "advanced":
                case "hard":
                    start = 200; break;
                default:
                {
                    var sibs = setup.Siblings;
                    int idx = -1;
                    for (int i = 0; i < sibs.Count; i++)
                        if (Norm(sibs[i].Name) == key) { idx = i; break; }
                    start = idx >= 0 ? 100.0 * (1 + idx) : 100;
                    break;
                }
            }
            if (k == 0) return Zero;
            var ladder = Steps(k, start);

            var catValues = new List<double>();
            foreach (var cat in cats)
            {
                var subValues = new List<double>();
                foreach (var sub in cat.Subs)
                {
                    var values = new List<double>();
                    foreach (var s in sub.Scenarios)
                    {
                        double v = 0;
                        if (s.Score > 0 && s.TierThresholds.Count > 0)
                        {
                            var p = PositionOf(s);
                            if (p.Base == 0) v = 0;
                            else if (p.Maxed) v = ladder[^1];
                            else
                            {
                                double lo = At(ladder, p.Base - 1);
                                double hi = p.Base < ladder.Count && ladder[p.Base] != 0 ? ladder[p.Base] : ladder[^1];
                                v = lo + p.Frac * (hi - lo);
                            }
                            v = Math.Max(0, RoundHalfUp(v * 100) / 100);
                        }
                        values.Add(v);
                    }

                    if (values.Count == 0) { subValues.Add(0); continue; }
                    int half = (int)Math.Ceiling(values.Count / 2.0);
                    if (values.Count(v => v >= ladder[0]) >= half)
                        subValues.Add(RoundHalfUp(Mean(values.OrderByDescending(v => v).Take(half).ToList())));
                    else
                        subValues.Add(RoundHalfUp(values.Sum() / (2.0 * half)));
                }
                catValues.Add(RoundHalfUp(Mean(subValues)));
            }

            double total = catValues.Count > 0 && catValues.All(v => v > 0) ? Mean(catValues) : 0;
            return RankByCount(RoundHalfUp(total), ladder);
        }

        /// <summary>Shared rule for playlists that average the top few scenarios of every subcategory.</summary>
        private TierResult JadeLike(int? fixedCount, string openDifficulty, bool alwaysOpen)
        {
            var ladder = SiblingSlice();
            if (ladder.Count > 0) ladder.RemoveAt(ladder.Count - 1);
            if (ladder.Count == 0) return Zero;

            bool fundamentals = key == "fundamentals";
            bool open = key == openDifficulty;

            var lows = new List<double>();
            var highs = new List<double>();
            foreach (var sub in parts)
            {
                var lowValues = new List<double>();
                var highValues = new List<double>();
                foreach (var s in sub.Scenarios)
                {
                    double low = 0, high = 0;
                    var t = s.TierThresholds;
                    if (s.Score > 0 && t.Count > 0)
                    {
                        var p = PositionOf(s);
                        if (p.Base == 0)
                        {
                            if (t[0] > 0) low = high = Math.Min(s.Score / t[0], 1) * ladder[0];
                        }
                        else if (p.Maxed)
                        {
                            double w = t[^1];
                            double d = t.Count >= 2 ? t[^2] : 0;
                            double span = Math.Abs(w - d);
                            if (span == 0) span = 1;
                            double over = Math.Max(0, (s.Score - w) / span);
                            low = ladder[^1] + Math.Min(over, 1) * 100;
                            high = open ? ladder[^1] + over * 100 : low;
                        }
                        else
                        {
                            double lo = At(ladder, p.Base - 1);
                            double hi = p.Base < ladder.Count && ladder[p.Base] != 0 ? ladder[p.Base] : ladder[^1];
                            low = high = lo + p.Frac * (hi - lo);
                        }
                        low = Math.Truncate(low);
                        high = Math.Truncate(high);
                    }
                    lowValues.Add(low);
                    highValues.Add(high);
                }

                int count = sub.Scenarios.Count;
                int q = fixedCount ?? (fundamentals ? Math.Min(3, count) : count / 2);
                lows.Add(TopMean(lowValues, q));
                highs.Add(TopMean(highValues, q));
            }

            double nLow = RobustHarmonic(lows);
            double total = open && (alwaysOpen || nLow >= 600) ? RobustHarmonic(highs) : nLow;
            return RankByCount(total, ladder);
        }

        private static double TopMean(List<double> values, int q)
        {
            var top = values.OrderByDescending(v => v).Take(Math.Max(q, 0)).ToList();
            return top.Count == 0 ? 0 : Math.Truncate(Mean(top));
        }

        private static double RobustHarmonic(List<double> values)
        {
            if (values.Count == 0) return 0;
            double inv = 0;
            foreach (var v in values) inv += 1 / (v <= 0 ? 0.1 : v);
            return Math.Truncate(values.Count / inv);
        }

        private TierResult RaS4()
        {
            double[] points, ladder;
            if (key == "easy")
            {
                points = new double[] { 20, 50, 80, 110, 140, 170 };
                ladder = new double[] { 240, 600, 960, 1320, 1680, 2040 };
            }
            else
            {
                points = new double[] { 200, 235, 270, 320, 360, 400 };
                ladder = new double[] { 2400, 2820, 3240, 3840, 4320, 4800 };
            }

            double total = 0;
            foreach (var cat in cats)
            {
                var scenarios = cat.Subs.SelectMany(s => s.Scenarios)
                    .Select(s => (S: s, Precise: PositionOf(s).Precise))
                    .OrderByDescending(x => x.Precise)
                    .Take(4);
                foreach (var x in scenarios)
                    total += RaS4Points(x.S.Score, x.S.TierThresholds, points, 1.5);
            }
            return Climb(total, ladder);
        }

        private static double RaS4Points(double e, IReadOnlyList<double> nn, IReadOnlyList<double> r, double div)
        {
            if (nn.Count < 2 || r.Count < 2 || nn.Count != r.Count) return 0;
            if (e < nn[1])
            {
                double s = nn[1] - nn[0];
                double c = (r[2] - r[1]) / (2.0 / 3.0);
                return Math.Max(0, Math.Ceiling(r[1] + (e - nn[1]) / s * c));
            }
            for (int j = 1; j <= nn.Count - 2; j++)
                if (e <= nn[j + 1])
                    return r[j] + (e - nn[j]) / (nn[j + 1] - nn[j]) * (r[j + 1] - r[j]);
            int o = nn.Count - 1;
            return r[o] + (e - nn[o]) / (nn[o] - nn[o - 1]) * (r[o] - r[o - 1]) / div;
        }

        private TierResult CbS1()
        {
            var r = new double[] { 300, 600, 900, 1200, 1500, 1800 };
            double sum = 0;
            foreach (var s in all)
            {
                var p = PositionOf(s);
                var t = s.TierThresholds;
                double m;
                if (p.Base == 0)
                    m = t.Count == 0 ? double.NaN : s.Score / t[0] * r[0];
                else if (p.Maxed)
                {
                    double last = t[p.Base - 1];
                    double prev = p.Base >= 2 ? t[p.Base - 2] : 0;
                    double span = Math.Abs(last - prev);
                    if (span == 0) span = 1;
                    double idx = Math.Min(p.Base + Math.Max(0, (s.Score - last) / span), 5);
                    m = double.IsFinite(idx) && idx == Math.Floor(idx) && idx >= 0 && idx < r.Length
                        ? r[(int)idx]
                        : double.NaN;
                }
                else
                {
                    if (p.Base >= r.Length) m = double.NaN;
                    else m = r[p.Base - 1] + (r[p.Base] - r[p.Base - 1]) * p.Frac;
                }
                sum += m / 1800 * 100;
            }
            return Climb(sum, r);
        }

        private TierResult AplusS1()
        {
            var b = Basic();
            int plus = 0;
            if (cats.Count > 0)
            {
                plus = int.MaxValue;
                foreach (var cat in cats)
                {
                    var positive = cat.Subs.SelectMany(s => s.Scenarios)
                        .Select(s => s.Tier).Where(v => v > 0)
                        .OrderByDescending(v => v).ToList();
                    plus = Math.Min(plus, positive.Count >= 3 ? positive[2] : 0);
                }
            }
            return new TierResult(Math.Max(b.Tier, plus), b.Progress);
        }

        private TierResult Tsk()
        {
            int? overNeeded, maxNeeded;
            int needed;
            switch (key)
            {
                case "beginner": overNeeded = null; maxNeeded = null; needed = 4; break;
                case "main": overNeeded = null; maxNeeded = null; needed = 8; break;
                case "ultimate": overNeeded = 8; maxNeeded = 10; needed = 12; break;
                case "static": overNeeded = null; maxNeeded = null; needed = 9; break;
                case "strafes": overNeeded = null; maxNeeded = null; needed = 7; break;
                case "thundah's bouncesphere": overNeeded = 2; maxNeeded = 2; needed = 4; break;
                case "reactive by slapped": overNeeded = 2; maxNeeded = 4; needed = 6; break;
                case "beginner classic": overNeeded = null; maxNeeded = null; needed = 5; break;
                case "main classic": overNeeded = null; maxNeeded = null; needed = 7; break;
                case "extra classic": overNeeded = 3; maxNeeded = 4; needed = 9; break;
                default: return Basic();
            }

            var pos = all.Select(PositionOf).ToList();
            int overMax = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i].TierThresholds;
                if (pos[i].Maxed && all[i].Score > t[^1]) overMax++;
            }
            var bases = pos.Where(p => p.Base > 0).Select(p => p.Base).ToList();
            int top = rankNames.Count > 0 ? rankNames.Count - 1 : all.Max(s => s.TierThresholds.Count);

            int Threshold(int k)
            {
                foreach (var r in bases.Distinct().OrderByDescending(v => v))
                    if (bases.Count(v => v >= r) >= k) return r;
                return 0;
            }
            double Next(int k, int m)
            {
                if (m == 0) return 0;
                if (k >= top) return 1;
                return Math.Min((double)bases.Count(v => v >= k + 1 && v <= top) / m, 1);
            }

            if (overNeeded.HasValue && overMax >= overNeeded.Value)
                return new TierResult(rankNames.Count > 0 ? rankNames.Count - 1 : 10, 1);

            if (maxNeeded.HasValue)
            {
                var maxed = new List<int>();
                for (int i = 0; i < all.Count; i++)
                {
                    int cnt = all[i].TierThresholds.Count;
                    if (cnt > 0 && pos[i].Base == cnt) maxed.Add(pos[i].Base);
                }
                if (maxed.Count >= maxNeeded.Value)
                {
                    int tier = maxed.Max();
                    return new TierResult(tier, tier >= top ? 1 : 0);
                }
            }

            int result = Threshold(needed);
            return new TierResult(result, result > 0 ? Next(result, needed) : 0);
        }

        private TierResult Xyz()
        {
            var groups = parts.Select(p => p.Scenarios.Select(s => s.Tier).ToList()).ToList();
            var flat = groups.SelectMany(g => g).ToList();
            int gMax = Math.Max(all.Max(s => s.TierThresholds.Count), tiers.Count);
            if (gMax <= 0 || n == 0) return Zero;
            if (flat.All(v => v == gMax)) return new TierResult(gMax, 1);

            int d = parts.Count;
            bool newcomer = key == "newcomer";
            int f = 0;
            for (int y = gMax; y >= 1; y--)
            {
                bool ok;
                if (newcomer)
                    ok = groups.All(g => g.Count(v => v >= y) >= 2) && flat.Count(v => v >= y) >= 2 * d;
                else
                {
                    int need = y == gMax ? 3 : 4;
                    ok = groups.All(g => g.Count(v => v >= y) >= need) && flat.Count(v => v >= y) >= need * d;
                }
                if (ok) { f = y; break; }
            }

            if (newcomer)
            {
                if (f >= gMax)
                {
                    int cnt = flat.Count(v => v >= gMax);
                    int v2 = Math.Min(2 * d, n);
                    int w = n - v2;
                    return new TierResult(gMax, w > 0 ? Clamp01((double)(cnt - v2) / w) : 1);
                }
                int y2 = f == 0 ? 1 : f + 1;
                int cap = 2 * d;
                double t = groups.Sum(g => Math.Min(g.Count(v => v >= y2), 2));
                return new TierResult(f, cap > 0 ? Clamp01(t / cap) : 0);
            }

            if (f >= gMax)
            {
                int cnt = flat.Count(v => v >= gMax);
                int cap = Math.Min(3 * d, n);
                int x = n - cap;
                return new TierResult(gMax, x > 0 ? Clamp01((double)(cnt - cap) / x) : 1);
            }
            int b = f == 0 ? 1 : f + 1;
            int needB = b == gMax ? 3 : 4;
            int total = needB * d;
            double have = groups.Sum(g => Math.Min(g.Count(v => v >= b), needB));
            return new TierResult(f, total > 0 ? Clamp01(have / total) : 0);
        }

        private TierResult Xyz2()
        {
            int g = key switch { "easy" => 12, "hard" => 10, _ => 0 };
            if (g == 0) return Zero;

            // Per category: positions of the ranked scenarios.
            var catPositions = cats
                .Select(c => c.Subs.SelectMany(s => s.Scenarios).Where(s => s.Tier > 0).Select(PositionOf).ToList())
                .ToList();

            int listed = catPositions.Sum(l => l.Count);
            if (n > 0 && listed == n && catPositions.All(l => l.All(p => p.Base == g)))
                return new TierResult(g, 1);
            if (catPositions.All(l => l.Count(p => p.Base == g) >= 3))
                return new TierResult(g, 1);
            if (catPositions.Any(l => l.Count < 4)) return Zero;

            int m = catPositions.Min(l => l.OrderByDescending(p => p.Base).ElementAt(3).Base);
            double sum = 0;
            foreach (var l in catPositions)
            {
                double s = 0;
                foreach (var p in l.OrderByDescending(p => p.Base).Take(4))
                    s += (p.Precise < m ? 0 : p.Precise >= m + 1 ? 1 : p.Precise - m) / 4;
                sum += s;
            }
            return new TierResult(m, Clamp01(sum / catPositions.Count));
        }

        private TierResult XyzSmoothnessV2()
        {
            int o = tiers.Count;
            if (n == 0 || o <= 0) return Zero;

            var ranks = new List<int>();
            foreach (var s in all)
            {
                if (s.Score <= 0) ranks.Add(0);
                else if (s.TierThresholds.Count > 0) ranks.Add(Math.Max(0, PositionOf(s).Base));
                else ranks.Add(Math.Max(0, s.Tier));
            }
            int C(int r) => ranks.Count(v => v >= r);

            int g = C(o);
            if (g >= n) return new TierResult(o, 1);
            if (g >= 6) return new TierResult(o, n - 6 > 0 ? Clamp01((double)(g - 6) / (n - 6)) : 1);

            int h = 0;
            for (int f = o - 1; f >= 1; f--)
                if (C(f) >= 9) { h = f; break; }
            int d = h + 1;
            int k = d >= o ? 6 : 9;
            int c = d <= o ? C(d) : 0;
            return new TierResult(h, Clamp01((double)c / k));
        }

        private TierResult Aoi()
        {
            var perSub = parts
                .Select(p => p.Scenarios.Where(s => s.Score > 0).Select(s => s.Tier).ToList())
                .ToList();
            var distinct = perSub.SelectMany(l => l).Where(v => v > 0).Distinct().OrderByDescending(v => v).ToList();

            int tier = 0;
            foreach (var i in distinct)
            {
                int one = perSub.Count(l => l.Count(v => v >= i) >= 1);
                int two = perSub.Count(l => l.Count(v => v >= i) >= 2);
                if (one >= 4 || two >= 3) { tier = i; break; }
            }

            double progress = 0;
            if (tier > 0 && distinct.Count > 0 && tier < distinct[0])
            {
                int i = tier + 1;
                int one = perSub.Count(l => l.Count(v => v >= i) >= 1);
                int two = perSub.Count(l => l.Count(v => v >= i) >= 2);
                progress = Math.Max(Math.Min(one / 4.0, 1), Math.Min(two / 3.0, 1));
            }
            return new TierResult(tier, progress);
        }

        /// <summary>The "真・葵Aim" rule: how many scenarios are ranked, against how many are required.</summary>
        private TierResult Aoi2()
        {
            int required = setup.Difficulty == "輝" ? 1 : n;
            int done = all.Count(s => s.Score > 0 && s.Tier > 0);
            return Climb(done, new double[] { required });
        }

        private TierResult CapAtTop(int tier, double progress)
        {
            int top = rankNames.Count > 0 ? rankNames.Count - 1 : 0;
            if (top > 0 && tier >= top) return new TierResult(top, 1);
            return new TierResult(tier, progress);
        }

        private TierResult Aimbeast()
        {
            bool played = false, unrankedAny = false;
            var catValues = new List<double>();
            foreach (var cat in cats)
            {
                bool unranked = false;
                double sum = 0;
                int count = 0;
                foreach (var s in cat.Subs.SelectMany(x => x.Scenarios))
                {
                    if (s.Score > 0) played = true;
                    int t = s.Score > 0 ? s.Tier : 0;
                    if (t <= 0) unranked = true;
                    else { sum += t; count++; }
                }
                double v = unranked || count == 0 ? 0 : sum / count;
                if (v <= 0) unrankedAny = true;
                catValues.Add(v);
            }

            double avg = Mean(catValues);
            bool valid = played && !unrankedAny;
            int tier = valid ? ToTier(Math.Floor(avg)) : 0;
            double progress = valid ? Clamp01(avg - tier) : 0;
            return CapAtTop(tier, progress);
        }

        private TierResult AimbeastPartial()
        {
            int needed = (int)Math.Ceiling(n / 2.0);
            double sum = 0;
            int count = 0;
            foreach (var cat in cats)
            {
                var precise = cat.Subs.SelectMany(x => x.Scenarios)
                    .Select(PositionOf).Where(p => p.Base > 0).Select(p => p.Precise).ToList();
                double mean = Mean(precise);
                if (mean > 0) { sum += mean; count++; }
            }
            int ranked = all.Count(s => PositionOf(s).Base > 0);
            bool enough = needed > 0 && ranked >= needed;
            double avg = count == 0 ? 0 : sum / count;
            int tier = enough ? ToTier(Math.Floor(avg)) : 0;
            double progress = enough ? Clamp01(avg - tier) : 0;
            return CapAtTop(tier, progress);
        }
    }
}
