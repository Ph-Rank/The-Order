using System.Collections.Immutable;
using System.Text;
using TheOrder.Domain;

namespace TheOrder.Application.Migrations;

public static class MigrationResolver
{
    public static MigrationPlan Preview(ImportDataset dataset, ImmutableArray<LibraryEntry> target, ILibraryCodec codec,
        CancellationToken cancellation = default, string targetScope = "unbound", ImmutableArray<ImportReviewDecision> reviews = default)
    {
        ImportModel.Validate(dataset);
        if (targetScope != "unbound" && !MigrationLineage.Digest(targetScope)) throw new InvalidDataException("Invalid target scope.");
        var digest = ImportHash.Snapshot(target, codec);
        var origins = Index(target.SelectMany(e => e.MigrationOrigins.Select(o => ((o.Manager, o.RecordId), e))));
        var identities = Index(target.SelectMany(e => Identities(e).Select(i => ((i.Provider, i.Id), e))));
        var paths = Index(target.SelectMany(e => e.Installations.Select(i => (ImportPaths.Key(i.Folder), e))));
        var names = Index(target.SelectMany(e => new[] { e.Name, e.Metadata?.Title ?? "", e.Metadata?.OriginalTitle ?? "" }
            .Concat(e.Metadata?.Aliases ?? []).Where(n => n.Length > 0).Distinct().Select(n => (Name(n), e))), maximum: 20);
        var decisions = (reviews.IsDefault ? [] : reviews).ToDictionary(r => r.RecordKey, StringComparer.Ordinal);
        if (decisions.Count > dataset.Games.Length || decisions.Values.Any(r => r.TargetKey is null || !r.IdentityAndVersionConfirmed || !Enum.IsDefined(r.History)))
            throw new InvalidDataException("Invalid import review.");
        var usedReviews = new HashSet<string>(StringComparer.Ordinal);
        var targetIds = target.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var duplicateRecords = Duplicates(dataset.Games.Select(g => (g.SourceId, g.SourceId)));
        var duplicateIds = Duplicates(dataset.Games.SelectMany(g => g.Identities.Select(i => (i.Provider + ":" + i.Id, g.SourceId))));
        var duplicatePaths = Duplicates(dataset.Games.SelectMany(g => g.Installations.Select(i => (ImportPaths.Key(i.Folder), g.SourceId))));
        var overlappingPaths = OverlappingPaths(target.SelectMany(e => e.Installations.Select(i => ImportPaths.Key(i.Folder)))
            .Concat(dataset.Games.SelectMany(g => g.Installations.Select(i => ImportPaths.Key(i.Folder)))));
        var rows = ImmutableArray.CreateBuilder<ImportRow>(); var operations = ImmutableArray.CreateBuilder<MigrationOperation>();
        foreach (var g in dataset.Games)
        {
            cancellation.ThrowIfCancellationRequested();
            var key = ImportHash.Key(dataset.Manager, g.SourceId); var reasons = ImmutableArray.CreateBuilder<string>();
            var prior = origins.GetValueOrDefault((dataset.Manager, g.SourceId)) ?? [];
            var byId = g.Identities.SelectMany(i => identities.GetValueOrDefault((i.Provider, i.Id)) ?? []).DistinctBy(e => e.Id).ToArray();
            var byPath = g.Installations.SelectMany(i => paths.GetValueOrDefault(ImportPaths.Key(i.Folder)) ?? []).DistinctBy(e => e.Id).ToArray();
            var reviewPool = prior.Concat(byPath).Concat(byId).Concat(new[] { g.Name, g.OriginalName }.Concat(g.Aliases).Where(n => n.Length > 0)
                .SelectMany(n => names.GetValueOrDefault(Name(n)) ?? [])).DistinctBy(e => e.Id).Take(20).ToImmutableArray();
            var reviewable = true;
            if (g.NeedsReview || duplicateRecords.Contains(g.SourceId) || g.Identities.Any(i => duplicateIds.Contains(i.Provider + ":" + i.Id))
                || g.Installations.Any(i => duplicatePaths.Contains(ImportPaths.Key(i.Folder))))
            { reviewable = false; Row(ImportMatch.Conflict, ImportAction.Skip, null, ["SOURCE_RECORD_OR_IDENTITY_CONFLICT"]); continue; }
            if (g.Installations.Any(i => overlappingPaths.Contains(ImportPaths.Key(i.Folder))))
            { reviewable = false; Row(ImportMatch.Conflict, ImportAction.Skip, null, ["PARENT_CHILD_INSTALLATION_CONFLICT"]); continue; }
            if (decisions.TryGetValue(key, out var choice))
            {
                var selected = reviewPool.SingleOrDefault(e => ImportHash.Text(e.Id)[..20] == choice.TargetKey);
                if (selected is null || prior.Count > 1 || selected.Sessions.Any(s => s.Native is { IsTerminal: false }))
                    throw new InvalidDataException("Review target is unavailable or ambiguous.");
                if (choice.History == ImportHistoryChoice.UseSourceTotal && !MigrationReview.CanUseSourceTotal(selected, g.TotalSeconds))
                    throw new InvalidDataException("来源累计值小于需保留的已知会话或记录，不能采用。");
                var fields = ImmutableArray.CreateBuilder<string>();
                if (GameProgress.Higher(selected.Status, g.Status) != selected.Status) fields.Add("status");
                if (prior.Count == 0)
                {
                    if (selected.Notes.Length == 0 && g.Notes.Length > 0) fields.Add("notes");
                    if (selected.PersonalRating is null && g.Rating is not null) fields.Add("rating");
                    if (!selected.Favorite && g.Favorite == true) fields.Add("favorite");
                    if (g.Categories.Except(selected.Categories).Any()) fields.Add("categories");
                    if (g.Tags.Except(selected.CustomTags).Any()) fields.Add("tags");
                    if (selected.LastPlayedAt is null && g.LastPlayed is not null) fields.Add("lastPlayed");
                }
                if (choice.History == ImportHistoryChoice.UseSourceTotal && g.TotalSeconds != selected.EffectivePlaySeconds)
                    fields.Add(selected.TotalPlaySeconds == 0 && selected.ManualPlaytime.IsEmpty && selected.Sessions.IsEmpty && selected.PlayCount == 0 ? "history" : "history-total");
                fields.Add("lineage"); usedReviews.Add(key);
                Row(ImportMatch.Confirmed, ImportAction.FillMissing, selected, ["USER_CONFIRMED_IDENTITY_AND_VERSION",
                    choice.History == ImportHistoryChoice.UseSourceTotal ? "USER_ADOPTED_SOURCE_TOTAL" : "USER_KEPT_EXISTING_HISTORY"], fields.ToImmutable());
                operations.Add(new(g, selected.Id, selected.Id, fields.ToImmutable(), choice));
                continue;
            }
            if (prior.Count != 0)
            {
                if (prior.Count == 1 && (SameRecordExceptProgress(prior[0].MigrationOrigins.Single(o => o.Manager == dataset.Manager && o.RecordId == g.SourceId), g)
                    || GameProgress.Higher(prior[0].Status, g.Status) != prior[0].Status && ReliableProgressIdentity(prior[0], g)))
                {
                    var previous = prior[0];
                    if (GameProgress.Higher(previous.Status, g.Status) != previous.Status)
                    {
                        Row(ImportMatch.Confirmed, ImportAction.FillMissing, previous, ["HIGHER_PROGRESS_ONLY_PREVIOUS_DATA_PRESERVED"], ["status"]);
                        operations.Add(new(g, previous.Id, previous.Id, ["status"]));
                    }
                    else Row(ImportMatch.Confirmed, ImportAction.AlreadyImported, previous, ["SAME_SOURCE_RECORD_ALREADY_IMPORTED"]);
                }
                else Row(ImportMatch.Conflict, ImportAction.Skip, null, ["SOURCE_RECORD_CHANGED_OR_DUPLICATED"]);
                continue;
            }
            if (byId.Length > 1 || byPath.Length > 1 || byPath.Length == 1 && byId.Length == 1 && byPath[0].Id != byId[0].Id)
            { Row(ImportMatch.Conflict, ImportAction.Skip, null, ["MULTIPLE_OR_CONTRADICTORY_TARGETS"], candidates: [.. byId.Concat(byPath).Select(e => ImportHash.Text(e.Id)[..20]).Distinct()]); continue; }
            if (byId.Length == 1)
            {
                var e = byId[0]; var existingIds = Identities(e);
                if (MigrationReview.ConflictingIds(g, existingIds))
                { Row(ImportMatch.Conflict, ImportAction.Skip, e, ["EXTERNAL_ID_OR_EDITION_CONFLICT"]); continue; }
                if (MigrationReview.EditionDifference(g, e))
                { Row(ImportMatch.Candidate, ImportAction.Skip, e, ["TITLE_EDITION_DIFFERENCE_REQUIRES_REVIEW"]); continue; }
                if (byPath.Length != 1 || g.Installations.Any(i => !e.Installations.Any(x => ImportPaths.Key(x.Folder) == ImportPaths.Key(i.Folder))))
                { Row(ImportMatch.Candidate, ImportAction.Skip, e, ["WORK_ID_ONLY_VERSION_OR_INSTALLATION_REVIEW_REQUIRED"]); continue; }
                var conflicts = ImmutableArray.CreateBuilder<string>(); var blocked = ImmutableArray.CreateBuilder<string>();
                if (e.Sessions.Any(s => s.Native is { IsTerminal: false })) { conflicts.Add("LIVE_HISTORY_CONFLICT"); blocked.Add("history"); }
                if (e.Notes.Length != 0 && g.Notes.Length != 0 && e.Notes != g.Notes) { conflicts.Add("NOTES_CONFLICT"); blocked.Add("notes"); }
                if (e.PersonalRating is { } a && g.Rating is { } b && a != b) { conflicts.Add("RATING_CONFLICT"); blocked.Add("rating"); }
                if (e.EffectivePlaySeconds > 0 && g.TotalSeconds > 0 && e.EffectivePlaySeconds != g.TotalSeconds) { conflicts.Add("PLAYTIME_OVERLAP_CONFLICT"); blocked.Add("history"); }
                if (conflicts.Count != 0)
                {
                    if (!conflicts.Contains("LIVE_HISTORY_CONFLICT") && GameProgress.Higher(e.Status, g.Status) != e.Status)
                    {
                        Row(ImportMatch.Confirmed, ImportAction.FillMissing, e, [.. conflicts, "HIGHER_PROGRESS_ONLY_OTHER_CONFLICTS_PRESERVED"], ["status", "lineage"]);
                        operations.Add(new(g, e.Id, e.Id, ["status", "lineage"]));
                    }
                    else Row(ImportMatch.Conflict, ImportAction.Skip, e, conflicts.ToImmutable(), blocked.ToImmutable());
                    continue;
                }
                var fields = ImmutableArray.CreateBuilder<string>();
                if (GameProgress.Higher(e.Status, g.Status) != e.Status) fields.Add("status");
                if (e.Notes.Length == 0 && g.Notes.Length != 0) fields.Add("notes");
                if (e.PersonalRating is null && g.Rating is not null) fields.Add("rating");
                if (!e.Favorite && g.Favorite == true) fields.Add("favorite");
                if (g.Categories.Except(e.Categories).Any()) fields.Add("categories");
                if (g.Tags.Except(e.CustomTags).Any()) fields.Add("tags");
                if (e.LastPlayedAt is null && g.LastPlayed is not null) fields.Add("lastPlayed");
                if (e.TotalPlaySeconds == 0 && e.ManualPlaytime.IsEmpty && e.Sessions.IsEmpty && e.PlayCount == 0 && g.TotalSeconds > 0) fields.Add("history");
                else if (g.TotalSeconds > 0) reasons.Add("EXISTING_HISTORY_PRESERVED_NOT_ADDED");
                if (g.Status != e.Status) reasons.Add(fields.Contains("status") ? "HIGHER_PROGRESS_SELECTED" : "HIGHER_EXISTING_PROGRESS_PRESERVED");
                if (g.Name != e.Name) reasons.Add("EXISTING_NAME_PRESERVED");
                reasons.Add("EXTERNAL_ID_AND_EXACT_INSTALLATION");
                fields.Add("lineage");
                Row(ImportMatch.Confirmed, ImportAction.FillMissing, e, reasons.ToImmutable(), fields.ToImmutable());
                operations.Add(new(g, e.Id, e.Id, fields.ToImmutable()));
            }
            else if (byPath.Length != 0) Row(ImportMatch.Conflict, ImportAction.Skip, byPath[0], ["PATH_WITHOUT_CONFIRMED_IDENTITY"]);
            else if (new[] { g.Name, g.OriginalName }.Concat(g.Aliases).Where(s => s.Length > 0).Any(n => names.ContainsKey(Name(n))))
                Row(ImportMatch.Candidate, ImportAction.Skip, null, ["TITLE_ONLY_NEVER_MERGED", "BOUNDED_CANDIDATE_LIST_REPREVIEW_BEFORE_SELECTION"],
                    candidates: [.. new[] { g.Name, g.OriginalName }.Concat(g.Aliases).Where(s => s.Length > 0).SelectMany(n => names.GetValueOrDefault(Name(n)) ?? []).Select(e => ImportHash.Text(e.Id)[..20]).Distinct().Take(20)]);
            else
            {
                var id = "migration-" + key;
                if (targetIds.Contains(id)) { Row(ImportMatch.Conflict, ImportAction.Skip, null, ["TARGET_ID_REUSED"]); continue; }
                Row(ImportMatch.Unmatched, ImportAction.Create, null, ["NEW_ENTRY_NO_UNSAFE_IDENTITY_BINDING"], ["name", "status", "rating", "favorite", "categories", "tags", "notes", "installations", "history", "lastPlayed", "addedAt", "lineage"]);
                operations.Add(new(g, null, id, rows[^1].Fields));
            }
            void Row(ImportMatch match, ImportAction action, LibraryEntry? e, ImmutableArray<string> why, ImmutableArray<string> fields = default, ImmutableArray<string> candidates = default)
                => rows.Add(new(key, match, action, e is null ? null : ImportHash.Text(e.Id)[..20], why, fields.IsDefault ? [] : fields)
                { CandidateKeys = candidates.IsDefault ? e is null ? [] : [ImportHash.Text(e.Id)[..20]] : candidates,
                    ReviewCandidates = reviewable && prior.Count <= 1 && (match is ImportMatch.Candidate or ImportMatch.Conflict
                        || decisions.ContainsKey(key) || why.Contains("HIGHER_PROGRESS_ONLY_OTHER_CONFLICTS_PRESERVED"))
                        ? [.. reviewPool.Where(t => !t.Sessions.Any(s => s.Native is { IsTerminal: false })).Select(t => MigrationReview.Candidate(t, g))] : [],
                    HumanReviewed = decisions.ContainsKey(key),
                    StatusBefore = e?.Status ?? GameProgress.Default,
                    StatusAfter = action == ImportAction.Create ? g.Status : e is not null ? !fields.IsDefault && fields.Contains("status") ? GameProgress.Higher(e.Status, g.Status) : e.Status : null });
        }
        if (usedReviews.Count != decisions.Count) throw new InvalidDataException("Review no longer applies to the source or target.");
        // A preview is a validated plan, including the real target codec and document-size limit.
        var projected = target.ToDictionary(e => e.Id, StringComparer.Ordinal);
        foreach (var op in operations)
        {
            cancellation.ThrowIfCancellationRequested();
            projected[op.GameId] = MigrationProjection.Apply(op, dataset, op.TargetId is null ? null : projected[op.TargetId], "2000-01-01T00:00:00.000Z");
        }
        _ = codec.Decode(codec.Encode([.. projected.Values], "2000-01-01T00:00:00.000Z"));
        if (operations.Where(o => o.TargetId is not null).GroupBy(o => o.TargetId).Any(g => g.Count() > 1))
            throw new InvalidDataException("同一次导入不能把多个来源记录合并到同一条目。");
        return new(dataset, digest, targetScope, rows.ToImmutable(), operations.ToImmutable(), reviews);
    }
    public static ImmutableArray<ImportedIdentity> Identities(LibraryEntry entry)
        => [.. (entry.Metadata?.References.Where(r => r.Source is "vndb" or "bangumi" or "nextmoe" or "steam").Select(r => new ImportedIdentity(r.Source, r.Id)) ?? [])
            .Concat(entry.MigrationOrigins.Where(o => o.Review is null).SelectMany(o => o.Identities)).Where(MigrationLineage.Identity).Distinct()];
    private static bool SameRecordExceptProgress(MigrationOrigin origin, ImportGame game)
    {
        if (origin.RecordDigest == ImportHash.Of(game) || origin.ProgressUpdate?.RecordDigest == ImportHash.Of(game) || origin.Review?.SourceRecordDigest == ImportHash.Of(game)) return true;
        // Includes the v1 mapper that collapsed PotatoVN's 想玩 into planned. No other changed fact is admitted.
        return GameProgress.Ordered.Where(status => status != game.Status).Any(status =>
            origin.RecordDigest == ImportHash.Of(game with { Status = status }) || origin.Review?.SourceRecordDigest == ImportHash.Of(game with { Status = status }));
    }
    private static bool ReliableProgressIdentity(LibraryEntry target, ImportGame game)
    {
        var ids = Identities(target);
        return !game.Identities.IsEmpty && game.Identities.Any(i => ids.Contains(i))
            && !MigrationReview.ConflictingIds(game, ids) && !MigrationReview.EditionDifference(game, target)
            && !game.Installations.IsEmpty && game.Installations.All(i => target.Installations.Any(x => ImportPaths.Key(x.Folder) == ImportPaths.Key(i.Folder)
                && (i.Executable is null || x.Executable is null || ImportPaths.Key(x.Executable) == ImportPaths.Key(i.Executable))))
            && !target.MigrationOrigins.Any(o => o.Edition is not null && game.Edition is not null && o.Edition != game.Edition);
    }
    private static string Name(string value) => value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    private static Dictionary<K, List<LibraryEntry>> Index<K>(IEnumerable<(K Key, LibraryEntry Entry)> data, int maximum = 2) where K : notnull
    {
        var result = new Dictionary<K, List<LibraryEntry>>();
        foreach (var (key, entry) in data) { if (!result.TryGetValue(key, out var list)) result[key] = list = []; if (list.Count < maximum && !list.Any(x => x.Id == entry.Id)) list.Add(entry); }
        return result;
    }
    private static HashSet<string> Duplicates(IEnumerable<(string Key, string Record)> data)
        => data.GroupBy(x => x.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
    private static HashSet<string> OverlappingPaths(IEnumerable<string> paths)
    {
        var all = paths.ToHashSet(StringComparer.Ordinal); var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in all)
            for (var slash = path.IndexOf('\\', 3); slash >= 0; slash = path.IndexOf('\\', slash + 1))
                if (all.Contains(path[..slash])) { result.Add(path); result.Add(path[..slash]); }
        return result;
    }
}

