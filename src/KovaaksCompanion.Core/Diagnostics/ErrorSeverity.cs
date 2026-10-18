namespace KovaaksCompanion.Core.Diagnostics;

/// <summary>How urgent an automatic error report is: 1 is worst. It weighs the damage and whether a new release can fix it: a code bug a release fixes ranks above a problem on the user's machine (disk, permissions, device, network, antivirus) that no release changes. The number and the embed color in the webhook message follow it.</summary>
public enum ErrorSeverity
{
    /// <summary>1: crash or unhandled exception; always a code bug, a release fixes it.</summary>
    Critical = 1,
    /// <summary>2: a main feature (recording, replay playback) is broken by a code or bundled-file bug; a release fixes it.</summary>
    High = 2,
    /// <summary>3: data or settings may not be read or saved, or a main feature fails because of the user's machine (disk, permissions, device, missing Steam); a release may not help.</summary>
    Medium = 3,
    /// <summary>4: best-effort work failed (cleanup, clipboard) or the cause is only the environment (offline, access denied); the app keeps working and no release helps.</summary>
    Low = 4,
}
