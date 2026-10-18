using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// A Native Profile user-data component. It shares the existing profile pins,
// writer ownership and atomic file primitive, and has no remote-cache eviction.
public sealed partial class IsolatedMediaIdentityDecisions(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? persistenceProbe = null) : IMediaIdentityDecisions
{
    public const string DirectoryName = "native-media-identities-v1";
    public const string LegacyArchiveName = "legacy-confirmations.json";
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly ConditionalWeakTable<NativeProfileStore, SemaphoreSlim> Gates = new();
    private readonly SemaphoreSlim serial = configuration.Profile is NativeProfileStore owner ? Gates.GetValue(owner, _ => new(1, 1)) : new(1, 1);
    private readonly HashSet<MediaIdentityPurpose> initialized = [];
    private LegacyMediaIdentityChoice[] legacy = [];
    private static readonly JsonSerializerOptions Options = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 32 };
    private sealed record Envelope(int Version, string Kind, Guid ProfileId, Guid StoreId, string Key, string Hash, byte[] Payload);

    public Task InitializeAsync(CancellationToken cancellation = default) => Run(async (root, profile) =>
    {
        foreach (var purpose in Enum.GetValues<MediaIdentityPurpose>()) await Initialize(root, profile, purpose, cancellation).ConfigureAwait(false);
        return true;
    }, cancellation);
    public Task ArchiveLegacyAsync(MediaIdentityPurpose purpose, CancellationToken cancellation) => Run(async (root, profile) =>
    { await Initialize(root, profile, purpose, cancellation).ConfigureAwait(false); return true; }, cancellation);

    public Task<MediaIdentityDecisionDocument> ReadAsync(MediaIdentityContext source, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("跨站关联来源身份无效。");
        return Run(async (root, profile) =>
        {
            var path = Path.Combine(root, source.Anchor.Key + ".json");
            var current = ReadDocument(path, profile, source.Anchor);
            if (current is not null)
            {
                // Repair a secondary migration receipt after a successful
                // primary commit. Failure here cannot hide a valid user choice.
                foreach (var row in current.Decisions.Where(d => d.Origin == MediaIdentityDecisionOrigin.LegacyConfirmation))
                    try { await AssignLegacySource(root, profile, row.Context, cancellation).ConfigureAwait(false); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return current;
            }
            await Initialize(root, profile, source.Anchor.Purpose, cancellation).ConfigureAwait(false);
            var choices = legacy.Where(x => !x.Automatic && x.Purpose == source.Anchor.Purpose && x.Fingerprint == source.Fingerprint).ToArray();
            if (choices.Length == 0)
                // A legacy fingerprint cannot reveal its source ID. If facts
                // changed before the first upgrade, abstain rather than silently
                // replacing an unassigned historical choice by an automatic one.
                return MediaIdentityDecisionDocument.Empty(source.Anchor) with
                { HasLegacyConflict = legacy.Any(x => !x.Automatic && x.Purpose == source.Anchor.Purpose && x.SourceAnchor is null) };
            var targets = choices.Select(x => x.Target).Distinct().ToArray();
            MediaIdentityDecisionDocument imported;
            if (targets.Length != 1 || !MediaIdentityDecisionRules.Compatible(source.Anchor, targets[0]) || choices.Any(x => x.NeedsReview || x.SourceAnchor is { } assigned && assigned != source.Anchor))
                imported = new(source.Anchor, 1, []) { HasLegacyConflict = true };
            else
            {
                var choice = choices.OrderByDescending(x => x.ConfirmedAtUtc).First();
                imported = new(source.Anchor, 1, [new(choice.Target, MediaIdentityDecisionKind.Confirmed, source, "", null,
                    choice.ConfirmedAtUtc, MediaIdentityDecisionOrigin.LegacyConfirmation, choice.Evidence)]);
            }
            await Write(root, profile, source.Anchor.Key, "decisions", imported, cancellation).ConfigureAwait(false);
            try { await AssignLegacySource(root, profile, source, cancellation).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return imported;
        }, cancellation);
    }

    public Task<MediaIdentityDecisionDocument> ApplyAsync(MediaIdentityDecision decision, long expectedRevision, CancellationToken cancellation)
    {
        if (!MediaIdentityDecisionRules.Valid(decision)) throw new InvalidDataException("跨站关联决定无效。");
        // Snapshot before yielding; neither UI arrays nor cache state authorize a write.
        return Run(async (root, profile) =>
        {
            var anchor = decision.Context.Anchor;
            var current = ReadDocument(Path.Combine(root, anchor.Key + ".json"), profile, anchor);
            if (current is null) await Initialize(root, profile, anchor.Purpose, cancellation).ConfigureAwait(false);
            current ??= MediaIdentityDecisionDocument.Empty(anchor);
            var next = MediaIdentityDecisionRules.Apply(current, decision, expectedRevision);
            await Write(root, profile, anchor.Key, "decisions", next, cancellation).ConfigureAwait(false);
            return next;
        }, cancellation);
    }

    private async Task Initialize(string root, NativeProfileStore profile, MediaIdentityPurpose purpose, CancellationToken cancellation)
    {
        var archivePath = Path.Combine(root, LegacyArchiveName);
        var archived = ReadPayload<LegacyMediaIdentityChoice[]>(archivePath, profile, "legacy-confirmations", "legacy");
        var collected = new List<LegacyMediaIdentityChoice>(archived ?? []);
        if (collected.Count > 16384 || collected.Any(x => !LegacyValid(x))) throw new IOException("历史关联备份内容无效，原文件保留。");
        if (initialized.Contains(purpose)) { legacy = collected.ToArray(); return; }
        var parent = Path.GetDirectoryName(root)!;
        foreach (var directory in new[] { IsolatedAniListCache.DirectoryName, IsolatedMusicBrainzCache.DirectoryName })
        {
            if ((directory == IsolatedAniListCache.DirectoryName ? MediaIdentityPurpose.AniListMedia : MediaIdentityPurpose.MusicRelease) != purpose) continue;
            var source = Path.Combine(parent, directory); NativeProfilePaths.CheckChain(source);
            if (!Directory.Exists(source)) continue;
            var pins = IsolatedLibraryPersistence.PinDirectoryChain(source);
            try
            {
                var files = Directory.EnumerateFiles(source, "*.json").Take(129).ToArray();
                if (files.Length > 128) throw new IOException("旧关联缓存超出既有边界，未猜测或删除历史记录。");
                foreach (var path in files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (directory == IsolatedAniListCache.DirectoryName)
                    {
                        var value = IsolatedAniListCache.ReadVerified(path, profile);
                        if (value is null) throw new IOException("旧 AniList 文件不可验证，保留原文件，请恢复有效备份后重试。");
                        if (value.Binding is { } b && !b.Evidence.StartsWith("自动关联", StringComparison.Ordinal))
                        {
                            await BackupLegacyFile(root, profile, purpose, path, cancellation).ConfigureAwait(false);
                            collected.Add(new(MediaIdentityPurpose.AniListMedia, b.SourceFingerprint,
                                new("AniList", b.Kind == AniListMediaKind.Anime ? "Anime" : "Manga", b.MediaId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                value.CachedAtUtc, b.Evidence, false,
                                !b.Evidence.StartsWith("用户", StringComparison.Ordinal) && !b.Evidence.StartsWith("人工", StringComparison.Ordinal)));
                        }
                    }
                    else
                    {
                        var value = IsolatedMusicBrainzCache.ReadVerified(path, profile);
                        if (value is null) throw new IOException("旧音乐文件不可验证，保留原文件，请恢复有效备份后重试。");
                        if (value.Binding is { } b)
                        {
                            await BackupLegacyFile(root, profile, purpose, path, cancellation).ConfigureAwait(false);
                            collected.Add(new(MediaIdentityPurpose.MusicRelease, b.SourceFingerprint,
                                new("MusicBrainz", "Release", b.ReleaseId), value.CachedAtUtc, "用户选择发行版 · 旧版记录"));
                        }
                    }
                }
            }
            finally { foreach (var pin in pins) pin.Dispose(); }
        }
        legacy = collected.Where(x => x.SourceAnchor is not null || !collected.Any(y => y.SourceAnchor is not null
            && y.Purpose == x.Purpose && y.Fingerprint == x.Fingerprint && y.Target == x.Target)).Distinct()
            .OrderBy(x => x.Purpose).ThenBy(x => x.Fingerprint, StringComparer.Ordinal).ThenBy(x => x.Target.Id, StringComparer.Ordinal).ToArray();
        if (legacy.Length > 16384) throw new IOException("历史关联数量超出边界，原文件保留。");
        if (archived is null || !archived.SequenceEqual(legacy))
            await Write(root, profile, "legacy", "legacy-confirmations", legacy, cancellation).ConfigureAwait(false);
        initialized.Add(purpose);
    }
    private async Task AssignLegacySource(string root, NativeProfileStore profile, MediaIdentityContext source, CancellationToken cancellation)
    {
        var archived = ReadPayload<LegacyMediaIdentityChoice[]>(Path.Combine(root, LegacyArchiveName), profile, "legacy-confirmations", "legacy");
        if (archived is null) return;
        if (archived.Length > 16384 || archived.Any(x => !LegacyValid(x))) throw new IOException("历史关联来源记录无效。");
        var updated = archived.Select(x => x.SourceAnchor is null && x.Purpose == source.Anchor.Purpose && x.Fingerprint == source.Fingerprint
            ? x with { SourceAnchor = source.Anchor } : x).ToArray();
        if (updated.SequenceEqual(archived)) return;
        await Write(root, profile, "legacy", "legacy-confirmations", updated, cancellation).ConfigureAwait(false); legacy = updated;
    }
    private async Task BackupLegacyFile(string root, NativeProfileStore profile, MediaIdentityPurpose purpose, string path, CancellationToken cancellation)
    {
        using var file = IsolatedLibrarySource.OpenChecked(path);
        if (file.Length is < 1 or > 6 * 1024 * 1024) throw new IOException("旧确认文件超过既有边界，原内容保留。");
        var original = new byte[checked((int)file.Length)]; file.ReadExactly(original);
        var key = "legacy-file-" + purpose + "-" + Path.GetFileNameWithoutExtension(path) + "-" + Convert.ToHexString(SHA256.HashData(original))[..16];
        var stored = ReadPayload<byte[]>(Path.Combine(root, key + ".json"), profile, "legacy-file", key);
        if (stored is not null)
        { if (!original.SequenceEqual(stored)) throw new IOException("旧确认备份校验不一致，原内容保留。"); return; }
        // Preserve the exact old envelope for a deliberate rollback. Only after
        // every choice and raw envelope is archived may remote eviction remove it.
        await Write(root, profile, key, "legacy-file", original, cancellation).ConfigureAwait(false);
    }

    private static bool LegacyValid(LegacyMediaIdentityChoice choice) => choice is { Target.IsValid: true }
        && Enum.IsDefined(choice.Purpose) && choice.Fingerprint is { Length: 64 } && choice.Fingerprint.All(char.IsAsciiHexDigit)
        && (choice.SourceAnchor is null || choice.SourceAnchor.IsValid && choice.SourceAnchor.Purpose == choice.Purpose
            && MediaIdentityDecisionRules.Compatible(choice.SourceAnchor, choice.Target))
        && choice.ConfirmedAtUtc != default && AniListValidation.Text(choice.Evidence, 1000, false);
    private static MediaIdentityDecisionDocument? ReadDocument(string path, NativeProfileStore profile, MediaIdentityAnchor anchor)
    {
        var value = ReadPayload<MediaIdentityDecisionDocument>(path, profile, "decisions", anchor.Key);
        if (value is not null && (!MediaIdentityDecisionRules.Valid(value) || value.Anchor != anchor)) throw new IOException("关联决定的身份或内容不一致，原文件保留。");
        return value;
    }
    private static T? ReadPayload<T>(string path, NativeProfileStore profile, string kind, string key)
    {
        try
        {
            using var file = IsolatedLibrarySource.OpenChecked(path);
            if (file.Length is < 1 or > MaximumBytes) throw new IOException("关联文件大小无效。");
            var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
            var envelope = Decode<Envelope>(bytes);
            if (envelope is null || envelope.Version != 1 || envelope.Kind != kind || envelope.Key != key
                || envelope.ProfileId != profile.Manifest.ProfileId || envelope.StoreId != profile.Manifest.StoreId
                || envelope.Payload is not { Length: > 0 and <= MaximumBytes }
                || envelope.Hash != Convert.ToHexString(SHA256.HashData(envelope.Payload))) throw new IOException("关联文件版本、身份或校验无效，原文件保留。");
            return Decode<T>(envelope.Payload) ?? throw new IOException("关联文件内容缺失。");
        }
        catch (FileNotFoundException) { return default; }
    }
    private static T? Decode<T>(byte[] bytes)
    {
        try
        {
            using var parsed = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            Unique(parsed.RootElement); return parsed.RootElement.Deserialize<T>(Options);
        }
        catch (JsonException e) { throw new IOException("关联文件格式不可读取，原文件保留。", e); }
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new JsonException("Duplicate property"); Unique(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
    }
    private async Task Write<T>(string root, NativeProfileStore profile, string key, string kind, T value, CancellationToken cancellation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, kind, profile.Manifest.ProfileId, profile.Manifest.StoreId,
            key, Convert.ToHexString(SHA256.HashData(payload)), payload), Options);
        if (bytes.Length > MaximumBytes) throw new IOException("关联记录超过单文件边界，原内容保留。");
        var target = Path.Combine(root, key == "legacy" ? LegacyArchiveName : key + ".json");
        cancellation.ThrowIfCancellationRequested();
        await WindowsAtomicFile.WriteAsync(target, bytes, async stage =>
        {
            if (persistenceProbe is not null) await persistenceProbe(stage).ConfigureAwait(false);
            if (stage is PersistenceStage.BeforeTempWrite or PersistenceStage.BeforeReplace) cancellation.ThrowIfCancellationRequested();
            if (stage == PersistenceStage.BeforeReplace)
            {
                profile.ValidateAccess();
                _ = ReadPayload<JsonElement>(target, profile, kind, key);
            }
        }).ConfigureAwait(false);
    }
    private async Task<T> Run<T>(Func<string, NativeProfileStore, Task<T>> action, CancellationToken cancellation)
    {
        if (configuration.Profile is not NativeProfileStore profile) throw new IOException("当前环境没有可保存关联的 Profile。");
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                var parent = NativeProfileAccess.Validate(configuration); var held = IsolatedLibraryPersistence.PinDirectoryChain(parent);
                try
                {
                    profile.ValidateAccess(); var root = Path.Combine(parent, DirectoryName); NativeProfilePaths.CheckChain(root);
                    Directory.CreateDirectory(root); var pinned = IsolatedLibraryPersistence.PinDirectoryChain(root);
                    try { profile.ValidateAccess(); cancellation.ThrowIfCancellationRequested(); return await action(root, profile).ConfigureAwait(false); }
                    finally { foreach (var pin in pinned) pin.Dispose(); }
                }
                finally { foreach (var pin in held) pin.Dispose(); }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NativeProfileException or DataIsolationException or JsonException or ArgumentException)
        { throw new IOException("关联资料路径或身份不可用，原内容保留。", e); }
        finally { serial.Release(); }
    }
}
