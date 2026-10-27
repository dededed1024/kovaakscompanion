using KovaaksCompanion.Core.Benchmarks;
using KovaaksCompanion.Core.Diagnostics;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace KovaaksCompanion.Core;

/// <summary>"Start with KovaaK's": the app puts itself in the Steam launch options of KovaaK's, so Steam starts it together with the game.
/// The option lives in userdata\&lt;account&gt;\config\localconfig.vdf, a file Steam rewrites on exit, so it is only edited while Steam is closed.</summary>
public static partial class SteamLaunchOption
{
    public const string AppId = "824270";

    public enum Result { Done, SteamRunning, NotFound }

    /// <summary>Argument after which the app finds the game's command line (Steam replaces %command% with it).</summary>
    public const string GameArg = "--with-game";

    /// <summary>The option text: the app starts the game itself and the tray instance beside it. No cmd, so no console window stays open.</summary>
    public static string Build(string exePath, string trayArg) => $"\"{exePath}\" {trayArg} {GameArg} %command%";

    // The second alternative is the older cmd form; it is still recognized so it gets rewritten.
    [GeneratedRegex("^(\"[^\"]*KovaaksCompanion[^\"]*\" \\S+ --with-game |cmd /c start \"\" \"[^\"]*KovaaksCompanion[^\"]*\" \\S+ & )", RegexOptions.IgnoreCase)]
    private static partial Regex OwnPrefix();

    /// <summary>Splits the command line after <see cref="GameArg"/> into the game's exe and its arguments; null when there is none.</summary>
    public static (string Exe, string Args)? ParseGame(string commandLine)
    {
        var i = commandLine.IndexOf(GameArg + " ", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var rest = commandLine[(i + GameArg.Length + 1)..].TrimStart();
        if (rest.Length == 0) return null;
        int end;
        if (rest[0] == '"')
        {
            var close = rest.IndexOf('"', 1);
            if (close < 0) return null;
            return (rest[1..close], rest[(close + 1)..].Trim());
        }
        end = rest.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        end = end < 0 ? rest.IndexOf(' ') : end + 4;
        return end < 0 ? (rest, "") : (rest[..end], rest[end..].Trim());
    }

    public static bool IsOwn(string option) => OwnPrefix().IsMatch(option);

    /// <summary>The option with our prefix removed; what the player had before. "%command%" alone counts as nothing.</summary>
    public static string Strip(string option)
    {
        var rest = OwnPrefix().Replace(option, "", 1);
        if (rest.StartsWith("%command%")) rest = rest[9..].TrimStart();
        return rest;
    }

    /// <summary>The option to store when the player's own option <paramref name="existing"/> is kept: ours in front, theirs behind.</summary>
    public static string Compose(string ours, string existing)
    {
        existing = Strip(existing).Trim();
        if (existing.Length == 0) return ours;
        var prefix = ours[..(ours.Length - "%command%".Length)];
        return existing.Contains("%command%") ? prefix + existing : ours + " " + existing;
    }

    /// <summary>The current LaunchOptions of KovaaK's in <paramref name="vdf"/>; empty when there is none, null when the file has no apps section.</summary>
    public static string? Read(string vdf)
    {
        var l = Lex(vdf);
        var apps = FindPath(l, 0, l.Count, AppsPath);
        if (apps == null) return null;
        var app = FindBlock(l, apps.Value.open + 1, apps.Value.close, AppId);
        if (app == null) return "";
        return FindValue(l, app.Value.open + 1, app.Value.close, "LaunchOptions") is { } v ? l[v].Text : "";
    }

    /// <summary>Returns <paramref name="vdf"/> with the LaunchOptions of KovaaK's set to <paramref name="option"/> (null or empty removes it).
    /// Everything else stays byte for byte. Null when the file has no apps section.</summary>
    public static string? Set(string vdf, string? option)
    {
        var l = Lex(vdf);
        var apps = FindPath(l, 0, l.Count, AppsPath);
        if (apps == null) return null;
        var nl = vdf.Contains("\r\n") ? "\r\n" : "\n";
        var depth = AppsPath.Length;
        var app = FindBlock(l, apps.Value.open + 1, apps.Value.close, AppId);
        var value = Quote(option ?? "");
        if (app == null)
        {
            if (string.IsNullOrEmpty(option)) return vdf;
            var t = new string('\t', depth);
            var block = $"{nl}{t}\"{AppId}\"{nl}{t}{{{nl}{t}\t\"LaunchOptions\"\t\t{value}{nl}{t}}}";
            return vdf.Insert(l[apps.Value.open].End, block);
        }
        if (FindValue(l, app.Value.open + 1, app.Value.close, "LaunchOptions") is { } v)
            return vdf[..l[v].Start] + value + vdf[l[v].End..];
        if (string.IsNullOrEmpty(option)) return vdf;
        var tabs = new string('\t', depth + 2);
        return vdf.Insert(l[app.Value.open].End, $"{nl}{tabs}\"LaunchOptions\"\t\t{value}");
    }

    static readonly string[] AppsPath = ["UserLocalConfigStore", "Software", "Valve", "Steam", "apps"];

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static bool SteamIsRunning() => Process.GetProcessesByName("steam").Length > 0;

    /// <summary>Asks Steam to exit and waits until it has. False when it is still running after <paramref name="timeout"/>.</summary>
    public static bool CloseSteam(string kovaaksPath, TimeSpan timeout)
    {
        var exe = SteamExe(kovaaksPath);
        if (exe == null) return false;
        try { Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false })?.Dispose(); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { AppLog.Error("steam shutdown", e); return false; }
        var end = DateTime.UtcNow + timeout;
        while (SteamIsRunning() && DateTime.UtcNow < end) Thread.Sleep(500);
        return !SteamIsRunning();
    }

