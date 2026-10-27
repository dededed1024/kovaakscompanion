using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using KovaaksCompanion.Core;
using KovaaksCompanion.Core.Diagnostics;
using KovaaksCompanion.Core.Update;
using KovaaksCompanion.Core.Web;

namespace KovaaksCompanion.App;

public partial class App : Application
{
    const string ShowEventName = @"Local\KovaaksCompanion.ShowMain";
    const string QuitEventName = @"Local\KovaaksCompanion.Quit";
    const string VersionMapName = @"Local\KovaaksCompanion.Version";
    const string LinkEventName = @"Local\KovaaksCompanion.OpenLink";
    const string LinkMapName = @"Local\KovaaksCompanion.OpenLinkText";

    /// <summary>This build's version (assembly informational version).</summary>
    public static Version CurrentVersion { get; } = UpdateCheck.ParseVersion(typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new Version(0, 0, 0);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int dwProcessId);

    Mutex? _single;
    EventWaitHandle? _showEvent;
    RegisteredWaitHandle? _showWait;
    EventWaitHandle? _linkEvent;
    RegisteredWaitHandle? _linkWait;
    EventWaitHandle? _quitEvent;
    RegisteredWaitHandle? _quitWait;
    MemoryMappedFile? _versionMap;
    AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(100));
        if (SteamLaunchOption.ParseGame(Environment.CommandLine) is { } game) { RunGame(game.Exe, game.Args); Shutdown(); return; }
        MiddleScroll.Register();
        DispatcherUnhandledException += (_, a) => Unhandled("ui thread", a.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Unhandled("unhandled", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { foreach (var inner in a.Exception.Flatten().InnerExceptions) Unhandled("background task", inner); };
        _single =new Mutex(true, @"Local\KovaaksCompanion.SingleInstance", out var first);
        if (!first)
        {
            var replace = UpdateCheck.ShouldReplace(CurrentVersion, RunningVersion());
            if (replace) SignalQuit();
            if (!replace || !TakeOver(_single))
            {
                _single.Dispose(); _single = null;
                // A replay link goes to the running instance; a tray launch (Windows start, KovaaK's start) must not pop its window.
                if (e.Args.FirstOrDefault(ReplayLink.IsLink) is { } link) SendLink(link);
                else if (!e.Args.Contains(Autostart.TrayArg))
                    try
                    {
                        using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                        AllowSetForegroundWindow(-1);
                        ev.Set();
                    }
                    catch (Exception ex) { AppLog.Error("signal show", ex); }
                Shutdown();
                return;
            }
        }
        PublishVersion();
        if (Environment.ProcessPath is { } exe) SelfUpdate.Cleanup(exe);
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quitEvent, (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown)), null, Timeout.Infinite, false);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => _host?.ShowMain("Stats"), null, Timeout.Infinite, false);
        _linkEvent = new EventWaitHandle(false, EventResetMode.AutoReset, LinkEventName);
        _linkWait = ThreadPool.RegisterWaitForSingleObject(_linkEvent, (_, _) => { if (ReadLink() is { } l) _host?.OpenLink(l); }, null, Timeout.Infinite, false);
        _host = new AppHost(this);
        // Autostart launches with --tray; a manual launch opens the Stats window.
        _host.Start(showMain: !e.Args.Contains(Autostart.TrayArg));
        if (e.Args.FirstOrDefault(ReplayLink.IsLink) is { } startLink) _host.OpenLink(startLink);
    }

    static void Unhandled(string title, Exception? ex)
    {
        if (ex == null) return;
        AppLog.Error(title, ex);
    }

    /// <summary>Steam launch option mode: starts the tray instance, then the game, and stays (without a window) until the game exits so Steam sees it running.</summary>
    static void RunGame(string gameExe, string gameArgs)
    {
        try
        {
            if (Environment.ProcessPath is { } self)
                Process.Start(new ProcessStartInfo(self, Autostart.TrayArg) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(self)! })?.Dispose();
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { AppLog.Error("start tray", e); }
        try
        {
            using var game = Process.Start(new ProcessStartInfo(gameExe, gameArgs) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(gameExe) ?? "" });
            game?.WaitForExit();
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { AppLog.Error("start game", e); }
    }

    /// <summary>Waits for the previous instance to release the mutex; abandoned counts as acquired.</summary>
    static bool TakeOver(Mutex m)
    {
        try { return m.WaitOne(TimeSpan.FromSeconds(15)); }
        catch (AbandonedMutexException) { return true; }
    }

    static void SignalQuit()
    {
        try { using var q = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName); q.Set(); } catch (Exception e) { AppLog.Error("signal quit", e); }
    }

    /// <summary>Hands a replay link to the running instance (its text through a shared memory map, then the event).</summary>
    static void SendLink(string link)
    {
        try
        {
            using var map = MemoryMappedFile.CreateOrOpen(LinkMapName, 2048);
            using (var s = map.CreateViewStream(0, 2048))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(link.Length > 1000 ? link[..1000] : link);
                s.Write(new byte[2048], 0, 2048); s.Position = 0;
                s.Write(bytes, 0, bytes.Length);
            }
            using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, LinkEventName);
            AllowSetForegroundWindow(-1);
            ev.Set();
        }
        catch (Exception ex) { AppLog.Error("signal replay link", ex); }
    }

    static string? ReadLink()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(LinkMapName, MemoryMappedFileRights.Read);
            using var s = map.CreateViewStream(0, 2048, MemoryMappedFileAccess.Read);
            var buf = new byte[2048];
            var n = s.Read(buf, 0, buf.Length);
            var text = System.Text.Encoding.UTF8.GetString(buf, 0, n).TrimEnd('\0');
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex) { AppLog.Error("read replay link", ex); return null; }
    }

    /// <summary>The running instance's published version; null when unreadable.</summary>
    static Version? RunningVersion()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(VersionMapName, MemoryMappedFileRights.Read);
            using var s = map.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            var buf = new byte[64];
            var n = s.Read(buf, 0, buf.Length);
            return UpdateCheck.ParseVersion(System.Text.Encoding.UTF8.GetString(buf, 0, n).TrimEnd('\0'));
        }
        catch (Exception e) { AppLog.Error("running version", e); return null; }
    }

    /// <summary>Publishes this version for later launches; the map lives until exit.</summary>
    void PublishVersion()
    {
        try
        {
            _versionMap = MemoryMappedFile.CreateOrOpen(VersionMapName, 64);
            using var s = _versionMap.CreateViewStream(0, 64);
            var bytes = System.Text.Encoding.UTF8.GetBytes(CurrentVersion.ToString(3));
            s.Write(new byte[64], 0, 64); s.Position = 0;
            s.Write(bytes, 0, bytes.Length);
        }
        catch (Exception e) { AppLog.Error("publish version", e); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _linkWait?.Unregister(null);
        _linkEvent?.Dispose();
        _quitWait?.Unregister(null);
        _quitEvent?.Dispose();
        _versionMap?.Dispose();
        _host?.Dispose();
        if (_single != null) { _single.ReleaseMutex(); _single.Dispose(); }
        base.OnExit(e);
    }
}
