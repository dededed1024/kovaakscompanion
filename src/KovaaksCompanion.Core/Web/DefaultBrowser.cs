using System.Diagnostics;
using KovaaksCompanion.Core.Diagnostics;
using Microsoft.Win32;

namespace KovaaksCompanion.Core.Web;

/// <summary>Opens a local file in the user's default web browser. The shell would open it with the program for its extension (an editor for a .js file), and a userscript manager only sees it in a browser.</summary>
public static class DefaultBrowser
{
    public static bool OpenFile(string path)
    {
        var url = new Uri(path).AbsoluteUri;
        try
        {
            if (Exe() is { } exe) { Process.Start(new ProcessStartInfo(exe, "\"" + url + "\"") { UseShellExecute = false }); return true; }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { AppLog.Write("web", "starting the browser failed: " + e.Message); }
        return false;
    }

    /// <summary>The program of the https handler (the default browser); null when it cannot be read.</summary>
    static string? Exe()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        if (choice?.GetValue("ProgId") is not string progId) return null;
        using var open = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
        if (open?.GetValue(null) is not string command) return null;
        command = command.Trim();
        var exe = command.StartsWith('"') ? command[1..].Split('"')[0] : command.Split(' ')[0];
        return File.Exists(exe) ? exe : null;
    }
}
