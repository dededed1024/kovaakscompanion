using System.Reflection;

namespace KovaaksCompanion.Core;

/// <summary>Values baked in at build time from environment variables or the git-ignored .env (see the Core csproj).</summary>
public static class BuildConfig
{
    /// <summary>Discord server invite URL from KOVAAKS_DISCORD_INVITE_URL. Empty hides the Discord icon in Settings.</summary>
    public static readonly string DiscordInviteUrl =
        typeof(BuildConfig).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DiscordInviteUrl")?.Value ?? "";
}
