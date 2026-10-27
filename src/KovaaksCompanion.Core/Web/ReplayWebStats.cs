using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KovaaksCompanion.Core.Diagnostics;
using KovaaksCompanion.Core.Library;
using KovaaksCompanion.Core.Replays;
using KovaaksCompanion.Core.Trajectory;

namespace KovaaksCompanion.Core.Web;

/// <summary>
/// What the browser popup shows besides the video, one file per replay with a video: <c>&lt;data root&gt;\cache\&lt;replay id&gt;.cache</c> (JSON text). The popup reads it through the
/// folder the user picked in the browser, which holds the videos too. All of it can be rebuilt from the archive, so it is cache: a file of an older
/// <see cref="Version"/> or one that is gone is written again, and one whose replay lost its video is deleted.
/// </summary>
public static partial class ReplayWebStats
{
    /// <summary>Raise when the file's content changes; files of another version are rewritten.</summary>
    public const int Version = 3;
    public const string FolderName = "cache";

    /// <summary>Rate the camera path is written at, in samples per second.</summary>
    const double TrajectoryHz = 100;

    static readonly byte[] Prefix = Encoding.ASCII.GetBytes("{\"v\":" + Version + ",");

    [GeneratedRegex("^[0-9a-f]{32}\\.cache$")]
    private static partial Regex OwnFile();

    public static string PathOf(string dataRoot, Guid id) => Path.Combine(dataRoot, FolderName, id.ToString("N") + CacheFile.Extension);

