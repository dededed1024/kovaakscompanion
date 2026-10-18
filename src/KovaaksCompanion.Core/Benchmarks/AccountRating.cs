using System.Globalization;

namespace KovaaksCompanion.Core.Benchmarks;

/// <summary>Account tier: its name, the estimated share of accounts at or above the account's current energy (null for Rookie and Super GM) and the energy it was read from.</summary>
public sealed record AccountRatingResult(string Tier, double? TopPercent, double Energy)
{
    /// <summary>The tier's color as "#RRGGBB".</summary>
    public string Color => AccountRating.ColorOf(Tier);

    /// <summary>"Top 4.3%", "Top 0.42%", "Top 0.017%"; empty for the lowest tier and Super GM.</summary>
    public string TopLabel => TopPercent is { } p ? $"Top {p.ToString(p < 0.1 ? "0.###" : p < 1 ? "0.##" : p < 10 ? "0.#" : "0", CultureInfo.InvariantCulture)}%" : "";
}

/// <summary>
/// Rates the account, not a single benchmark. Every Voltaic and Viscose result is put on Voltaic's energy scale; the higher of the two
/// sets the tier, and Super GM needs both at the Grandmaster level. The shares are estimates, not leaderboard data.
/// </summary>
public static class AccountRating
{
    /// <summary>
    /// Share of players (percent) at or above an energy, measured on the Voltaic S5 leaderboards: the rank of each tier's threshold score over the
    /// scenario's player count, median over 18 scenarios (energies up to 1200). Above 1200 the same decay continues. Energies up to 900 come from the
    /// Novice and Intermediate boards, so they are roughly right only.
    /// </summary>
    static readonly (double Energy, double Percent)[] Curve =
    [
        (0, 100), (300, 62), (500, 46), (700, 15), (900, 8.3), (1000, 2.2), (1100, 0.53), (1200, 0.14),
        (1250, 0.07), (1300, 0.037), (1360, 0.017), (1430, 0.0065),
    ];

    sealed record Step(string Tier, double TopPercent)
    {
        /// <summary>The energy where the measured share falls to <see cref="TopPercent"/>.</summary>
        public double Energy { get; } = EnergyAt(TopPercent);
    }

    /// <summary>Tiers by the share of players they reach; the energy cutoffs follow from the measured curve.</summary>
    static readonly Step[] Steps =
    [
        new("Novice", 50),
        new("Apprentice", 30),
        new("Intermediate", 15),
        new("Skilled", 7),
        new("Proficient", 3),
        new("Advanced", 1.5),
        new("Expert", 0.75),
        new("Master", 0.35),
        new("Grandmaster", 0.15),
    ];

    /// <summary>The energy where the share is <paramref name="percent"/>: the inverse of <see cref="ShareAt"/>.</summary>
    static double EnergyAt(double percent)
    {
        var i = Array.FindLastIndex(Curve, c => c.Percent >= percent);
        if (i < 0) return 0;
        if (i >= Curve.Length - 1) return Curve[^1].Energy;
        var (e0, p0) = Curve[i];
        var (e1, p1) = Curve[i + 1];
        return Math.Round(e0 + (e1 - e0) * Math.Log(percent / p0) / Math.Log(p1 / p0));
    }

    /// <summary>The share at an energy: steady in log scale between two curve points, flat beyond the last.</summary>
    static double ShareAt(double energy)
    {
        var i = Array.FindLastIndex(Curve, c => energy >= c.Energy);
        if (i >= Curve.Length - 1) return Curve[^1].Percent;
        var (e0, p0) = Curve[i];
        var (e1, p1) = Curve[i + 1];
        return p0 * Math.Pow(p1 / p0, (energy - e0) / (e1 - e0));
    }

