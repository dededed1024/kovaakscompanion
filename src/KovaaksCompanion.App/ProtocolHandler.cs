using System.IO;
using System.Security;
using KovaaksCompanion.Core.Diagnostics;
using KovaaksCompanion.Core.Web;
using Microsoft.Win32;

namespace KovaaksCompanion.App;

/// <summary>Registers <c>kovaaks-companion://</c> for the current user, so the browser player can start the app (or reach the running one) at a replay.</summary>
static class ProtocolHandler
{
    public static void Register()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ReplayLink.Scheme);
            key.SetValue(null, "URL:KovaaK's Companion");
            key.SetValue("URL Protocol", "");
            using var command = key.CreateSubKey(@"shell\open\command");
            var line = $"\"{exe}\" \"%1\"";
            if (command.GetValue(null) as string != line) command.SetValue(null, line);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException) { AppLog.Write("web", "registering the replay link failed: " + e.Message); }
    }
}
