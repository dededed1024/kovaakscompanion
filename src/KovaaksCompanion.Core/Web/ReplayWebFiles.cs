using System.Text;
using System.Text.Json;
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
    public static void Write(ReplayArchive archive, string folder)
    {
        Directory.CreateDirectory(folder);
        WriteIfChanged(Path.Combine(folder, IndexName), Index(archive));
        WriteIfChanged(ScriptPath(folder), Resource(ScriptName)
            .Replace("__WEB_DIR__", new Uri(folder).AbsoluteUri.TrimEnd('/'))
            .Replace("__MATCHES__", string.Join("\n// @match        ", Sites.Select(s => s + "/*"))));
    }

    /// <summary>
    /// The list as <c>window.KC_REPLAYS = {...}</c>; the userscript reads the file's text as a resource and takes the object out of it.
    /// <c>dir</c> is the videos folder the user picks in the browser, and each <c>video</c> is a path inside it with forward slashes.
    /// </summary>
    static string Index(ReplayArchive archive)
    {
        var items = archive.List()
            .Where(i => archive.VideoPath(i) is not null)
            .Select(i => new { id = i.Id, scenario = i.Scenario, score = i.Score, end = i.End.ToString("s"), video = i.VideoFile!.Replace('\\', '/') });
        return "window.KC_REPLAYS=" + JsonSerializer.Serialize(new { dir = archive.VideoFolder, items }) + ";\n";
    }

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
