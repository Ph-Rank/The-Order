using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed partial class MusicBrainzProvider : IMusicOutletProvider
{
    public Task<MusicOutletSnapshot> FetchOutletsAsync(MusicBrainzReference reference, CancellationToken cancellation)
    {
        if (!MusicOutlets.Reference(reference)) throw new InvalidDataException("收听资料需要具体发行版或录音编号。");
        return Operation(async token =>
        {
            var resource = reference.Kind == MusicBrainzEntityKind.Release ? "release" : "recording";
            var includes = reference.Kind == MusicBrainzEntityKind.Release ? "url-rels+release-groups+artist-credits+recordings" : "url-rels+artist-credits+isrcs";
            using var json = await GetJsonAsync(resource + "/" + reference.Id + "?fmt=json&inc=" + Uri.EscapeDataString(includes), token).ConfigureAwait(false);
            return MapOutlets(reference, json.RootElement, Now);
        }, cancellation);
    }
    public static MusicOutletSnapshot MapOutlets(MusicBrainzReference reference, JsonElement root, DateTimeOffset stamp)
    {
        if (!MusicOutlets.Reference(reference) || Id(root, "id", true) != reference.Id) throw Invalid();
        var links = new List<MusicOutletLink>();
        if (root.TryGetProperty("relations", out var rows))
        {
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 512) throw Invalid();
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.Object) continue;
                var raw = OptionalText(url, "resource", 2048);
                if (raw is null) continue;
                var ended = row.TryGetProperty("ended", out var flag) && flag.ValueKind == JsonValueKind.True;
                var item = MusicOutlets.FromRelationship(reference, Text(row, "type", 200), Text(row, "direction", 20), ended, raw, stamp);
                if (item is not null && MusicOutlets.Valid(item)) links.Add(item);
            }
        }
        string? group = null, date = null, barcode = null, status = null; string[] formats = []; int? tracks = null;
        if (reference.Kind == MusicBrainzEntityKind.Release)
        {
            var release = MapCandidate(root); group = release.ReleaseGroupId; date = release.Date; barcode = release.Barcode; formats = release.MediaFormats; tracks = release.TrackCount; status = release.Status;
        }
        var result = new MusicOutletSnapshot(reference, Text(root, "title", 2000, true), Artist(root), group,
            formats, tracks, date, barcode, MusicOutlets.Merge(links, MusicOutletSort.SourceOrder).Take(64).ToArray(), stamp) { ReleaseStatus = status };
        if (!MusicOutlets.Valid(result)) throw Invalid(); return result;
    }
}
