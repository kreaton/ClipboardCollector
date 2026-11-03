using System.Globalization;
using System.Reflection;

namespace ClipboardCollector;

/// <summary>Version, commit and build time stamped in by Directory.Build.props.</summary>
public static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

    // "1.0.42+a1b2c3d4..." - the part after + is the git commit, when the build had one.
    private static readonly string[] Informational =
        (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+', 2);

    public static string Version { get; } = Informational[0];

    public static string? Commit { get; } =
        Informational.Length > 1 ? Informational[1][..Math.Min(7, Informational[1].Length)] : null;

    public static DateTime? BuildDate { get; } =
        DateTime.TryParse(
            Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value,
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)
            ? utc
            : null;

    /// <summary>"v1.0.42 · 2026-10-24 14:32 · a1b2c3d", with the build time in the PC's local time.</summary>
    public static string Display { get; } = string.Join(" · ", new[]
    {
        "v" + Version,
        BuildDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        Commit,
    }.Where(part => part is not null));

    public const string Copyright = "© 2026 Per Hejndorf";
}