    /// <summary>Writes the files that are missing or of another version, and deletes the ones whose replay has no video any more.</summary>
    public static void Write(ReplayArchive archive, string dataRoot)
    {
        var folder = Path.Combine(dataRoot, FolderName);
        try
        {
            Directory.CreateDirectory(folder);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in archive.List())
            {
                if (archive.VideoPath(info) is null) continue;
                var path = PathOf(dataRoot, info.Id);
                keep.Add(path);
                if (IsCurrent(path)) continue;
                try { AtomicFile.Write(path, Build(archive, info)); }
                catch (Exception e) when (e is ReplayFormatException or TrajectoryFormatException or FormatException or ArgumentException)
                { AppLog.Write("web", $"statistics of {info.Id:N} not written: {e.Message}"); }
            }
            foreach (var file in Directory.EnumerateFiles(folder, "*" + CacheFile.Extension))
                if (OwnFile().IsMatch(Path.GetFileName(file)) && !keep.Contains(file)) File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Write("web", "writing the statistics failed: " + e.Message); }
    }

    static bool IsCurrent(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[Prefix.Length];
            return f.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(Prefix);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static byte[] Build(ReplayArchive archive, ReplayInfo info)
    {
        var parts = archive.ReadParts(info.Id);
        var loaded = ReplayStore.Open(info, parts, null);
        var record = ReplayStore.ReadRecord(info, parts);

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteString("id", info.Id.ToString());
            w.WriteNumber("duration", Math.Round((info.End - info.Start).TotalSeconds, 1));
            w.WriteNumber("offset", Math.Round(info.VideoTime(0), 3)); // video seconds = run seconds + offset
            if (record is not null) Summary(w, record, loaded);
            if (record?.Speed is { } speed) Speed(w, speed);
            if (parts.Perf is { } perf) PerSecond(w, perf, loaded.Trajectory);
            if (loaded.Stats is { KillEvents.Count: > 0 } stats) Kills(w, stats);
            if (loaded.Trajectory is { } traj) Camera(w, loaded, traj);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    static void Summary(Utf8JsonWriter w, RunRecord r, LoadedReplay loaded)
    {
        w.WriteStartObject("summary");
        Num(w, "score", r.Score);
        Num(w, "accuracy", r.Accuracy);
        Int(w, "kills", r.Kills);
        Int(w, "shots", r.Shots);
        if (loaded.Stats is { } s) Int(w, "hits", s.HitCount);
        Num(w, "avgTtk", r.AvgTtk, 3);
        Int(w, "overshots", r.OverShots);
        Int(w, "reloads", r.Reloads);
        Num(w, "distance", r.Distance, 1);
        Num(w, "cm360", r.Cm360);
        Int(w, "dpi", r.Dpi > 0 ? r.Dpi : null);
        if (loaded.Stats is { } st) Num(w, "fov", st.Settings.Fov > 0 ? st.Settings.Fov : null);
        w.WriteEndObject();
    }

    static void Speed(Utf8JsonWriter w, RunSpeed s)
    {
        w.WriteStartObject("speed");
        Num(w, "mean", s.Mean); Num(w, "peak", s.Peak); Num(w, "max", s.Max);
        Num(w, "fire", s.FireSpeed); Num(w, "idle", s.IdleSpeed);
        Num(w, "hold", s.HoldRatio, 3); Num(w, "reversals", s.ReversalsPerSec);
        Num(w, "killInterval", s.KillInterval, 3); Num(w, "flickDistance", s.FlickDistance); Num(w, "flickPeak", s.FlickPeak); Num(w, "flickTime", s.FlickTime, 3);
        Num(w, "reaction", s.Reaction, 3); Num(w, "efficiency", s.Efficiency, 3);
        Num(w, "jerk", s.Jerk); Num(w, "speedCv", s.SpeedCv, 3); Num(w, "accelStd", s.AccelStd); Num(w, "ballistic", s.BallisticRatio, 3); Num(w, "stopToClick", s.StopToClick, 3);
        w.WriteEndObject();
    }

    /// <summary>The per-second data as parallel arrays (one entry per second of the run).</summary>
    static void PerSecond(Utf8JsonWriter w, Perf.PerfData perf, TrajectoryData? trajectory)
    {
        var b = perf.Buckets;
        if (b.Count == 0) return;
        w.WriteStartObject("perf");
        Series(w, "t", b.Select(k => k.TSec), 1);
        Series(w, "shots", b.Select(k => (double)k.Shots), 0);
        Series(w, "hits", b.Select(k => (double)k.Hits), 0);
        Series(w, "kills", b.Select(k => (double)k.Kills), 0);
        Series(w, "score", b.Select(k => k.Score), 1);
        Series(w, "reloads", b.Select(k => (double)k.Reloads), 0);
        Series(w, "distance", b.Select(k => k.Distance), 2);
        if (trajectory is not null) Series(w, "speed", MouseSpeedStats.Averages(trajectory, b.Select(k => k.TSec).ToList()), 1); // deg/s over each second
        w.WriteEndObject();
    }

    static void Kills(Utf8JsonWriter w, Stats.RunStats stats)
    {
        w.WriteStartObject("kills");
        Series(w, "t", stats.KillEvents.Select(k => (k.Time - stats.Start).TotalSeconds), 2);
        Series(w, "ttk", stats.KillEvents.Select(k => k.Ttk.TotalSeconds), 3);
        Series(w, "shots", stats.KillEvents.Select(k => (double)k.Shots), 0);
        Series(w, "hits", stats.KillEvents.Select(k => (double)k.Hits), 0);
        Series(w, "over", stats.KillEvents.Select(k => (double)k.OverShots), 0);
        w.WriteEndObject();
    }

    /// <summary>
    /// What the trail over the video needs: the camera path at a fixed rate (the raw 1 kHz movement would make every file large), the left button's fire / release events and the kills,
    /// the outcome of every shot (<see cref="ShotClassifier"/>), the accuracy while the button is held (<see cref="HeldAccuracy"/>, 20 per second) and the projection (horizontal field of view and aspect).
    /// The popup only draws these, so the derivations stay in one place.
    /// </summary>
    static void Camera(Utf8JsonWriter w, LoadedReplay loaded, TrajectoryData t)
    {
        var s = t.Samples;
        if (s.Count < 2 || !(s[^1].TSec > s[0].TSec)) return;
        var n = (int)Math.Floor((s[^1].TSec - s[0].TSec) * TrajectoryHz) + 1;
        var yaw = new double[n];
        var pitch = new double[n];
        for (var i = 0; i < n; i++)
        {
            var d = CameraPath.At(s, s[0].TSec + i / TrajectoryHz);
            yaw[i] = d.YawDeg;
            pitch[i] = d.PitchDeg;
        }
        var length = (loaded.Info.End - loaded.Info.Start).TotalSeconds;
        w.WriteStartObject("trajectory");
        w.WriteNumber("hz", TrajectoryHz);
        w.WriteNumber("t0", Math.Round(s[0].TSec, 3));
        Series(w, "yaw", yaw, 2);
        Series(w, "pitch", pitch, 2);

        w.WriteStartArray("events"); // [time, 0 fire | 1 release | 2 kill]
        foreach (var e in t.Events.Where(e => e.Type == TrajectoryEventType.Kill || e.Button == 0))
        {
            w.WriteStartArray();
            w.WriteNumberValue(Math.Round(e.TSec, 3));
            w.WriteNumberValue((int)e.Type);
            w.WriteEndArray();
        }
        w.WriteEndArray();

        if (loaded.Stats is { } stats)
        {
            var kills = stats.KillEvents.Select(k => new KillShots((k.Time - stats.Start).TotalSeconds, k.Shots, k.Hits)).ToList();
            var outcomes = ShotClassifier.Classify(t.Events, kills, loaded.Perf);
            w.WriteStartArray("fires"); // [time, 0 unknown | 1 hit | 2 miss]
            foreach (var e in t.Events.Where(e => e.Type == TrajectoryEventType.Fire && e.Button == 0))
            {
                w.WriteStartArray();
                w.WriteNumberValue(Math.Round(e.TSec, 3));
                w.WriteNumberValue(outcomes.TryGetValue(e.TSec, out var o) ? (int)o : 0);
                w.WriteEndArray();
            }
            w.WriteEndArray();

            if (stats.Settings.Fov > 0)
            {
                var aspect = ViewProjection.ParseAspect(stats.Settings.Resolution) ?? 16d / 9;
                w.WriteStartObject("view");
                w.WriteNumber("hfov", Math.Round(ViewProjection.HorizontalFovDeg(stats.Settings.Fov, stats.Settings.FovScale, aspect), 3));
                w.WriteNumber("aspect", Math.Round(aspect, 4));
                w.WriteEndObject();
            }
        }

        var held = new HeldAccuracy(t.Events, loaded.Perf, length);
        var heldHz = 20;
        var steps = (int)Math.Floor(length * heldHz) + 1;
        w.WriteStartArray("held"); // accuracy while the button is held, null otherwise; one value per 1/20 s from the run start
        for (var i = 0; i < steps; i++)
        {
            if (held.AccuracyAt(i / (double)heldHz) is { } a) w.WriteNumberValue(Math.Round(a, 2)); else w.WriteNullValue();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    static void Series(Utf8JsonWriter w, string name, IEnumerable<double> values, int digits)
    {
        w.WriteStartArray(name);
        foreach (var v in values) { if (double.IsFinite(v)) w.WriteNumberValue(Math.Round(v, digits)); else w.WriteNullValue(); }
        w.WriteEndArray();
    }

    static void Num(Utf8JsonWriter w, string name, double? v, int digits = 2)
    {
        if (v is { } x && double.IsFinite(x)) w.WriteNumber(name, Math.Round(x, digits));
    }

    static void Int(Utf8JsonWriter w, string name, int? v)
    {
        if (v is { } x) w.WriteNumber(name, x);
    }
}
