using System.Globalization;

namespace KovaaksCompanion.Core.Web;

/// <summary>The <c>kovaaks-companion://open?id=&lt;replay id&gt;&amp;s=&lt;video seconds&gt;</c> address the browser player uses to continue a replay in the app.</summary>
public static class ReplayLink
{
    public const string Scheme = "kovaaks-companion";

    public static bool IsLink(string? arg) => arg is not null && arg.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The replay and the video time (0 when none is given); false when the text is not an open link of a replay.</summary>
    public static bool TryParse(string? text, out Guid id, out double seconds)
    {
        id = Guid.Empty; seconds = 0;
        if (!IsLink(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase)) return false;
        string? idText = null, secText = null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "id") idText = Uri.UnescapeDataString(kv[1]);
            else if (kv[0] == "s") secText = Uri.UnescapeDataString(kv[1]);
        }
        if (!Guid.TryParse(idText, out id)) return false;
        if (double.TryParse(secText, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 && double.IsFinite(s)) seconds = s;
        return true;
    }
}
