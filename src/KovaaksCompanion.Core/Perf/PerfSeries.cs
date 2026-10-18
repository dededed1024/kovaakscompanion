namespace KovaaksCompanion.Core.Perf;

/// <summary>Running totals from the run start. <see cref="DamageEff"/> is NaN until some damage was possible; <see cref="Reloads"/> and <see cref="Distance"/> are NaN where the points do not come from per-second data.</summary>
public readonly record struct PerfPoint(double TSec, double Accuracy, double Score, int Kills, int Shots, int Hits, double DamageEff, double Reloads = double.NaN, double Distance = double.NaN);

public static class PerfSeries
{
    /// <summary>One point per bucket.</summary>
    public static IReadOnlyList<PerfPoint> Build(PerfData perf)
    {
        var b = perf.Buckets;
        var res = new List<PerfPoint>(b.Count);
        int shots = 0, hits = 0, kills = 0;
        double score = 0, dmg = 0, possible = 0, distance = 0;
        var reloads = 0;
        foreach (var k in b)
        {
            shots += k.Shots; hits += k.Hits; kills += k.Kills; score += k.Score; dmg += k.DamageDone; possible += k.DamagePossible; reloads += k.Reloads; distance += k.Distance;
            res.Add(new PerfPoint(k.TSec, shots == 0 ? 0 : (double)hits / shots, score, kills, shots, hits, possible > 0 ? dmg / possible : double.NaN, reloads, distance));
        }
        return res;
    }
}
