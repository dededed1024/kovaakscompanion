using System.Text;
using System.Text.Json;
using KovaaksCompanion.Core.Library;
using KovaaksCompanion.Core.Replays;

namespace KovaaksCompanion.Core.Web;

/// <summary>
/// The files the browser userscript works from, kept in the cache folder (all of it can be rebuilt from the archive): the list of replays that have a video
/// and the userscript itself. The userscript manager reads them straight from disk, and the page reads the clips from a videos folder the user picks in the
/// browser, so playback needs neither the app running nor a server.
/// </summary>
public static class ReplayWebFiles
{
    public const string IndexName = "replays.js";
    public const string ScriptName = "replay-player.user.js";

    /// <summary>The websites the userscript runs on; they become its <c>@match</c> lines.</summary>
    static readonly string[] Sites = ["https://evxl.app", "https://www.evxl.app", "https://app.voltaic.gg"];

    public static string FolderOf(AppSettings settings) => Path.Combine(settings.CacheFolder, "web");

    public static string ScriptPath(string folder) => Path.Combine(folder, ScriptName);

    /// <summary>Rewrites the list of replays that have a video and the userscript (with this folder's address in it); a file nobody changed is left alone.</summary>
    public static void Write(ReplayArchive archive, string folder, string dataRoot, IReadOnlyList<RunRecord> runs)
    {
        Directory.CreateDirectory(folder);
        WriteIfChanged(Path.Combine(folder, IndexName), Index(archive, dataRoot, runs));
        WriteIfChanged(ScriptPath(folder), Resource(ScriptName)
            .Replace("__WEB_DIR__", new Uri(folder).AbsoluteUri.TrimEnd('/'))
            .Replace("__MATCHES__", string.Join("\n// @match        ", Sites.Select(s => s + "/*"))));
        ReplayWebStats.Write(archive, dataRoot);
    }

    /// <summary>
    /// The list as <c>window.KC_REPLAYS = {...}</c>; the userscript reads the file's text as a resource and takes the object out of it.
    /// <c>dir</c> is the data folder the user picks in the browser (the videos and <see cref="ReplayWebStats"/> files are inside it), and each <c>video</c> is a path inside it with forward slashes.
    /// </summary>
    static string Index(ReplayArchive archive, string dataRoot, IReadOnlyList<RunRecord> runs)
    {
        var items = archive.List()
            .Select(i => (Info: i, Path: archive.VideoPath(i)))
            .Where(x => x.Path is not null)
            .Select(x => new { id = x.Info.Id, scenario = x.Info.Scenario, score = x.Info.Score, end = x.Info.End.ToString("s"), video = System.IO.Path.GetRelativePath(dataRoot, x.Path!).Replace('\\', '/') });
        return "window.KC_REPLAYS=" + JsonSerializer.Serialize(new { dir = dataRoot, items, runs = Runs(runs) }) + ";\n";
    }

    /// <summary>
    /// Every local run per scenario (lower-case name), oldest first, one row per run: end (Unix seconds), score, accuracy, kills, average TTK, overshots, reloads, distance, cm/360, seconds played, shots, mouse speed (see <see cref="SpeedRow"/>).
    /// A figure the run does not have is null. The popup's averages, rank and score history come from these, so they cover runs whose video is gone too.
    /// </summary>
    static Dictionary<string, object?[][]> Runs(IReadOnlyList<RunRecord> runs) => runs
        .GroupBy(r => r.Scenario.ToLowerInvariant())
        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.End).Select(r => new object?[]
        {
            new DateTimeOffset(r.End).ToUnixTimeSeconds(), Round(r.Score, 2), Round(r.Accuracy, 4), r.Kills, Round(r.AvgTtk, 3), r.OverShots, r.Reloads, Round(r.Distance, 2), Round(r.Cm360, 2),
            Round(r.Duration.TotalSeconds, 1), r.Shots, SpeedRow(r.Speed),
        }).ToArray());

    /// <summary>The mouse-speed figures of a run in a fixed order (see the userscript's SPEED list); null when it has none.</summary>
    static double?[]? SpeedRow(Trajectory.RunSpeed? s) => s is null ? null :
    [
        Round(s.Mean, 2), Round(s.Max, 2), Round(s.FireSpeed, 2), Round(s.IdleSpeed, 2), Round(s.KillInterval, 3), Round(s.HoldRatio, 3), Round(s.ReversalsPerSec, 2), Round(s.SpeedCv, 3), Round(s.Jerk, 1),
        Round(s.AccelStd, 1), Round(s.FlickPeak, 2), Round(s.FlickTime, 3), Round(s.StopToClick, 3), Round(s.Reaction, 3), Round(s.Efficiency, 3), Round(s.BallisticRatio, 3), Round(s.FlickDistance, 2),
    ];

    static double? Round(double? v, int digits) => v is { } x && double.IsFinite(x) ? Math.Round(x, digits) : null;

    static string Resource(string name)
    {
        using var s = typeof(ReplayWebFiles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name + " is not embedded");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    static void WriteIfChanged(string path, string text)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        AtomicFile.Write(path, bytes);
    }
}
