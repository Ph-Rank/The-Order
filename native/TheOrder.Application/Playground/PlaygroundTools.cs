using System.Collections.Immutable;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using TheOrder.Domain;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    // This is JSON carried as model text, never HTML. Preserve CJK characters
    // rather than expanding each into a second layer of literal \u escapes.
    private static readonly JsonSerializerOptions ToolJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string WorkKeyFor(LibraryEntry entry)
    {
        var metadata = entry.Metadata;
        if (metadata is null) return "Native:LibraryEntry:" + Uri.EscapeDataString(entry.Id) + "|Unlinked";
        var references = metadata.References.Select(r => new { source = r.Source.Trim().ToLowerInvariant(), r.Id,
            resource = Uri.TryCreate(r.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
                ? uri.IdnHost.ToLowerInvariant() + uri.AbsolutePath.TrimEnd('/') : "unknown" })
            .OrderBy(r => r.source, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.resource, StringComparer.Ordinal).ToArray();
        // WorkMetadata can carry mixed-source/imported IDs. Preserve its real
        // namespace evidence; never invent a NextMoe or media interpretation.
        return "Native:LibraryEntry:" + Uri.EscapeDataString(entry.Id) + "|Metadata:" + Uri.EscapeDataString(metadata.Id)
            + "|Evidence:" + Hash(JsonSerializer.Serialize(references))[7..31];
    }
    private bool Can(Run run, string permission)
    {
        EnsureCurrent(run); if (!run.SavedContext) return false;
        return permission switch { "library" => run.Settings.Permissions.Library, "history" => run.Settings.Permissions.History,
            "memory" => run.Settings.Permissions.Memory, "auto" => run.Settings.Permissions.Memory && run.Settings.Permissions.AutoMemory && !run.Conversation.Temporary, _ => false };
    }
    private LibrarySnapshot? ReadLibrary(Run run)
    {
        if (!Can(run, "library")) return null;
        if (!run.LibraryCaptured) { var value = library(); EnsureCurrent(run); run.LibrarySnapshot = value; run.LibraryCaptured = true;
            if (run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.LibraryStatus) run.SpoilerLibraryStamp = SpoilerLibraryIdentity(value); }
        return run.LibrarySnapshot;
    }
    private bool ValidMemoryWork(Run run, PlaygroundMemory memory) => memory.WorkKey is null
        || ReadLibrary(run)?.Entries.Any(e => WorkKeyFor(e) == memory.WorkKey) == true;
    private static string Result(string status, object? data = null, string? notice = null)
        => JsonSerializer.Serialize(new { status, data, notice }, ToolJson);
    private static int Count(JsonElement args, string key, int fallback, int maximum)
    {
        if (!args.TryGetProperty(key, out var value)) return fallback;
        if (!value.TryGetInt32(out int result) || result is < 1 || result > maximum) throw new ArgumentException("数量超出允许范围。"); return result;
    }
    private static string Argument(JsonElement args, string key, int maximum, bool required = true)
    {
        if (!args.TryGetProperty(key, out var item)) return required ? throw new ArgumentException("缺少参数。") : "";
        if (item.ValueKind != JsonValueKind.String) throw new ArgumentException("参数应为文字。");
        string text = item.GetString() ?? ""; if (text.Length > maximum || required && string.IsNullOrWhiteSpace(text)) throw new ArgumentException("参数长度无效。"); return text;
    }
    private ImmutableArray<PlaygroundToolDefinition> Definitions(Run run)
    {
        var tools = ImmutableArray.CreateBuilder<PlaygroundToolDefinition>();
        void Add(string name, string description, string properties, string required = "") => tools.Add(new(name, description,
            "{\"type\":\"object\",\"properties\":{" + properties + "},\"required\":[" + required + "],\"additionalProperties\":false}"));
        const string limit = "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}";
        const string query = "\"query\":{\"type\":\"string\",\"maxLength\":200}";
        if (Can(run, "library"))
        {
            Add("recent_play", "按最近游玩日期返回少量作品及本次已确认会话；不是全库排名，不能用它判断累计时长前几名。", limit);
            Add("rank_playtime", "程序对完整当前书架按累计记录时长降序排序后分页。含有效人工修正、旧导入与久未游玩的记录；查询最长/最多/时长排名用此工具，不用最近游玩样本自行排序。", limit + ",\"offset\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100000}");
            Add("search_library", "用标题、别名查找作品。返回稳定 work_key；同名结果不可自动合并。", query + "," + limit, "\"query\"");
            Add("get_work", "按 search_library 返回的 work_key 读取当前状态、评分和可信目录标识。笔记在严格模式下省略。", "\"work_key\":{\"type\":\"string\",\"maxLength\":1024}", "\"work_key\"");
            Add("play_history", "查询 UTC 时间范围内的真实会话及单列的手动时长修正；跨范围会话不估计区间秒数。",
                "\"from\":{\"type\":\"string\"},\"to\":{\"type\":\"string\"},\"offset\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":5000}," + limit, "\"from\",\"to\"");
        }
        if (Can(run, "history"))
        {
            Add("search_history", "检索 Playground 已保存的用户原话，不代表覆盖前的书架笔记历史。结果可能因遗忘或剧透边界省略。", query + "," + limit, "\"query\"");
            Add("read_chat_excerpt", "读取本 Profile 某条历史消息及必要前后文；被遗忘、临时和不允许保存的来源不可读。", "\"message_id\":{\"type\":\"string\"}", "\"message_id\"");
        }
        if (Can(run, "memory")) Add("get_preferences", "读取相关、启用的有来源偏好。明确原话、用户编辑和系统推测必须区分。", query + "," + limit);
        if (Can(run, "auto")) Add("propose_preference", "只提出当前用户原话支持的作品、音乐、角色等文娱偏好；不整理生活、健康、联系方式或敏感身份，混合原话也不截取保存。宿主验证后才保存。",
            "\"message_id\":{\"type\":\"string\"},\"quote\":{\"type\":\"string\",\"maxLength\":2000},\"text\":{\"type\":\"string\",\"maxLength\":2000},\"kind\":{\"type\":\"string\",\"enum\":[\"explicit\",\"inferred\"]}", "\"message_id\",\"quote\",\"text\",\"kind\"");
        if (SearchToolAvailable(run)) Add(PublicSearchTool,
            "使用本型号的公开搜索核实宿主准入的问题；没有自由query参数，不能发送本地记录或文件内容。相同问题已读取时复用结果。准入问题：" + run.SafePublicQuestion, "");
        if (FetchToolAvailable(run)) Add(PublicFetchTool,
            "使用本服务商读取已登记source_id的公开网页。只读有界内容，不接受URL、路径或本地资料。",
            "\"source_id\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":160}", "\"source_id\"");
        return tools.ToImmutable();
    }
    private async Task<string> Tool(Run run, PlaygroundToolCall call)
    {
        EnsureCurrent(run); await Task.Yield(); EnsureCurrent(run);
        try
        {
            using var json = JsonDocument.Parse(call.ArgumentsJson, new() { MaxDepth = 8 }); var args = json.RootElement;
            if (args.ValueKind != JsonValueKind.Object) return Result("invalid_arguments");
            if (!Definitions(run).Any(d => d.Name == call.Name)) return Result("permission_denied", notice: "此工具不在本轮授权白名单。");
            var allowed = call.Name switch { "recent_play" => new[] { "limit" }, "search_library" or "search_history" or "get_preferences" => ["query", "limit"],
                "rank_playtime" => ["limit", "offset"], "get_work" => ["work_key"], "play_history" => ["from", "to", "offset", "limit"], "read_chat_excerpt" => ["message_id"],
                "propose_preference" => ["message_id", "quote", "text", "kind"], PublicSearchTool => [],
                PublicFetchTool => ["source_id"], _ => [] };
            if (args.EnumerateObject().Any(p => !allowed.Contains(p.Name))) return Result("invalid_arguments", notice: "包含未知参数。");
            string result;
            switch (call.Name)
            {
                case "recent_play":
                    Activity(run, "读取最近游玩"); var recent = ReadLibrary(run);
                    if (recent is null) return Result("unknown", notice: "书架尚未就绪。");
                    var current = activeSession(); EnsureCurrent(run);
                    var rows = recent.Entries.Where(e => e.LastPlayedAt is not null || e.Sessions.Length > 0 || e.Id == current?.GameId)
                        .OrderByDescending(e => e.Id == current?.GameId).ThenByDescending(e => Date(e.LastPlayedAt)).Take(Count(args, "limit", 6, 10)).Select(e => Record(run, e)).ToArray();
                    result = Result(rows.Length == 0 ? "empty" : "ok", new { scope = "recent_play_sample", libraryEntryCount = recent.Entries.Length,
                        returnedCount = rows.Length, observedAt = clock(), current = current is null ? null : new { current.GameId, status = Bounded(current.Status, 100),
                        confirmedSeconds = Math.Max(0, current.ConfirmedSeconds), current.ObservedAt, storyProgress = (string?)null }, works = rows }); break;
                case "rank_playtime": result = PlaytimeRanking(run, args); break;
                case "search_library":
                    Activity(run, "查找作品记录"); string query = Argument(args, "query", 200); var found = ReadLibrary(run)?.Entries
                        .Select(e => (Entry: e, Score: Aliases(e).Select(a => Match(a, query)).DefaultIfEmpty(0).Max()))
                        .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(Count(args, "limit", 6, 10)).Select(x => Record(run, x.Entry)).ToArray();
                    result = found is null ? Result("unknown") : Result(found.Length == 0 ? "empty" : "ok", found); break;
                case "get_work":
                    Activity(run, "读取作品记录"); string key = Argument(args, "work_key", 1024); var work = ReadLibrary(run)?.Entries.FirstOrDefault(e => WorkKeyFor(e) == key);
                    result = work is null ? Result("unknown_work") : Result("ok", new { record = Record(run, work),
                        notes = NarrativeRecordAllowed(run, work) ? Redacted(SpoilerSafeText(run, work.Notes, false, out _, [WorkKeyFor(work)])) : null,
                        notesNotice = "仅为当前笔记；没有常规编辑历史，原始表达时间未知。", notesReadAt = clock(),
                        metadata = work.Metadata is not { } m ? null : new { m.Id, title = Bounded(m.Title, 240), originalTitle = Bounded(m.OriginalTitle, 240),
                            aliases = m.Aliases.Take(20).Select(x => Bounded(x, 160)), developers = m.Developers.Take(10), m.ReleaseDate, m.FetchedAt,
                            references = m.References.Take(20).Select(x => new { x.Source, x.Id }),
                            description = NarrativeRecordAllowed(run, work) ? Redacted(SpoilerSafeText(run, m.Description, false, out _, [WorkKeyFor(work)]), 2000) : null } }); break;
                case "play_history": result = History(run, args); break;
                case "search_history":
                    Activity(run, "查找历史讨论"); result = ChatSearch(run, Argument(args, "query", 200), Count(args, "limit", 6, 10)); break;
                case "read_chat_excerpt":
                    Activity(run, "读取历史讨论片段"); if (!Guid.TryParse(Argument(args, "message_id", 40), out var message)) return Result("invalid_arguments");
                    result = ChatExcerpt(run, message); break;
                case "get_preferences":
                    Activity(run, "查找相关偏好"); result = Preferences(run, Argument(args, "query", 200, false), Count(args, "limit", 8, 10)); break;
                case "propose_preference": result = Propose(run, args); break;
                case PublicSearchTool: result = await PublicSearch(run).ConfigureAwait(false); break;
                case PublicFetchTool: result = await PublicFetch(run, Argument(args, "source_id", 160)).ConfigureAwait(false); break;
                default: return Result("permission_denied");
            }
            EnsureCurrent(run);
            int maximum = call.Name is PublicSearchTool or PublicFetchTool ? run.Settings.MaximumContextCharacters : 24000;
            return result.Length <= maximum ? result : Result("too_large", notice: "结果超过本轮上限，请缩小查询。");
        }
        catch (PlaygroundProviderException error) when (error.Failure.Kind is not (PlaygroundFailureKind.Authentication or PlaygroundFailureKind.AccessDenied))
        { EnsureCurrent(run); Activity(run, "本轮公开资料读取失败，未核验外部事实。"); return Result("research_failed", notice: "公开资料读取未完成，未自动重发付费请求。"); }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException && !run.Cancel.IsCancellationRequested)
        { EnsureCurrent(run); return Result("invalid_arguments", notice: "参数无效，未执行任意文件、SQL 或写入操作。"); }
    }
    private static string RecordSourceId(LibraryEntry e) => "record_" + Hash(WorkKeyFor(e))[7..23];
    private object Record(Run run, LibraryEntry e, bool register = true)
    {
        string source = RecordSourceId(e);
        if (register) LocalSource(run, new(source, PlaygroundSourceKind.LocalRecord, "作品记录 · " + Redacted(e.Name, 160))
            { TargetId = e.Id, WorkKey = WorkKeyFor(e), At = clock(), Quote = "当前书架状态与个人记录；未从时长推断剧情进度。" });
        return new { sourceId = source, workKey = WorkKeyFor(e), entryId = e.Id, title = Redacted(e.Name, 160), catalogId = e.Metadata?.Id,
            status = e.Status, statusLabel = GameProgress.Label(e.Status), statusNotice = e.Status switch
            {
                "planned" => "planned 是兼容保留值，界面为“未设置”，不表示用户计划游玩。",
                "completed" => "“玩过”不证明已通关、全路线或 CG 回收完成；没有用户明确进度时，剧情进度仍未知。",
                _ => "书架状态不代表剧情进度；以用户明确填写的进度为准。"
            },
            narrativeDiscussionAllowed = NarrativeRecordAllowed(run, e),
            personalRating = e.PersonalRating, recordedPlaySeconds = e.TotalPlaySeconds == 0 && e.Sessions.IsEmpty && e.ManualPlaytime.IsEmpty ? (long?)null : e.EffectivePlaySeconds,
            playtimeNotice = "累计记录时长，包含人工调整；不代表剧情进度。", e.LastPlayedAt,
            storyProgress = run.Conversation.Options.Spoiler is PlaygroundSpoilerMode.Progress or PlaygroundSpoilerMode.Full
                ? run.Conversation.Options.WorkProgress.GetValueOrDefault(WorkKeyFor(e)) : null,
            progressSource = (run.Conversation.Options.Spoiler is PlaygroundSpoilerMode.Progress or PlaygroundSpoilerMode.Full)
                && run.Conversation.Options.WorkProgress.ContainsKey(WorkKeyFor(e)) ? "用户在本会话明确填写" : "未知", observedAt = clock() };
    }
    private static IEnumerable<string> Aliases(LibraryEntry e) => new[] { e.Name, e.Metadata?.Title ?? "", e.Metadata?.OriginalTitle ?? "" }
        .Concat(e.Metadata?.Aliases ?? []).Where(x => !string.IsNullOrWhiteSpace(x));
    private static DateTimeOffset? Date(string? text) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    private string History(Run run, JsonElement args)
    {
        Activity(run, "读取游玩历史"); var from = Date(Argument(args, "from", 100)); var to = Date(Argument(args, "to", 100));
        if (from is null || to is null || to <= from || to - from > TimeSpan.FromDays(3660)) return Result("invalid_arguments", notice: "请提供有效 UTC 范围，最长十年。");
        int offset = args.TryGetProperty("offset", out var o) && o.TryGetInt32(out int number) ? number : 0;
        if (offset is < 0 or > 5000) return Result("invalid_arguments");
        var snapshot = ReadLibrary(run); if (snapshot is null) return Result("unknown"); int limit = Count(args, "limit", 8, 10);
        var sessions = snapshot.Entries.SelectMany(e => e.Sessions.Select(s => (Entry: e, Session: s, Start: Date(s.StartedAt), End: Date(s.EndedAt))))
            .Where(x => x.Start is not null && x.End is not null && x.Start < to && x.End > from).OrderByDescending(x => x.Start).Skip(offset).Take(limit + 1).ToArray();
        var data = sessions.Take(limit).Select(x => new { work = Record(run, x.Entry), sessionId = x.Session.Id, startedAt = x.Start, endedAt = x.End,
            totalSessionSeconds = x.Session.Seconds, secondsWithinRange = x.Start >= from && x.End <= to ? (long?)x.Session.Seconds : null,
            partialRange = x.Start < from || x.End > to }).ToArray();
        var adjustments = snapshot.Entries.SelectMany(e => e.ManualPlaytime.Where(m => !m.Undone).Select(m => (Entry: e, Time: m)))
            .Where(x => Date(x.Time.Date) >= from && Date(x.Time.Date) < to).Take(20).Select(x => new { entryId = x.Entry.Id, date = x.Time.Date, seconds = x.Time.Seconds,
                kind = "manual_correction", notice = "按日期记录的人工修正，不是精确起止的游戏会话。" }).ToArray();
        return Result(data.Length == 0 && adjustments.Length == 0 ? "empty" : "ok", new { sessions = data, manualAdjustments = adjustments,
            nextOffset = sessions.Length > limit ? (int?)(offset + limit) : null, from, to, timeZone = "UTC" });
    }
    private bool AllowedHistory(Run run, PlaygroundState s, PlaygroundMessage message, MessageAdmission admission)
    {
        if (message.Id == run.UserId || message.Retention != PlaygroundRetention.Portable || message.Status != PlaygroundMessageStatus.Complete
            || s.Conversations.Any(c => c.Id == message.ConversationId && c.Temporary) || !admission.Allows(message)) return false;
        // Historical plot fragments have no reliable per-scene progress annotation.
        // Keywords and free-form chapter labels cannot establish narrative safety.
        return NarrativeHistoryAllowed(run, message);
    }
    private IEnumerable<string> Queries(Run run, string query)
    {
        yield return query;
        if (!Can(run, "library")) yield break;
        foreach (var e in ReadLibrary(run)?.Entries ?? []) if (Aliases(e).Any(a => AliasIn(query, a)))
            foreach (var alias in Aliases(e).Take(12)) yield return alias;
    }
    private static bool AliasIn(string text, string alias)
    {
        if (alias.Length < 2) return false;
        if (alias.All(c => c < 128)) return System.Text.RegularExpressions.Regex.IsMatch(text,
            @"(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(alias) + @"(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return Normalize(text).Contains(Normalize(alias), StringComparison.Ordinal);
    }
    private string ChatSearch(Run run, string query, int limit)
    {
        var s = Snapshot; var variants = Queries(run, query).ToArray(); var admission = new MessageAdmission(this, run, s);
        var hits = s.Messages.Where(m => m.Role == "user" && AllowedHistory(run, s, m, admission)).Select(m => (Message: m, Score: variants.Max(q => Match(m.Text, q))))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => x.Message.CreatedAt).Take(limit).Select(x => ChatRecord(run, x.Message)).ToArray();
        return Result(hits.Length == 0 ? "empty_or_omitted" : "ok", hits, "范围仅为实际保存的 Playground 原话，遗忘、临时和剧情边界可能使部分片段不可用。");
    }
    private object ChatRecord(Run run, PlaygroundMessage message)
    {
        string source = "chat_" + message.Id.ToString("N");
        LocalSource(run, new(source, PlaygroundSourceKind.ChatMessage, "历史讨论 · " + message.CreatedAt.ToString("yyyy-MM-dd"))
            { TargetId = message.ConversationId.ToString("N"), MessageId = message.Id.ToString("N"), At = message.CreatedAt, Quote = Redacted(message.Text, 1000) });
        return new { sourceId = source, messageId = message.Id, conversationId = message.ConversationId, role = message.Role, at = message.CreatedAt,
            quote = Redacted(message.Text, 1800), notice = message.Role == "user" ? "用户原话" : "助手当时的回答，不是用户观点或独立事实。" };
    }
    private string ChatExcerpt(Run run, Guid id)
    {
        var s = Snapshot; var target = s.Messages.FirstOrDefault(m => m.Id == id); var admission = new MessageAdmission(this, run, s);
        if (target is null || !AllowedHistory(run, s, target, admission)) return Result("not_available", notice: "片段不存在，或受遗忘、临时及剧透边界限制。");
        var conversation = s.Messages.Where(m => m.ConversationId == target.ConversationId).ToArray(); int at = Array.FindIndex(conversation, m => m.Id == id);
        return Result("ok", conversation.Skip(Math.Max(0, at - 1)).Take(3).Where(m => AllowedHistory(run, s, m, admission)).Select(m => ChatRecord(run, m)).ToArray());
    }
    private string Preferences(Run run, string query, int limit)
    {
        var s = Snapshot; var variants = Queries(run, query).ToArray();
        bool full = run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full;
        var memories = s.Memories.Where(m => !m.Disabled && m.SupersededBy is null && ValidMemoryWork(run, m)
            && !s.Suppressions.Any(x => x.MessageIds.Contains(m.MessageId)) && (full || NarrativeMemoryAllowed(run, m) || SafePreferenceFor(m) is not null))
            .Select(m => (Memory: m, Score: variants.Max(q => Match(m.Text, q)))).Where(x => string.IsNullOrWhiteSpace(query) || x.Score > 0)
            .OrderByDescending(x => x.Memory.Kind == "user-edited").ThenByDescending(x => x.Memory.UpdatedAt).ThenByDescending(x => x.Score).Take(limit).Select(x =>
            {
                var m = x.Memory; bool detail = full || NarrativeMemoryAllowed(run, m); string id = "memory_" + m.Id.ToString("N");
                LocalSource(run, new(id, PlaygroundSourceKind.Memory, m.Kind == "inferred" ? "有证据的推测" : "已记录的用户观点")
                    { TargetId = m.Id.ToString("N"), MessageId = m.MessageId.ToString("N"), WorkKey = m.WorkKey, At = m.UpdatedAt, Quote = Redacted(m.Text, 1000) });
                return new { sourceId = id, memoryId = m.Id, text = detail ? Redacted(m.Text) : SafePreferenceFor(m)!.Text, kind = m.Kind,
                    safePreference = detail ? null : SafePreferenceFor(m), quote = Redacted(detail ? m.OriginalQuote ?? m.Text : SafePreferenceFor(m)!.Text),
                    m.ConversationId, m.MessageId, m.CreatedAt, m.UpdatedAt, m.WorkKey };
            }).ToArray();
        return Result(memories.Length == 0 ? "empty_or_omitted" : "ok", memories);
    }
    private static void LocalSource(Run run, PlaygroundSource source)
    {
        if (source.MessageId is { } text && Guid.TryParse(text, out var id)) DependOn(run, id);
        if (run.Sources.Count >= 64 && !run.Sources.ContainsKey(source.Id))
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "本轮资料来源已达到上限，请缩小问题后重试。"));
        run.Sources.TryAdd(source.Id, source);
    }
}