    public static void StartSteam(string kovaaksPath)
    {
        try { if (SteamExe(kovaaksPath) is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })?.Dispose(); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { AppLog.Error("steam start", e); }
    }

    static string? SteamExe(string kovaaksPath) => SteamAccount.FindRoot(kovaaksPath) is { } r && File.Exists(Path.Combine(r, "steam.exe")) ? Path.Combine(r, "steam.exe") : null;

    /// <summary>The account's localconfig.vdf, or null when the Steam folder or account cannot be found.</summary>
    public static string? ConfigFile(string kovaaksPath, string steamId)
    {
        var root = SteamAccount.FindRoot(kovaaksPath);
        if (root == null || !long.TryParse(steamId, out var id64) || id64 <= SteamId64Base) return null;
        var file = Path.Combine(root, "userdata", (id64 - SteamId64Base).ToString(), "config", "localconfig.vdf");
        return File.Exists(file) ? file : null;
    }

    const long SteamId64Base = 76561197960265728;

    /// <summary>True when the stored option is ours and starts the app at <paramref name="exePath"/>.</summary>
    public static bool IsOn(string kovaaksPath, string steamId, string exePath, string trayArg)
    {
        try
        {
            var file = ConfigFile(kovaaksPath, steamId);
            return file != null && Read(File.ReadAllText(file)) is { } o && o.StartsWith(Build(exePath, trayArg)[..^"%command%".Length], StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Adds (<paramref name="on"/>) or removes our launch option. The player's own option stays. Steam must be closed.</summary>
    public static Result Apply(string kovaaksPath, string steamId, string exePath, string trayArg, bool on)
    {
        try
        {
            var file = ConfigFile(kovaaksPath, steamId);
            if (file == null) return Result.NotFound;
            if (SteamIsRunning()) return Result.SteamRunning;
            var vdf = File.ReadAllText(file);
            if (Read(vdf) is not { } current) return Result.NotFound;
            var next = Set(vdf, on ? Compose(Build(exePath, trayArg), current) : IsOwn(current) ? Strip(current) : current);
            if (next == null) return Result.NotFound;
            if (next == vdf) return Result.Done;
            var backup = file + ".companion-backup";
            if (!File.Exists(backup)) File.Copy(file, backup);
            var temp = file + ".companion-tmp";
            File.WriteAllText(temp, next, new UTF8Encoding(false));
            File.Move(temp, file, true);
            AppLog.Write("steam", on ? "launch option set" : "launch option removed");
            return Result.Done;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Error("steam launch option", e); return Result.NotFound; }
    }

    /// <summary>Rewrites our option when it points at an old exe path or uses the old cmd form. While Steam runs it waits for Steam to exit
    /// (Steam rewrites the file on exit, and the game is always started by a running Steam), so the fix lands after the next Steam close.</summary>
    public static void Refresh(string kovaaksPath, string steamId, string exePath, string trayArg)
    {
        if (IsOn(kovaaksPath, steamId, exePath, trayArg)) return;
        while (Apply(kovaaksPath, steamId, exePath, trayArg, true) == Result.SteamRunning)
        {
            while (SteamIsRunning()) Thread.Sleep(5000);
            Thread.Sleep(2000); // let Steam finish writing the file
        }
    }

    readonly record struct Tok(char Kind, int Start, int End, string Text);

    static List<Tok> Lex(string t)
    {
        var l = new List<Tok>();
        for (var i = 0; i < t.Length;)
        {
            var c = t[i];
            if (c is '{' or '}') { l.Add(new Tok(c, i, i + 1, "")); i++; }
            else if (c == '"')
            {
                var sb = new StringBuilder();
                var start = i++;
                while (i < t.Length && t[i] != '"')
                {
                    if (t[i] == '\\' && i + 1 < t.Length) { sb.Append(t[i + 1] switch { 'n' => '\n', 't' => '\t', var x => x }); i += 2; }
                    else sb.Append(t[i++]);
                }
                i = Math.Min(i + 1, t.Length);
                l.Add(new Tok('s', start, i, sb.ToString()));
            }
            else if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; }
            else i++;
        }
        return l;
    }

    /// <summary>Index of the '}' closing the '{' at <paramref name="open"/>; the end of the list when unbalanced.</summary>
    static int Close(List<Tok> l, int open)
    {
        var depth = 0;
        for (var i = open; i < l.Count; i++)
        {
            if (l[i].Kind == '{') depth++;
            else if (l[i].Kind == '}' && --depth == 0) return i;
        }
        return l.Count;
    }

    /// <summary>The block named <paramref name="key"/> directly inside tokens [from, to); open and close are the brace indexes.</summary>
    static (int open, int close)? FindBlock(List<Tok> l, int from, int to, string key)
    {
        for (var i = from; i < to; i++)
        {
            if (l[i].Kind == '{') { i = Close(l, i); continue; }
            if (l[i].Kind != 's' || i + 1 >= to) continue;
            if (l[i + 1].Kind == '{')
            {
                var close = Close(l, i + 1);
                if (l[i].Text.Equals(key, StringComparison.OrdinalIgnoreCase) && close < to) return (i + 1, close);
                i = close;
            }
            else if (l[i + 1].Kind == 's') i++;
        }
        return null;
    }

    static (int open, int close)? FindPath(List<Tok> l, int from, int to, string[] path)
    {
        (int open, int close)? cur = null;
        foreach (var key in path)
        {
            cur = FindBlock(l, cur == null ? from : cur.Value.open + 1, cur == null ? to : cur.Value.close, key);
            if (cur == null) return null;
        }
        return cur;
    }

    /// <summary>Token index of the value of <paramref name="key"/> directly inside tokens [from, to).</summary>
    static int? FindValue(List<Tok> l, int from, int to, string key)
    {
        for (var i = from; i < to; i++)
        {
            if (l[i].Kind == '{') { i = Close(l, i); continue; }
            if (l[i].Kind != 's' || i + 1 >= to) continue;
            if (l[i + 1].Kind == '{') { i = Close(l, i + 1); continue; }
            if (l[i + 1].Kind == 's')
            {
                if (l[i].Text.Equals(key, StringComparison.OrdinalIgnoreCase)) return i + 1;
                i++;
            }
        }
        return null;
    }
}
