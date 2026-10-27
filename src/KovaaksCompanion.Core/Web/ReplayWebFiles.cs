using System.Text;
using System.Text.Json;
using KovaaksCompanion.Core.Replays;

namespace KovaaksCompanion.Core.Web;

/// <summary>
/// The files the browser userscript works from, kept in the cache folder (all of it can be rebuilt from the archive): the list of replays that have a video
/// and the userscript itself. The userscript manager reads them straight from disk, so playback needs neither the app running nor a server.
/// </summary>
public static class ReplayWebFiles
{
    public const string IndexName = "replays.js";
    public const string ScriptName = "replay-player.user.js";

    /// <summary>The websites the userscript runs on; they become its <c>@match</c> lines.</summary>
    static readonly string[] Sites = ["https://evxl.app", "https://www.evxl.app", "https://app.voltaic.gg"];

    public static string FolderOf(AppSettings settings) => Path.Combine(settings.CacheFolder, "web");

    /// <summary>Rewrites the list of replays that have a video (a list nobody changed is left alone).</summary>
    public static void Write(ReplayArchive archive, string folder)
    {
        Directory.CreateDirectory(folder);
        WriteIfChanged(Path.Combine(folder, IndexName), Index(archive));
    }

    /// <summary>Writes the userscript (with this folder's address in it) and returns its path.</summary>
    public static string WriteScript(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ScriptName);
        var text = Resource(ScriptName)
            .Replace("__WEB_DIR__", new Uri(folder).AbsoluteUri.TrimEnd('/'))
            .Replace("__MATCHES__", string.Join("\n// @match        ", Sites.Select(s => s + "/*")));
        WriteIfChanged(path, text);
        return path;
    }

    /// <summary>The list as <c>window.KC_REPLAYS = [...]</c>; the userscript reads the file's text as a resource and takes the array out of it.</summary>
    static string Index(ReplayArchive archive)
    {
        var items = archive.List()
            .Select(i => (Info: i, Path: archive.VideoPath(i)))
            .Where(x => x.Path is not null)
            .Select(x => new { id = x.Info.Id, scenario = x.Info.Scenario, score = x.Info.Score, end = x.Info.End.ToString("s"), video = new Uri(x.Path!).AbsoluteUri });
        return "window.KC_REPLAYS=" + JsonSerializer.Serialize(items) + ";\n";
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
