using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Legacy;

// Native persistence retains the user's spelling; the Electron oracle codec still models its
// historical path normalization. Both share the same complete schema validation and limits.
public sealed class NativeLibraryCodec : ILibraryCodec
{
    private readonly LegacyLibraryCodec legacy = new();
    public ImmutableArray<LibraryEntry> Decode(ReadOnlyMemory<byte> document)
    {
        try { return DecodeCore(document); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException)
        { throw new InvalidDataException("原生 session JSON 无效。", e); }
    }
    private ImmutableArray<LibraryEntry> DecodeCore(ReadOnlyMemory<byte> document)
    {
        var normalized = legacy.Decode(document);
        var original = LegacyLibraryReader.Parse(document).ToDictionary(e => e.Id.Trim(LegacyLibraryCodec.Whitespace));
        using var json = JsonDocument.Parse(document);
        var extras = new Dictionary<string, NativeExtras>(StringComparer.Ordinal);
        foreach (var game in json.RootElement.GetProperty("games").EnumerateArray())
            if (game.TryGetProperty("nativeExtras", out var x))
            {
                if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("version", out var v) || !v.TryGetInt32(out var version)) throw new InvalidDataException("缺少扩展资料版本。");
                if (version is not (1 or 2 or 3 or 4) || x.EnumerateObject().Any(p => p.Name is not ("version" or "manualPlaytime" or "guidePaths" or "metadataMix" or "installations" or "migrationOrigins" or "progressStatus"))) throw new UnsupportedExtrasSchemaException();
                var extra = x.Deserialize<NativeExtras>(NativeSessionCodec.Options) ?? throw new InvalidDataException("空扩展资料。");
                if (extra.ManualPlaytime.IsDefault || extra.Installations.IsDefault || extra.Installations.Any(i => i is null) || extra.Installations.Select(i => i.Id).Distinct().Count() != extra.Installations.Length) throw new InvalidDataException("扩展安装记录无效。");
                if (version == 1 && extra.Installations.Any(i => i.Compatibility?.Auxiliaries is not null)) throw new InvalidDataException("启动覆盖需要扩展资料 v2。");
                if (version < 3 && extra.MigrationOrigins is not null || version == 3 && (extra.MigrationOrigins is null || extra.MigrationOrigins.Value.IsDefaultOrEmpty))
                    throw new InvalidDataException("Migration lineage requires native extras v3.");
                var progressUpdate = extra.MigrationOrigins?.Any(o => o.ProgressUpdate is not null) == true;
                if (version < 4 && (extra.ProgressStatus is not null || progressUpdate)
                    || version == 4 && (extra.ProgressStatus is not (null or "wish") || extra.ProgressStatus == "wish" && LegacyJson.String(game.GetProperty("status")) != "planned"
                        || extra.ProgressStatus is null && !progressUpdate))
                    throw new InvalidDataException("进度更新需要扩展资料 v4 与兼容状态。");
                extras.Add(LegacyJson.String(game.GetProperty("id")).Trim(LegacyLibraryCodec.Whitespace), extra);
            }
        var sessions = new Dictionary<(string, string), NativeSession>();
        foreach (var game in json.RootElement.GetProperty("games").EnumerateArray())
            foreach (var session in game.GetProperty("sessions").EnumerateArray())
                if (session.TryGetProperty("native", out var n))
                    sessions.Add((LegacyJson.String(game.GetProperty("id")).Trim(LegacyLibraryCodec.Whitespace), LegacyJson.String(session.GetProperty("id")).Trim(LegacyLibraryCodec.Whitespace)),
                        NativeSessionCodec.Read(n, session.GetProperty("seconds").GetInt64()));
        var nativeIds = new Dictionary<string, NativeSession>(StringComparer.Ordinal);
        foreach (var (key, value) in sessions)
        {
            if (!Guid.TryParseExact(key.Item2, "D", out _) || !value.IsTerminal && value.GameId != key.Item1)
                throw new InvalidDataException("原生 session binding 无效。");
            // Historical aliases can contain the same terminal invocation before an explicit merge.
            // New creation is globally unique; conflicting/active duplicates can never be admitted.
            if (nativeIds.TryGetValue(key.Item2, out var previous) && (!value.IsTerminal || previous != value))
                throw new InvalidDataException("冲突的 native SessionId。");
            nativeIds[key.Item2] = value;
        }
        var result = normalized.Select(e => e with { Installations = [.. e.Installations.Select((i, index) => i with
        { Folder = original[e.Id].Installations[index].Folder, Executable = original[e.Id].Installations[index].Executable,
            WorkingDirectory = original[e.Id].Installations[index].WorkingDirectory })],
            Sessions = [.. e.Sessions.Select(s => sessions.TryGetValue((e.Id, s.Id), out var n) ? s with { Native = n } : s)] }).Select(e =>
        {
            if (extras.TryGetValue(e.Id, out var x))
            {
                if (x.Installations.Any(i => !e.Installations.Any(existing => existing.Id == i.Id))) throw new InvalidDataException("扩展安装不存在。");
                e = e with { Status = x.ProgressStatus ?? e.Status, ManualPlaytime = x.ManualPlaytime, GuidePaths = x.GuidePaths, MetadataMix = NormalizeMix(x.MetadataMix), MigrationOrigins = x.MigrationOrigins ?? [],
                    Installations = [.. e.Installations.Select(i => x.Installations.FirstOrDefault(a => a.Id == i.Id) is { } a ? i with { SaveFolder = a.SaveFolder, SteamAppId = a.SteamAppId, Compatibility = a.Compatibility } : i)] };
            }
            LibraryExtras.Validate(e); return e;
        }).ToImmutableArray();
        return result;
    }
    public LibraryDocument Inspect(ReadOnlyMemory<byte> document, string exportedAt)
    {
        ImmutableArray<LibraryEntry> entries;
        try { entries = Decode(document); }
        catch (UnsupportedExtrasSchemaException) { return new(LibraryAccess.ValidReadOnly, legacy.Decode(document), null, "当前版本只能读取，不能覆盖未来扩展字段。"); }
        catch (UnsupportedSessionSchemaException) { return new(LibraryAccess.ValidReadOnly, legacy.Decode(document), null, "UnsupportedSessionSchema：当前版本只能读取，不能覆盖未来 Session 字段。"); }
        catch (InvalidDataException e) { return new(LibraryAccess.InvalidInput, [], null, e.Message); }
        try { return new(LibraryAccess.ValidWritable, entries, Encode(entries, exportedAt)); }
        catch (LibraryWriteLimitException e) { return new(LibraryAccess.ValidReadOnly, entries, null, e.Message); }
    }
    public byte[] Encode(ImmutableArray<LibraryEntry> entries, string exportedAt)
    {
        if (entries.Any(e => !GameProgress.Valid(e.Status))) throw new InvalidDataException("游玩状态无效。");
        // Old clients recognize the legacy fallback but stop at unknown extras v4 in read-only mode.
        // Do not place an unknown enum into the legacy JSON and risk treating the library as corrupt.
        var bytes = legacy.Encode([.. entries.Select(LegacyProgress)], exportedAt);
        if (!entries.Any(e => HasExtras(e) || e.Sessions.Any(s => s.Native is not null))) return bytes;
        var games = entries.Select(e =>
        {
            var projected = LegacyLibraryCodec.Project(LegacyProgress(e));
            LibraryExtras.Validate(e);
            if (HasExtras(e)) projected["nativeExtras"] = new NativeExtras(e.Status == "wish" || e.MigrationOrigins.Any(o => o.ProgressUpdate is not null) ? 4 : !e.MigrationOrigins.IsEmpty ? 3 : e.Installations.Any(i => i.Compatibility?.Auxiliaries is not null) ? 2 : 1, e.ManualPlaytime, e.GuidePaths, NormalizeMix(e.MetadataMix),
                [.. e.Installations.Where(i => i.SaveFolder is not null || i.SteamAppId is not null || i.Compatibility is not null).Select(i => new InstallationExtras(i.Id, i.SaveFolder, i.SteamAppId, i.Compatibility))], e.MigrationOrigins.IsEmpty ? null : e.MigrationOrigins, e.Status == "wish" ? "wish" : null);
            projected["sessions"] = e.Sessions.Select(s =>
            {
                var record = new Dictionary<string, object?> { ["id"] = s.Id, ["startedAt"] = s.StartedAt, ["endedAt"] = s.EndedAt, ["seconds"] = s.Seconds };
                if (s.Native is { } n)
                {
                    n.Validate();
                    if (n.Accumulated100ns / NativeSession.TicksPerSecond != s.Seconds) throw new InvalidDataException("Session marker 不一致。");
                    record["native"] = n;
                }
                return record;
            });
            return projected;
        });
        bytes = JsonSerializer.SerializeToUtf8Bytes(new { format = "kun-gal-manager", version = 1, exportedAt, games }, NativeSessionCodec.Options);
        if (bytes.Length > LegacyLibraryReader.MaximumBytes) throw new LibraryWriteLimitException();
        return bytes;
    }
    public string InstallationKey(string path) => legacy.InstallationKey(path);
    public string EvidenceIdentity(LibraryEntry entry) => legacy.EvidenceIdentity(entry);
    public bool ProtectsName(LibraryEntry entry) => legacy.ProtectsName(entry);
    public WorkMetadata NormalizeWork(WorkMetadata work) => legacy.NormalizeWork(work);
    private MetadataMix? NormalizeMix(MetadataMix? mix)
    {
        if (mix is null) return null;
        if (mix.Layers.IsDefault || mix.Layers.Any(x => x is null || x.Work is null)) throw new InvalidDataException("资料源配置无效。");
        return mix with { Base = mix.Base is null ? null : legacy.NormalizeWork(mix.Base), Layers = [.. mix.Layers.Select(x => x with { Work = legacy.NormalizeWork(x.Work) })] };
    }
    private static LibraryEntry LegacyProgress(LibraryEntry e) => e.Status == "wish" ? e with { Status = "planned" } : e;
    private static bool HasExtras(LibraryEntry e) => e.Status == "wish" || !e.MigrationOrigins.IsEmpty || !e.ManualPlaytime.IsEmpty || !e.GuidePaths.IsEmpty || e.MetadataMix is not null || e.Installations.Any(i => i.SaveFolder is not null || i.SteamAppId is not null || i.Compatibility is not null);
    private sealed record InstallationExtras(string Id, string? SaveFolder, string? SteamAppId, LaunchCompatibility? Compatibility);
    private sealed record NativeExtras(int Version, ImmutableArray<ManualPlaytime> ManualPlaytime, ImmutableArray<string> GuidePaths, MetadataMix? MetadataMix, ImmutableArray<InstallationExtras> Installations,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<MigrationOrigin>? MigrationOrigins = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ProgressStatus = null);
}
internal sealed class UnsupportedExtrasSchemaException : Exception;
