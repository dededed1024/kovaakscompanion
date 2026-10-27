using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KovaaksCompanion.Core.Diagnostics;
using KovaaksCompanion.Core.Library;
using KovaaksCompanion.Core.Replays;

namespace KovaaksCompanion.Core.Web;

/// <summary>
/// What the browser popup shows about a whole scenario (its overview): the totals, the score history with the figures drawn over it, the score by sensitivity and the advice.
/// One file per scenario that has a replay with a video, <c>&lt;data root&gt;\cache\scenario-&lt;16 hex of the lower-case name's SHA-1&gt;.cache</c> (JSON text), built from the same
/// <see cref="ScoreBook"/> the app's overview uses, so the numbers and the advice are the app's own. A file is rewritten when its content changes; one whose scenario has no video any more is deleted.
/// </summary>
public static partial class ReplayWebOverview
{
    public const int Version = 2;

    [GeneratedRegex("^scenario-[0-9a-f]{16}\\.cache$")]
    private static partial Regex OwnFile();

    /// <summary>The file name of a scenario's overview; the userscript derives the same name.</summary>
    public static string NameOf(string scenario) =>
        "scenario-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(scenario.ToLowerInvariant())))[..16].ToLowerInvariant() + CacheFile.Extension;

    public static void Write(ReplayArchive archive, ScoreBook book, string dataRoot)
    {
        var folder = Path.Combine(dataRoot, ReplayWebStats.FolderName);
        try
        {
            Directory.CreateDirectory(folder);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scenarios = archive.List().Where(i => archive.VideoPath(i) is not null).Select(i => i.Scenario).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var scenario in scenarios)
            {
                var path = Path.Combine(folder, NameOf(scenario));
                keep.Add(path);
                var bytes = Build(book.For(scenario));
                if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
                AtomicFile.Write(path, bytes);
            }
            foreach (var file in Directory.EnumerateFiles(folder, "scenario-*" + CacheFile.Extension))
                if (OwnFile().IsMatch(Path.GetFileName(file)) && !keep.Contains(file)) File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Write("web", "writing the scenario overviews failed: " + e.Message); }
    }

    static byte[] Build(ScenarioHistory s)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            var summary = ScenarioInsights.Summarize(s.Records);
            var runSet = RunSetInsights.Summarize(s.Runs, new HashSet<RunRecord>());
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteString("scenario", s.Scenario);
            w.WriteNumber("plays", s.Plays);
            w.WriteNumber("time", Math.Round(summary?.TimePlayed.TotalSeconds ?? 0));
            Num(w, "best", s.Best > 0 ? s.Best : null, 2);
            w.WriteNumber("pbs", s.PersonalBests.Count);
            Num(w, "accuracy", runSet?.Accuracy, 4);
            Num(w, "ttk", runSet?.AvgTtk, 3);
            Records(w, s);
            Sensitivity(w, s);
            Advice(w, s);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>The score history, oldest first, as parallel arrays; the other figures are null for a score that has no local run (a cloud score).</summary>
    static void Records(Utf8JsonWriter w, ScenarioHistory s)
    {
        var recs = s.Records;
        w.WriteStartObject("records");
        Series(w, "t", recs.Select(r => (double?)new DateTimeOffset(r.Time).ToUnixTimeSeconds()), 0);
        Series(w, "score", recs.Select(r => (double?)r.Score), 2);
        Series(w, "acc", recs.Select(r => r.Run?.Accuracy), 4);
        Series(w, "kills", recs.Select(r => (double?)r.Run?.Kills), 0);
        Series(w, "ttk", recs.Select(r => r.Run?.AvgTtk), 3);
        Series(w, "over", recs.Select(r => (double?)r.Run?.OverShots), 0);
        Series(w, "reloads", recs.Select(r => (double?)r.Run?.Reloads), 0);
        Series(w, "dist", recs.Select(r => r.Run?.Distance), 2);
        Series(w, "speed", recs.Select(r => r.Run is { Speed: { } sp, Cm360: > 0 and var cm } ? sp.Mean * cm / 360 : (double?)null), 2); // cm/s
        Series(w, "reaction", recs.Select(r => r.Run?.Speed?.Reaction * 1000), 0); // ms
        w.WriteEndObject();
    }

    /// <summary>The sensitivities played most (best score first) with every run of them as a point.</summary>
    static void Sensitivity(Utf8JsonWriter w, ScenarioHistory s)
    {
        var groups = ScenarioInsights.BySensitivity(s.Runs);
        if (groups.Count == 0) return;
        w.WriteStartObject("sens");
        w.WriteStartArray("groups");
        foreach (var g in groups)
        {
            w.WriteStartObject();
            w.WriteNumber("cm", g.Cm360); w.WriteNumber("runs", g.Runs); w.WriteNumber("avg", Math.Round(g.Average, 2)); w.WriteNumber("best", Math.Round(g.Best, 2));
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("points"); // [group, cm/360, score, end]
        foreach (var r in s.Runs.Where(r => r.Cm360 is > 0))
        {
            var gi = groups.FindIndex(g => Math.Abs(g.Cm360 - Math.Round(r.Cm360!.Value, 2)) < 1e-9);
            if (gi < 0) continue;
            w.WriteStartArray();
            w.WriteNumberValue(gi); w.WriteNumberValue(Math.Round(r.Cm360!.Value, 2)); w.WriteNumberValue(Math.Round(r.Score, 2)); w.WriteNumberValue(new DateTimeOffset(r.End).ToUnixTimeSeconds());
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>The advice paragraphs as format string (<c>f</c>, <c>**x**</c> marks the bold parts) and figures (<c>a</c>); the popup words them in the browser's language.</summary>
    static void Advice(Utf8JsonWriter w, ScenarioHistory s)
    {
        var speeds = s.Runs.Where(r => r.Speed != null).Select(r => (r.End, r.Score, Speed: r.Speed! with { Cm360 = r.Cm360 })).ToList();
        var advice = ScenarioAdvice.Build(s, speeds);
        w.WriteStartArray("advice");
        foreach (var a in advice)
        {
            w.WriteStartObject();
            w.WriteString("f", a.Format);
            w.WriteStartArray("a");
            foreach (var x in a.Args) w.WriteNumberValue(Convert.ToDouble(x, CultureInfo.InvariantCulture));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    static void Series(Utf8JsonWriter w, string name, IEnumerable<double?> values, int digits)
    {
        w.WriteStartArray(name);
        foreach (var v in values) { if (v is { } x && double.IsFinite(x)) w.WriteNumberValue(Math.Round(x, digits)); else w.WriteNullValue(); }
        w.WriteEndArray();
    }

    static void Num(Utf8JsonWriter w, string name, double? v, int digits)
    {
        if (v is { } x && double.IsFinite(x)) w.WriteNumber(name, Math.Round(x, digits));
    }
}