    static readonly Dictionary<string, string> TierColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Super GM"] = "#FF0000", ["Grandmaster"] = "#3BDDE6", ["Master"] = "#DD43DA", ["Expert"] = "#6D39DD", ["Proficient"] = "#4354EC",
        ["Skilled"] = "#39D489", ["Advanced"] = "#76C548", ["Intermediate"] = "#CEBE2D", ["Apprentice"] = "#A79B6F", ["Novice"] = "#9B8E7C",
        ["Rookie"] = "#7A6C6C",
    };

    /// <summary>Tier names from the lowest to the highest ("Rookie" first, "Super GM" last).</summary>
    public static IReadOnlyList<string> Order { get; } = ["Rookie", .. Steps.Select(s => s.Tier), "Super GM"];

    /// <summary>A tier's share in a few words: "Top 4%"; "Voltaic and Viscose GM" for Super GM; empty for the lowest tier.</summary>
    public static string Requirement(string tier)
    {
        if (tier == SuperGm) return "Voltaic and Viscose GM";
        if (tier == Order[0]) return "";
        return $"Top {Steps.First(s => s.Tier == tier).TopPercent.ToString("0.###", CultureInfo.InvariantCulture)}%";
    }

    public static string ColorOf(string tier) => TierColors.GetValueOrDefault(tier, "#7A6C6C");

    const string SuperGm = "Super GM";

    /// <summary>Energy of a tier name: Voltaic's own steps, and Viscose's tiers read as the Voltaic rank they correspond to.</summary>
    static readonly Dictionary<string, double> TierEnergy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Iron"] = 100, ["Bronze"] = 200, ["Silver"] = 300, ["Gold"] = 400, ["Platinum"] = 500, ["Diamond"] = 600, ["Jade"] = 700,
        ["Master"] = 800, ["Grandmaster"] = 900, ["Nova"] = 1000, ["Astra"] = 1100, ["Celestial"] = 1200,

        ["Lemming"] = 50, ["Hare"] = 50, ["Ermine"] = 100, ["Puffin"] = 150, ["Penguin"] = 200, ["Fox"] = 300, ["Mammoth"] = 400,
        ["Orca"] = 500, ["Seal"] = 650,
        ["Cinnabar"] = 400, ["Vermillion"] = 450, ["Saffron"] = 550, ["Celadon"] = 650, ["Viridian"] = 500, ["Cerulean"] = 750,
        ["Lavender"] = 850, ["Indigo"] = 950, ["Fuchsia"] = 1000,
        ["Wool"] = 890, ["Rayon"] = 930, ["Linen"] = 980, ["Velvet"] = 1010, ["Chiffon"] = 1080, ["Tricot"] = 1090, ["Satin"] = 1100, ["Silk"] = 1180,
        ["Interloper"] = 1150, ["Attuned"] = 1200, ["Heroic"] = 1250, ["Mythic"] = 1300, ["Ascension"] = 1360, ["Eclipse"] = 1430,
    };

    /// <summary>
    /// The account's tier from every benchmark difficulty's progress. Null when no Voltaic or Viscose progress exists to rate
    /// (nothing loaded yet); the lowest tier is "Rookie" with no share.
    /// </summary>
    public static AccountRatingResult? Compute(IEnumerable<(string Benchmark, BenchmarkProgress Progress)> progress)
    {
        double voltaic = 0, viscose = 0;
        var any = false;
        foreach (var (name, p) in progress)
        {
            var voltaicSeason = name.StartsWith("Voltaic", StringComparison.OrdinalIgnoreCase);
            var viscoseSeason = name.StartsWith("Viscose", StringComparison.OrdinalIgnoreCase) && !name.Contains("Entry", StringComparison.OrdinalIgnoreCase);
            if (!voltaicSeason && !viscoseSeason) continue;
            if (p.Setup is { Difficulty: var d } && d.Contains("unofficial", StringComparison.OrdinalIgnoreCase)) continue;
            any = true;
            var e = p.Setup is { Method: "vt-energy" } setup && TierCalculationEngine.VoltaicEnergy(setup, p.TierNames, p.Subcategories) is { } v
                ? v
                : TierEnergy.GetValueOrDefault(p.OverallTierName);
            if (voltaicSeason) voltaic = Math.Max(voltaic, e); else viscose = Math.Max(viscose, e);
        }
        if (!any) return null;

        var best = Math.Max(voltaic, viscose);
        var top = Steps[^1].Energy;
        if (voltaic >= top && viscose >= top) return new(SuperGm, null, best);
        var at = Array.FindLastIndex(Steps, s => best >= s.Energy);
        if (at < 0) return new("Rookie", null, best);
        return new(Steps[at].Tier, ShareAt(best), best);
    }
}
