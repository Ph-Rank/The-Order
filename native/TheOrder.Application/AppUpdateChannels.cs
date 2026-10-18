using System.Text.RegularExpressions;

namespace TheOrder.Application;

public enum AppUpdateChannel { Stable, Beta }
public interface IAppUpdateChannelFeed : IAppUpdateFeed
{
    Task<AppUpdateRelease?> LatestAsync(AppUpdateChannel channel, CancellationToken cancellation);
}
public interface IAppUpdateChannelStore
{
    Task<AppUpdateChannel?> LoadAsync(CancellationToken cancellation);
    Task SaveAsync(AppUpdateChannel channel, CancellationToken cancellation);
}

// Deliberately bounded SemVer subset used by our signed-off distribution names.
// Stable sorts after its own beta, irrespective of a tag's publication date.
public sealed partial record AppUpdateVersion(Version Core, int? Beta) : IComparable<AppUpdateVersion>
{
    public AppUpdateChannel Channel => Beta is null ? AppUpdateChannel.Stable : AppUpdateChannel.Beta;
    public string Text => Core.ToString(3) + (Beta is { } n ? $"-beta.{n}" : "");
    // Installer bootstrap resource contract used by already published clients.
    // This is not the payload's monotonically increasing Windows file revision.
    public Version FileVersion => new(Core.Major, Core.Minor, Core.Build, Beta ?? 0);
    public int CompareTo(AppUpdateVersion? other)
    {
        if (other is null) return 1;
        var core = Core.CompareTo(other.Core);
        return core != 0 ? core : Beta is null ? (other.Beta is null ? 0 : 1) : other.Beta is null ? -1 : Beta.Value.CompareTo(other.Beta.Value);
    }
    public static bool TryParse(string? value, out AppUpdateVersion version)
    {
        version = new(new(0, 0, 0), null);
        if (value is null || value.Length > 40) return false;
        var match = Pattern().Match(value);
        if (!match.Success || !AppUpdateIdentity.TryVersion(match.Groups[1].Value, out var core)) return false;
        int? beta = null;
        if (match.Groups[2].Success)
        {
            if (!int.TryParse(match.Groups[2].Value, out var n) || n is < 1 or > 65535) return false;
            beta = n;
        }
        version = new(core, beta); return true;
    }
    public static bool CanApply(string current, AppUpdateRelease offered)
        => TryParse(current, out var from) && TryParse(offered.Version, out var to)
        && (to.CompareTo(from) > 0 && (from.Channel == to.Channel || offered.ChannelProtocol == 1)
            || from.Channel == AppUpdateChannel.Beta && to.Channel == AppUpdateChannel.Stable && offered.ChannelProtocol == 1);
    [GeneratedRegex(@"\Av?((?:0|[1-9][0-9]{0,4})\.(?:0|[1-9][0-9]{0,4})\.(?:0|[1-9][0-9]{0,4}))(?:-beta\.([1-9][0-9]{0,4}))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
