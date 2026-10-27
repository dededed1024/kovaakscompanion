using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KovaaksCompanion.Core.Diagnostics;
using KovaaksCompanion.Core.Replays;

namespace KovaaksCompanion.Core.Web;

/// <summary>
/// Read-only loopback server for the userscript that adds a play button to benchmark websites: the list of replays that have a video and the video files.
/// Every data request needs the token baked into the script (only the user's own browser has it), and the Host header must name the loopback address,
/// so neither another website nor a DNS rebinding page can read the replays.
/// </summary>
public sealed class ReplayWebServer : IDisposable
{
    public const int Port = 47813;
    public static string ScriptUrl => $"http://127.0.0.1:{Port}/install.user.js";

    readonly Func<ReplayArchive> _archive;
    readonly byte[] _token;
    readonly HttpListener _listener = new();

    ReplayWebServer(Func<ReplayArchive> archive, string token)
    {
        _archive = archive;
        _token = Encoding.UTF8.GetBytes(token);
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    /// <summary>The running server, or null when the port is taken or the listener cannot start.</summary>
    public static ReplayWebServer? TryStart(Func<ReplayArchive> archive, string token)
    {
        var server = new ReplayWebServer(archive, token);
        try { server._listener.Start(); }
        catch (Exception e) when (e is HttpListenerException or InvalidOperationException)
        {
            AppLog.Write("web", "player server not started: " + e.Message);
            return null;
        }
        _ = Task.Run(server.Serve);
        return server;
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public void Dispose()
    {
        try { _listener.Close(); } catch (ObjectDisposedException) { }
    }

    async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            var req = ctx.Request;
            res.Headers["X-Content-Type-Options"] = "nosniff";
            res.Headers["Cache-Control"] = "no-store";
            if (req.HttpMethod != "GET" || !HostIsLoopback(req.Headers["Host"])) { Reply(res, 403, "text/plain", "forbidden"); return; }
            var path = req.Url?.AbsolutePath ?? "/";
            if (path == "/install.user.js") { Reply(res, 200, "text/javascript; charset=utf-8", Script()); return; }
            if (!TokenOk(req.QueryString["t"])) { Reply(res, 403, "text/plain", "forbidden"); return; }
            if (path == "/api/replays") { Reply(res, 200, "application/json", ReplayList()); return; }
            if (path.StartsWith("/video/", StringComparison.Ordinal) && Guid.TryParse(path.AsSpan(7), out var id)) { Video(ctx, id); return; }
            Reply(res, 404, "text/plain", "not found");
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException) { /* the browser went away */ }
        catch (Exception e)
        {
            AppLog.Write("web", "request failed: " + e.Message);
            try { Reply(res, 500, "text/plain", "error"); } catch (Exception) { }
        }
        finally { try { res.Close(); } catch (Exception) { } }
    }

    static bool HostIsLoopback(string? host) => host == $"127.0.0.1:{Port}" || host == $"localhost:{Port}";

    bool TokenOk(string? given) =>
        given is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), _token);

    static void Reply(HttpListenerResponse res, int status, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        res.StatusCode = status;
        res.ContentType = type;
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes);
    }

    string Script()
    {
        using var s = typeof(ReplayWebServer).Assembly.GetManifestResourceStream("replay-player.user.js")
            ?? throw new InvalidOperationException("replay-player.user.js is not embedded");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("__PORT__", Port.ToString()).Replace("__TOKEN__", Encoding.UTF8.GetString(_token));
    }

    string ReplayList()
    {
        var archive = _archive();
        var items = archive.List()
            .Where(i => archive.VideoPath(i) is not null)
            .Select(i => new { id = i.Id, scenario = i.Scenario, score = i.Score, end = i.End.ToString("s") });
        return JsonSerializer.Serialize(items);
    }

    void Video(HttpListenerContext ctx, Guid id)
    {
        var archive = _archive();
        var info = archive.List().FirstOrDefault(i => i.Id == id);
        var path = info is null ? null : archive.VideoPath(info);
        if (path is null) { Reply(ctx.Response, 404, "text/plain", "not found"); return; }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var res = ctx.Response;
        var length = file.Length;
        long from = 0, to = length - 1;
        res.ContentType = "video/mp4";
        res.Headers["Accept-Ranges"] = "bytes";
        if (ctx.Request.Headers["Range"] is { } range)
        {
            if (!TryParseRange(range, length, out from, out to)) { res.Headers["Content-Range"] = $"bytes */{length}"; Reply(res, 416, "text/plain", "range"); return; }
            res.StatusCode = 206;
            res.Headers["Content-Range"] = $"bytes {from}-{to}/{length}";
        }
        var count = to - from + 1;
        res.ContentLength64 = count;
        file.Seek(from, SeekOrigin.Begin);
        var buf = new byte[64 * 1024];
        while (count > 0)
        {
            var n = file.Read(buf, 0, (int)Math.Min(buf.Length, count));
            if (n <= 0) break;
            res.OutputStream.Write(buf, 0, n);
            count -= n;
        }
    }

    /// <summary>A single "bytes=a-b", "bytes=a-" or "bytes=-n" range; false when it is malformed or outside the file.</summary>
    static bool TryParseRange(string header, long length, out long from, out long to)
    {
        from = to = 0;
        if (!header.StartsWith("bytes=", StringComparison.Ordinal) || header.Contains(',')) return false;
        var parts = header[6..].Split('-');
        if (parts.Length != 2) return false;
        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], out var tail) || tail <= 0) return false;
            from = Math.Max(0, length - tail); to = length - 1;
            return true;
        }
        if (!long.TryParse(parts[0], out from) || from >= length) return false;
        to = parts[1].Length == 0 ? length - 1 : long.TryParse(parts[1], out var end) ? Math.Min(end, length - 1) : -1;
        return to >= from;
    }
}