internal static class MigrationProjection
{
    internal static LibraryEntry Apply(MigrationOperation operation, ImportDataset dataset, LibraryEntry? old, string now)
    {
        var g = operation.Game;
        var installations = g.Installations.Select(i => new Installation("import-" + ImportHash.Key(dataset.Manager, g.SourceId + ":" + i.SourceId), i.Folder, i.Executable, [])).ToImmutableArray();
        var history = g.Sessions.Select(s => new PlaySession(ImportHash.GuidFor(dataset.Manager + ":" + g.SourceId + ":session:" + s.SourceId), NativeSession.Format(s.Start), NativeSession.Format(s.End), s.Seconds)).ToImmutableArray();
        var manual = g.Days.Select(d => new ManualPlaytime(ImportHash.GuidFor(dataset.Manager + ":" + g.SourceId + ":day:" + d.Date), d.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), d.Seconds,
            "导入的来源按日累计；非真实会话。", now, false)).ToImmutableArray();
        var reportedTotal = checked(g.TotalSeconds - g.Days.Sum(d => d.Seconds));
        var e = old ?? new LibraryEntry(operation.GameId, g.Name, true, null, installations, installations.FirstOrDefault()?.Id,
            g.Status, g.Rating, g.Favorite ?? false, g.Categories, g.Tags, g.Notes, reportedTotal, g.PlayCount ?? 0,
            history, g.AddedAt is { } added ? NativeSession.Format(added) : now,
            g.LastPlayed is { } last ? NativeSession.Format(last) : null, null, null, new(false, null)) { ManualPlaytime = manual };
        if (old is not null)
        {
            var fields = operation.Fields;
            e = old with
            {
                Status = fields.Contains("status") ? GameProgress.Higher(old.Status, g.Status) : old.Status,
                Notes = fields.Contains("notes") ? g.Notes : old.Notes,
                PersonalRating = fields.Contains("rating") ? g.Rating : old.PersonalRating,
                Favorite = old.Favorite || fields.Contains("favorite"),
                Categories = fields.Contains("categories") ? [.. old.Categories.Concat(g.Categories).Distinct(StringComparer.Ordinal)] : old.Categories,
                CustomTags = fields.Contains("tags") ? [.. old.CustomTags.Concat(g.Tags).Distinct(StringComparer.Ordinal)] : old.CustomTags,
                LastPlayedAt = fields.Contains("lastPlayed") ? NativeSession.Format(g.LastPlayed!.Value) : old.LastPlayedAt,
                TotalPlaySeconds = fields.Contains("history") ? reportedTotal : old.TotalPlaySeconds,
                Sessions = fields.Contains("history") ? history : old.Sessions,
                ManualPlaytime = fields.Contains("history") ? manual : old.ManualPlaytime,
                PlayCount = fields.Contains("history") ? g.PlayCount ?? 0 : old.PlayCount
            };
            if (fields.Contains("history-total"))
            {
                if (operation.Review?.History != ImportHistoryChoice.UseSourceTotal || !MigrationReview.CanUseSourceTotal(old, g.TotalSeconds))
                    throw new InvalidDataException("Historical total requires explicit review.");
                // Preserve all dated sessions, corrections and launch counts. Only the
                // undated historical remainder changes; never manufacture daily rows.
                e = e with { TotalPlaySeconds = g.TotalSeconds - old.ManualPlaytime.Where(m => !m.Undone).Sum(m => m.Seconds) };
            }
        }
        var sourceFields = new Dictionary<string, string>
        { ["name"] = "Name.Value", ["status"] = "PlayType", ["rating"] = "MyRate", ["favorite"] = "source favorite", ["categories"] = "data.categoryGroups.json", ["tags"] = "Tags.Value", ["notes"] = "Comment", ["installations"] = "data.galgameSources.json / Path / ExePath", ["history"] = "TotalPlayTime / PlayedTime", ["history-total"] = "TotalPlayTime; explicitly reviewed cumulative total", ["lastPlayed"] = "LastPlayTime", ["addedAt"] = "AddTime", ["lineage"] = "Uuid / Ids / OriginalName / ChineseName" };
        var origin = new MigrationOrigin(dataset.Manager, g.SourceId, ImportHash.Of(g), dataset.DocumentDigest, dataset.Format, now,
            g.OriginalName, g.Aliases, g.Identities, [.. operation.Fields.Select(f => new ImportedField(f, sourceFields[f], ImportHash.Of(new { f, value = Value(e, f), g.OriginalName, g.Aliases, g.Identities })))],
            g.Precision switch { ImportTimePrecision.DailyAggregate => "daily-aggregate", ImportTimePrecision.Sessions => "sessions", _ => "undated-total" },
            g.TotalSeconds, g.PlayCount, g.LocalLastPlayed, g.LocalAddedAt, g.Edition, dataset.SourceTimeZone);
        if (operation.Review is { } review)
            origin = origin with { Review = new(ImportHash.Of(g), dataset.DocumentDigest, review.TargetKey!, review.IdentityAndVersionConfirmed,
                review.History == ImportHistoryChoice.UseSourceTotal ? "use-source-total" : "keep-existing", old?.EffectivePlaySeconds ?? 0, e.EffectivePlaySeconds, now) };
        var previous = e.MigrationOrigins.FirstOrDefault(o => o.Manager == dataset.Manager && o.RecordId == g.SourceId);
        return e with { MigrationOrigins = previous is null ? e.MigrationOrigins.Add(origin)
            : e.MigrationOrigins.Replace(previous, previous with {
                ProgressUpdate = operation.Fields.Contains("status") ? new(e.Status, dataset.DocumentDigest, ImportHash.Of(g), now) : previous.ProgressUpdate,
                Review = origin.Review ?? previous.Review }) };
    }
    private static object? Value(LibraryEntry e, string field) => field switch
    {
        "name" => e.Name, "status" => e.Status, "rating" => e.PersonalRating, "favorite" => e.Favorite,
        "categories" => e.Categories, "tags" => e.CustomTags, "notes" => e.Notes, "installations" => e.Installations,
        "history" or "history-total" => new { e.TotalPlaySeconds, e.ManualPlaytime, e.Sessions, e.PlayCount }, "lastPlayed" => e.LastPlayedAt,
        "addedAt" => e.AddedAt, _ => null
    };
}
