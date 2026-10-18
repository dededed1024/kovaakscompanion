using KovaaksCompanion.Core.Trajectory;

namespace KovaaksCompanion.Core.Benchmarks;

/// <summary>The aim kind (tracking, flick, switching) of each scenario of the bundled playlists, read from the name of the category it sits in, else from the name of the playlist.</summary>
public static class ScenarioKinds
{
    static readonly string[] TrackingWords = ["track", "pasu", "smooth", "strafe", "跟枪", "genqiang"];
    static readonly string[] SwitchingWords = ["switch", "swithing", "regen", "转火", "zhuanhuo"];
    static readonly string[] FlickWords = ["click", "flick", "1wall", "1 wall", "wide wall", "widewall"];

    /// <summary>The kind a category or playlist name says; null when it says none. Tracking wins over switching over flick, so "Reactive Tracking" and "Control: MicroTrack" are tracking.</summary>
    public static AimKind? FromText(string text)
    {
        var t = text.ToLowerInvariant();
        if (TrackingWords.Any(t.Contains)) return AimKind.Tracking;
        if (SwitchingWords.Any(t.Contains)) return AimKind.Switching;
        if (FlickWords.Any(t.Contains)) return AimKind.Flick;
        return null;
    }

    static readonly Lazy<Dictionary<string, AimKind>> Map = new(Build);

    static Dictionary<string, AimKind> Build()
    {
        var votes = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in BenchmarkCatalog.All)
            foreach (var d in b.Difficulties)
            {
                if (TierData.Get(d.KovaaksBenchmarkId) is not { } data) continue;
                var fromPlaylist = FromText(b.BenchmarkName);
                foreach (var c in data.Categories)
                {
                    if ((FromText(c.Name) ?? fromPlaylist) is not { } kind) continue;
                    foreach (var s in c.Scenarios)
                    {
                        if (s.Name.Length == 0) continue;
                        if (!votes.TryGetValue(s.Name, out var v)) votes[s.Name] = v = new int[3];
                        v[(int)kind]++;
                    }
                }
            }
        return votes.ToDictionary(p => p.Key, p => (AimKind)Array.IndexOf(p.Value, p.Value.Max()), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The kind of a scenario from its playlists; null when no playlist category or playlist name says.</summary>
    public static AimKind? Of(string scenario) => Map.Value.TryGetValue(scenario, out var k) ? k : null;
}
