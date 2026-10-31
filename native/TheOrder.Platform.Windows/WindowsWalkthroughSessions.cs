using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Owns only the companion, never a game process. The existing launch/session engine remains authoritative.
public sealed record GuideLaunchNotice(string GameId, string Title, string Message, bool Warning);

public sealed partial class WindowsWalkthroughSessions(OfflineLibraryClient library, IWalkthroughReader reader,
    NativeConfiguration configuration, INextMoeAccess auth, IGuideFlowLayout? flowLayout = null, ILocalReadingLibrary? localReading = null) : IGameGuideLaunch, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly List<Slot> slots = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Available> available = new(StringComparer.Ordinal);
    private sealed record Available(string WorkId, long Auth, DateTimeOffset At,
        ImmutableArray<WalkthroughSummary> Guides, string? Cursor)
    { public LocalReadingFiles Local { get; init; } = new([], []); }
    private bool disposed;
    private bool lightTheme;
    private int appearanceDpi = 96;
    public void SetAppearance(bool light, int dpi)
    {
        dpi = Math.Clamp(dpi, 96, 384);
        lock (gate)
        {
            if (disposed || lightTheme == light && appearanceDpi == dpi) return;
            lightTheme = light; appearanceDpi = dpi;
            foreach (var slot in slots) { slot.View.Write(212, light ? 1 : 0); slot.View.Write(216, dpi); }
        }
    }
    internal string? ComponentDirectoryForTest { get; init; }
    internal TimeSpan PreparationTimeout { get; init; } = TimeSpan.FromSeconds(15);
    private string ComponentDirectory => ComponentDirectoryForTest ?? Path.Combine(AppContext.BaseDirectory, "guide-overlay");
    private string ProfileRoot() => NativeProfileAccess.Validate(configuration);
    public bool Enabled { get; private set; } = true;
    public int VirtualKey { get; private set; } = WindowsGuideHotkeys.DefaultKey;
    public bool CanConfigure { get; private set; } = true;
    private const string PreferenceName = "native-guide-overlay-v1.json";
    private sealed record Preference(int Version, Guid ProfileId, Guid StoreId, bool Enabled, int VirtualKey)
    { public bool CustomShortcut { get; init; } public Dictionary<string, int>? Sources { get; init; } public Dictionary<string, string[]>? Notes { get; init; } public Dictionary<string, LocalReadingPosition>? Positions { get; init; } public Dictionary<string, OverlayPagePreference>? LastPages { get; init; } }
    private readonly Dictionary<string, int> sourcePreferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> notePreferences = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim preferenceGate = new(1, 1);
    public string Message { get; private set; } = "游戏内阅读：J 显示或隐藏";
    private GuideLaunchNotice? notice;
    public GuideLaunchNotice? Notice { get { lock (gate) return notice; } }
    private void Report(string gameId, string message, bool warning = false)
    {
        lock (gate)
        {
            Message = message;
            var title = library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId)?.Name ?? "";
            var next = new GuideLaunchNotice(gameId, title, message, warning);
            if (notice != next) notice = next;
        }
    }
    public Task InitializeAsync() => Task.Run(() => {
        var root = ProfileRoot(); var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
        try
        {
            var path = Path.Combine(root, PreferenceName);
            byte[] bytes;
            try { using var input = IsolatedLibrarySource.OpenChecked(path); if (input.Length > 2 * 1024 * 1024) throw new InvalidDataException(); bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); }
            catch (FileNotFoundException) { return; }
            var settings = JsonSerializer.Deserialize<Preference>(bytes) ?? throw new InvalidDataException();
            if (configuration.Profile is not NativeProfileStore profile || settings.Version != 1
                || settings.ProfileId != profile.Manifest.ProfileId || settings.StoreId != profile.Manifest.StoreId
                || !WindowsGuideHotkeys.IsSupported(settings.CustomShortcut ? settings.VirtualKey : WindowsGuideHotkeys.FromLegacy(settings.VirtualKey))
                || settings.Sources?.Count > 512 || settings.Sources?.Any(p => p.Key.Length > 200 || p.Value is < 0 or > 1) == true) throw new InvalidDataException();
            foreach (var pair in settings.Sources ?? []) sourcePreferences[pair.Key] = pair.Value;
            ValidateNotes(settings.Notes ?? []);
            foreach (var pair in settings.Notes ?? []) notePreferences[pair.Key] = pair.Value;
            LoadReadingPositions(settings.Positions ?? []);
            LoadOverlayPages(settings.LastPages ?? []);
            Configure(settings.Enabled, settings.CustomShortcut ? settings.VirtualKey : WindowsGuideHotkeys.FromLegacy(settings.VirtualKey));
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { lock (gate) { Enabled = false; CanConfigure = false; Message = "攻略显示设置无法安全读取，原文件已保留；网络攻略仍可阅读。"; } }
        finally { foreach (var handle in held) handle.Dispose(); }
    });
    public async Task ConfigureAsync(bool enabled, int virtualKey)
    {
        if (!CanConfigure || !WindowsGuideHotkeys.IsSupported(virtualKey)) throw new GuideHotkeyUnavailableException("请录入字母、数字、F1–F11 或符号键，可搭配 Ctrl、Shift、Alt；系统及翻页按键不可用。");
        if (enabled)
        {
            bool ownGuide; lock (gate) ownGuide = Enabled && virtualKey == VirtualKey && slots.Any(s => s.View.ReadInt32(44) == 1);
            if (!ownGuide) WindowsGuideHotkeys.RequireAvailable(virtualKey);
        }
        await preferenceGate.WaitAsync().ConfigureAwait(false);
        try { await PersistPreferencesAsync(enabled, virtualKey, sourcePreferences).ConfigureAwait(false); Configure(enabled, virtualKey); }
        finally { preferenceGate.Release(); }
    }
    public void Configure(bool enabled, int virtualKey)
    {
        if (!WindowsGuideHotkeys.IsSupported(virtualKey)) throw new ArgumentOutOfRangeException(nameof(virtualKey));
        lock (gate)
        {
            Enabled = enabled; VirtualKey = virtualKey;
            if (!enabled)
            {
                available.Clear(); foreach (var slot in slots) slot.Dispose(); slots.Clear();
                if (notice is { } previous) Report(previous.GameId, "游戏内攻略已关闭，可在游戏工具中重新开启。");
            }
            else foreach (var slot in slots) slot.View.Write(76, VirtualKey);
            Message = enabled ? $"游戏内阅读：{WindowsGuideHotkeys.Label(virtualKey)} 显示或隐藏" : "本次运行已关闭游戏内阅读";
        }
    }
    public async Task PrepareForGameAsync(string gameId, CancellationToken cancellation)
    {
        string work; long generation;
        var entry = library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId);
        if (entry is null) return;
        LocalReadingFiles files = new([], []);
        if (localReading is not null)
            try { files = await FindLocalFilesAsync(gameId, cancellation).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
        lock (gate)
        {
            if (disposed) return;
            if (!Enabled) { Report(gameId, "游戏内攻略已关闭，可在游戏工具中开启。"); return; }
            work = library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId)?.Metadata?.Id ?? "";
            generation = auth.AuthContext.Generation;
            if (available.TryGetValue(gameId, out var cached) && cached.WorkId == work
                && cached.Auth == generation && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(2))
            { available[gameId] = cached with { Local = files }; Report(gameId, "正在准备游戏内阅读…"); return; }
            available.Remove(gameId);
            Report(gameId, "正在查找此游戏的攻略…");
        }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        void Remember(ImmutableArray<WalkthroughSummary> summaries, string? next)
        {
            lock (gate)
            {
                if (disposed || !Enabled || cancellation.IsCancellationRequested || auth.AuthContext.Generation != generation
                    || (library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId)?.Metadata?.Id ?? "") != work) return;
                if (available.Count >= 64) available.Clear();
                available[gameId] = new(work, generation, DateTimeOffset.UtcNow, summaries, next) { Local = files };
            }
        }
        if (!CatalogReleaseFacts.Id(work))
        {
            Remember([], null);
            Report(gameId, !files.Guides.IsEmpty ? "已找到本地攻略，正在准备游戏内阅读。" : !files.Notes.IsEmpty ? "已找到游戏说明文件，可使用说明快捷键阅读。" : "尚未关联网络资料，也没有找到本地攻略或说明文件。", files.Guides.IsEmpty && files.Notes.IsEmpty);
            return;
        }
        try
        {
            // Like linked local details, a known work is read directly, independently of discovery filtering.
            var page = await reader.ListAsync(work, null, true, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
            Remember(page.Items, page.NextCursor);
            Report(gameId, page.Items.IsEmpty && !files.Guides.IsEmpty ? "没有网络攻略，已自动选用本地攻略。" : page.Items.IsEmpty ? "此作品暂无网络攻略。" : "已找到对应攻略，正在准备游戏内显示。", page.Items.IsEmpty && files.Guides.IsEmpty && files.Notes.IsEmpty);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { Remember([], null); Report(gameId, !files.Guides.IsEmpty ? "网络服务未响应，本次先使用本地攻略。" : "攻略服务暂未响应；本地说明文件仍可阅读。", true); }
        catch (OnlineException error) { Remember([], null); Report(gameId, Failure(error.Failure) + (!files.Guides.IsEmpty ? " 本次先使用本地攻略。" : ""), true); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        { Remember([], null); Report(gameId, "网络攻略暂不可用，本地资料仍可阅读。", true); }
    }
    public IGameGuideLaunchPlan? Prepare(LaunchSnapshot snapshot)
    {
        lock (gate)
        {
            if (disposed || !Enabled) return null;
            var entry = library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == snapshot.Game.GameId);
            if (entry is null) return null; string workId = entry.Metadata?.Id ?? "";
            if (!available.Remove(entry.Id, out var prepared) || prepared.Guides.IsEmpty && prepared.Local.Guides.IsEmpty && prepared.Local.Notes.IsEmpty
                || prepared.WorkId != workId || prepared.Auth != auth.AuthContext.Generation
                || DateTimeOffset.UtcNow - prepared.At >= TimeSpan.FromMinutes(2)) return null;
            if (slots.Count >= 16) { Report(entry.Id, "当前攻略会话过多，未启用本次攻略快捷键。", true); return null; }
            // Unique session names avoid collisions between profiles and PID reuse. Nothing is inherited by handle.
            try
            {
                var slot = new Slot(this, snapshot, entry.Name, workId, VirtualKey, prepared, lifetime.Token);
                slots.Add(slot); Report(entry.Id, "正在准备游戏内攻略…");
                slot.Pending = LoadAsync(slot, prepared.Cursor);
                return slot;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Report(entry.Id, "攻略显示组件未能准备，本次未启用攻略快捷键；请检查程序安装。", true); return null; }
        }
    }
    private async Task LoadAsync(Slot slot, string? cursor)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(slot.Token);
        budget.CancelAfter(PreparationTimeout);
        try
        {
            var summaries = ImmutableArray.CreateBuilder<WalkthroughSummary>(); summaries.AddRange(slot.Guides);
            var seen = new HashSet<string>(slot.Guides.Select(g => g.Id), StringComparer.Ordinal);
            while (cursor is not null)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (!seen.Add("cursor:" + cursor)) throw new InvalidDataException("Repeated cursor");
                var page = await reader.ListAsync(slot.WorkId, cursor, true, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
                foreach (var item in page.Items)
                {
                    if (summaries.Count >= 128) throw new InvalidDataException("Too many guides");
                    if (seen.Add(item.Id)) summaries.Add(item);
                }
                cursor = page.NextCursor;
            }
            lock (gate) { if (!Current(slot)) return; slot.Guides = summaries.ToImmutable(); }
            int initialPage = InitialOverlayPage(slot);
            if (initialPage != 0) await ChangeChannelAsync(slot, initialPage).ConfigureAwait(false);
            if (!slot.Guides.IsEmpty) await SelectAsync(slot, 0, budget.Token).ConfigureAwait(false);
            else if (slot.Channel == 0) Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "此作品暂无网络攻略。可切换本地攻略或说明文件。"));
        }
        catch (OperationCanceledException)
        { if (!slot.Token.IsCancellationRequested) Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title,
            budget.IsCancellationRequested ? "攻略准备超时，本次快捷键不可用；可先在网络攻略中阅读，下次启动时重试。" : "攻略准备已中断，本次快捷键不可用；可先在网络攻略中阅读。")); }
        catch (OnlineException error) { Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, Failure(error.Failure))); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        { Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "攻略数据暂不可用，游戏启动和计时不受影响。")); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "攻略准备未能完成，本次快捷键不可用；可先在网络攻略中阅读。")); }
    }
    private async Task SelectAsync(Slot slot, int index, CancellationToken preparation = default)
    {
        long revision = -1;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(slot.Token, preparation);
        budget.CancelAfter(PreparationTimeout);
        try
        {
            WalkthroughSummary guide;
            lock (gate)
            {
                if (!Current(slot) || index < 0 || index >= slot.Guides.Length) return;
                guide = slot.Guides[index]; slot.Selected = index; revision = ++slot.Revision;
                slot.ReadFailure = null; if (slot.Channel == 0) slot.HasDocument = false;
                slot.Write(GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "正在读取攻略…"));
            }
            var document = await reader.ReadAsync(guide.Id, slot.WorkId, true, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
            GuideFlowLayout? flow=null;
            if (flowLayout is not null)
                try { flow=await flowLayout.CreateAsync(document,budget.Token).WaitAsync(budget.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException && !slot.Token.IsCancellationRequested) { /* Body remains usable if optional layout fails. */ }
            lock (gate)
            {
                if (!Current(slot) || revision != slot.Revision) return;
                slot.Write(GuideOverlayDocument.From(slot.GameId, slot.Title, document) with { Flow=flow });
                if (slot.Channel == 0) slot.HasDocument = true; ReportReady(slot);
            }
        }
        catch (OperationCanceledException)
        {
            if (!slot.Token.IsCancellationRequested) SelectionFailed(slot, revision, budget.IsCancellationRequested
                ? "攻略读取超时，本次快捷键不可用；可先在网络攻略中阅读，下次启动时重试。"
                : "攻略读取已中断，本次快捷键不可用；可先在网络攻略中阅读。");
        }
        catch (OnlineException error) { lock (gate) { if (revision == slot.Revision) Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, Failure(error.Failure))); } }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        { lock (gate) { if (revision == slot.Revision) Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "攻略过大或结构不受支持，已保留游戏运行。")); } }
        catch (Exception error) when (error is not OutOfMemoryException)
        { SelectionFailed(slot, revision, "攻略准备未能完成，本次快捷键不可用；可先在网络攻略中阅读。"); }
    }
    private void SelectionFailed(Slot slot, long revision, string message)
    { lock (gate) { if (revision == slot.Revision) Update(slot, GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, message)); } }
    private void ReportReady(Slot slot)
    {
        lock (gate)
        {
            if (!Current(slot)) return;
            if (slot.ReadFailure is { } failure) Report(slot.GameId, failure, true);
            else if (slot.Warning is { } warning) Report(slot.GameId, warning, true);
            else if (slot.HasDocument) Report(slot.GameId, slot.Pid == 0 ? "攻略已载入，正在等待游戏显示连接…"
                : "攻略已准备：" + WindowsGuideHotkeys.Label(VirtualKey) + " 显示或隐藏");
        }
    }
    private static string Failure(OnlineFailure failure) => failure switch
    {
        OnlineFailure.NotFound => "攻略当前不可见或已移除，请核对作品关联与资料源访问状态。",
        OnlineFailure.RateLimited or OnlineFailure.QuotaExceeded => "攻略服务暂时限流，请稍后重新启动攻略会话。",
        OnlineFailure.Offline or OnlineFailure.Timeout => "攻略读取失败，请检查网络后重试。",
        _ => "攻略暂未能读取，游戏启动和计时不受影响。"
    };
    private bool Current(Slot slot) => !disposed && !slot.Cancel.IsCancellationRequested && slots.Contains(slot)
        && auth.AuthContext.Generation == slot.AuthGeneration
        && library.CurrentSnapshot?.Entries.Any(e => e.Id == slot.GameId && (e.Metadata?.Id ?? "") == slot.WorkId) == true;
    private void Update(Slot slot, GuideOverlayDocument document)
    { lock (gate) { if (!Current(slot)) return; slot.Write(document); if (slot.Channel == 0) { slot.HasDocument = false; slot.ReadFailure = document.Status; Report(slot.GameId, document.Status, true); if (!slot.Local.Guides.IsEmpty) slot.Pending = ChangeChannelAsync(slot, 1); } } }
    // Executed from the presentation's existing low-frequency launch timer.
    public void Tick(LaunchStatus? launch = null)
    {
        lock (gate)
        {
            if (disposed) return;
            foreach (var slot in slots.ToArray())
            {
                CaptureReadingPosition(slot);
                CaptureOverlayPage(slot);
                if (!Current(slot))
                {
                    if (notice?.GameId == slot.GameId && slots.LastOrDefault(s => s.GameId == slot.GameId) == slot)
                        Report(slot.GameId, auth.AuthContext.Generation != slot.AuthGeneration
                            ? "登录状态已变化，本次攻略会话已结束；下次启动游戏时会重新准备。"
                            : "作品关联已变化，本次攻略会话已结束；下次启动游戏时会重新准备。", true);
                    slots.Remove(slot); slot.Dispose(); continue;
                }
                bool sameLaunch = launch?.Snapshot?.OperationId == slot.Snapshot.OperationId;
                if (sameLaunch && launch?.Current is { } current && (current.Pid != slot.Pid || current.CreationFileTime != slot.Created)
                    && launch.Tracking is ProcessTrackingState.RunningConfirmed or ProcessTrackingState.RunningLimited)
                {
                    try { slot.AttachObserved(current); }
                    catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    { slot.Warning = "攻略未能连接当前游戏进程，可先在网络攻略中阅读。"; Report(slot.GameId, slot.Warning, true); }
                }
                if (slot.Pid == 0)
                {
                    if (DateTimeOffset.UtcNow - slot.CreatedAt < TimeSpan.FromSeconds(30)) continue;
                    Report(slot.GameId, "未能关联正在运行的游戏，本次攻略会话已结束；可先在网络攻略中阅读。", true);
                    slots.Remove(slot); slot.Dispose(); continue;
                }
                bool handoff = sameLaunch && launch?.Tracking == ProcessTrackingState.ObservingHandoff
                    && DateTimeOffset.UtcNow - slot.AttachedAt < TimeSpan.FromSeconds(30);
                // A process can exit just before the launch monitor publishes ObservingHandoff.
                // Preserve the session through that bounded transition; only the monitor may identify its child.
                handoff |= sameLaunch && launch?.Tracking is ProcessTrackingState.Attaching or ProcessTrackingState.RunningConfirmed or ProcessTrackingState.RunningLimited
                    && DateTimeOffset.UtcNow - slot.AttachedAt < TimeSpan.FromSeconds(30);
                if (!Alive(slot.Pid, slot.Created) && !handoff)
                {
                    if (notice?.GameId == slot.GameId && slots.LastOrDefault(s => s.GameId == slot.GameId) == slot)
                        Report(slot.GameId, "本次攻略会话已结束；若游戏仍在运行，可先使用网络攻略。", true);
                    slots.Remove(slot); slot.Dispose(); continue;
                }
                slot.View.Write(32, Environment.TickCount64);
                int command = slot.View.ReadInt32(56);
                if (command != 0)
                {
                    int argument = slot.View.ReadInt32(60); slot.View.Write(56, 0);
                    if (command == 1) slot.Pending = SelectAsync(slot, argument);
                    else if (command == 2 && argument is >= 0 and <= 2) slot.Pending = ChangeChannelAsync(slot, argument);
                    else if (command == 3 && slot.Channel != 0) slot.Pending = ReadLocalAsync(slot, argument, 0, restore: true);
                    else if (command == 4 && slot.Channel != 0) slot.Pending = ReadLocalAsync(slot, slot.LocalIndex, argument);
                    else if (command == 5 && slot.Channel != 0 && argument is -1 or 1) slot.Pending = ReadLocalAsync(slot, slot.LocalIndex, slot.LocalPagePositions[slot.Channel] + argument, startProgress: argument < 0 ? 1 : 0);
                }
                int keyStatus = slot.View.ReadInt32(44);
                if (keyStatus == 3) { slot.KeyConflict = true; Report(slot.GameId, "阅读快捷键尚未连接，可先在游戏工具中阅读。", true); }
                if (keyStatus == 2) { slot.KeyConflict = true; Report(slot.GameId, WindowsGuideHotkeys.Label(VirtualKey) + " 已被其他程序占用，请在游戏工具 → 游戏内攻略中更换快捷键。", true); }
                else if (keyStatus == 1 && slot.KeyConflict)
                { slot.KeyConflict = false; ReportReady(slot); }
            }
            QueueReadingSave();
        }
    }
    private static bool Alive(int pid, long created)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToFileTimeUtc() == created; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private bool StartHelper(Slot slot, string mode, string? module = null)
    {
        try { return StartHelperCore(slot, mode, module); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { slot.Warning = "攻略显示组件未能启动，请在游戏工具中检查或修复程序安装。"; Report(slot.GameId, slot.Warning, true); return false; }
    }
    private bool StartHelperCore(Slot slot, string mode, string? module)
    {
        var binary = Path.Combine(ComponentDirectory, slot.Architecture, "TheOrder.GuideHost.exe");
        if (!File.Exists(binary)) { slot.Warning = "攻略显示组件缺失，请修复程序安装。"; Report(slot.GameId, slot.Warning, true); return false; }
        var arguments = new List<string>();
        foreach (var argument in new[] { mode, slot.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            slot.Created.ToString(System.Globalization.CultureInfo.InvariantCulture), slot.Name }) arguments.Add(argument);
        if (module is not null) arguments.Add(module);
        using var helper = WindowsGuideHelperProcess.Start(binary, arguments);
        if (mode == "--inject")
        {
            if (!helper.WaitForExit(8000))
            {
                // A delayed helper must never edit imports after the game has resumed.
                try { helper.Kill(); } catch (InvalidOperationException) { }
                helper.WaitForExit(); slot.Warning = "全屏攻略准备超时；窗口或无边框模式可尝试攻略快捷键。";
            }
            else if (helper.ExitCode != 0) slot.Warning = "此游戏暂未能启用全屏攻略，可改用窗口或无边框全屏。";
            if (slot.Warning is not null) Report(slot.GameId, slot.Warning, true);
        }
        else if (helper.WaitForExit(150))
        { slot.Warning = "攻略显示组件未能连接游戏，可先在网络攻略中阅读。"; Report(slot.GameId, slot.Warning, true); return false; }
        return true;
    }
    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (gate)
        {
            if (disposed) return; disposed = true; lifetime.Cancel();
            pending = slots.Select(s => s.Pending ?? Task.CompletedTask).ToArray();
            foreach (var slot in slots) { CaptureReadingPosition(slot); CaptureOverlayPage(slot); slot.Dispose(); } slots.Clear();
        }
        try { await Task.WhenAll(pending).ConfigureAwait(false); } catch (OperationCanceledException) { }
        await SaveReadingPositionsAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }
    private sealed class Slot : IGameGuideLaunchPlan, IDisposable
    {
        private readonly WindowsWalkthroughSessions owner;
        public string Name { get; } = "Local\\TheOrder.Guide." + Guid.NewGuid().ToString("N");
        public LaunchSnapshot Snapshot { get; }
        public string GameId => Snapshot.Game.GameId;
        public string Title { get; }
        public string WorkId { get; }
        public long AuthGeneration { get; }
        public MemoryMappedFile Mapping { get; }
        public MemoryMappedViewAccessor View { get; }
        public CancellationTokenSource Cancel { get; }
        public CancellationToken Token { get; }
        public Task? Pending { get; set; }
        public ImmutableArray<WalkthroughSummary> Guides { get; set; } = [];
        public int Selected { get; set; }
        public long Revision { get; set; }
        public int Pid { get; private set; }
        public long Created { get; private set; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public string Architecture { get; private set; } = "x64";
        public DateTimeOffset AttachedAt { get; private set; }
        public bool KeyConflict { get; set; }
        public bool WasVisible { get; set; }
        public LocalReadingFiles Local { get; set; }
        public int Channel { get; set; }
        public int LocalIndex { get; set; }
        public int[] LocalFilePositions { get; } = new int[3];
        public int[] LocalPagePositions { get; } = new int[3];
        public long ReaderRevision { get; set; }
        public bool ReadingReady { get; set; }
        public int WrittenSequence => sequence;
        public GuideOverlayDocument? NetworkDocument { get; private set; }
        public readonly SemaphoreSlim DocumentGate = new(1, 1);
        public ILocalReadingDocument? LocalDocument { get; set; }
        public string? LocalPath { get; set; }
        public string? Warning { get; set; }
        public string? ReadFailure { get; set; }
        public bool HasDocument { get; set; }
        public IReadOnlyDictionary<string, string> Environment { get; }
        private int sequence, closed;
        private readonly WindowsGuidePayload payload;
        private readonly List<IDisposable> directories = [];
        public Slot(WindowsWalkthroughSessions owner, LaunchSnapshot snapshot, string title, string workId, int key, Available prepared, CancellationToken cancellation)
        {
            this.owner = owner; Snapshot = snapshot; Title = title; WorkId = workId;
            AuthGeneration = prepared.Auth; Guides = prepared.Guides;
            Local = new(prepared.Local.Guides.Where(f => f.InstallationId.Length == 0 || f.InstallationId == snapshot.Installation.Id).ToImmutableArray(), prepared.Local.Notes.Where(f => f.InstallationId.Length == 0 || f.InstallationId == snapshot.Installation.Id).ToImmutableArray());
            payload = new WindowsGuidePayload(owner.ComponentDirectory);
            try
            {
            Cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation); Token = Cancel.Token;
            Mapping = MemoryMappedFile.CreateNew(Name, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.ReadWrite);
            View = Mapping.CreateViewAccessor(); View.Write(0, GuideOverlayWire.Magic); View.Write(4, GuideOverlayWire.Version);
            View.Write(76, key); View.Write(88, .94f); View.Write(92, 22f); View.Write(112, System.Environment.ProcessId);
            View.Write(148, (Guides.IsEmpty ? 0 : 1) | (Local.Guides.IsEmpty ? 0 : 2) | (Local.Notes.IsEmpty ? 0 : 4));
            View.Write(72, owner.LastOverlayPage(GameId)?.Graph == false ? 0 : 1);
            View.Write(212, owner.lightTheme ? 1 : 0); View.Write(216, owner.appearanceDpi);
            View.Write(32, System.Environment.TickCount64);
            // Runtime configuration lives in a new session directory, never in a game directory.
            var root = owner.ProfileRoot();
            directories.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(root));
            var sessionRoot = Path.Combine(root, "guide-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);
            WindowsGuidePayload.CheckDirectory(sessionRoot);
            directories.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(sessionRoot));
            var addonPath = owner.ComponentDirectory.Replace('\\', '/');
            File.WriteAllText(Path.Combine(sessionRoot, "ReShade.ini"),
                "[ADDON]\nAddonPath=" + addonPath + "\nDisabledAddons=Generic Depth,Effect Runtime Sync\n" +
                "[GENERAL]\nNoReloadOnInit=1\nPerformanceMode=1\n" +
                "[INPUT]\nKeyOverlay=0,0,0,0\nKeyScreenshot=0,0,0,0\n" +
                "[OVERLAY]\nTutorialProgress=4\nShowClock=0\nShowFPS=0\nShowFrameTime=0\n" +
                "[STYLE]\nFont=C:/Windows/Fonts/msyh.ttc\nLatinFont=C:/Windows/Fonts/segoeui.ttf\nEditorFont=C:/Windows/Fonts/consola.ttf\nFontSize=20\n", new UTF8Encoding(false));
            Environment = new Dictionary<string, string> { ["THE_ORDER_GUIDE_SESSION"] = Name,
                ["RESHADE_BASE_PATH_OVERRIDE"] = sessionRoot, ["RESHADE_DISABLE_LOADING_CHECK"] = "1",
                ["RESHADE_DISABLE_LOGGING"] = "1" };
            Write(GuideOverlayDocument.Message(GameId, workId, title, "正在读取对应攻略…"));
            }
            catch
            {
                View?.Dispose(); Mapping?.Dispose(); Cancel?.Dispose(); payload.Dispose();
                foreach (var handle in directories) handle.Dispose(); throw;
            }
        }
        public void Attach(int pid, long creationFileTime)
        {
            lock (owner.gate)
            {
                if (owner.disposed || Cancel.IsCancellationRequested) return;
                Pid = pid; Created = creationFileTime; AttachedAt = DateTimeOffset.UtcNow; View.Write(40, pid); View.Write(48, creationFileTime);
                using var process = Process.GetProcessById(pid);
                if (IsWow64Process2(process.Handle, out var machine, out _)) Architecture = machine == 0x14c ? "x86" : "x64";
            }
            var module = Path.Combine(owner.ComponentDirectory, Architecture, "TheOrder.GuideRuntime.dll");
            if (WindowsGuidePayload.AllowsNative(Snapshot)) owner.StartHelper(this, "--inject", module);
            else { Warning = "攻略已准备；当前启动方式使用窗口攻略，请使用窗口或无边框全屏，并按 " + WindowsGuideHotkeys.Label(owner.VirtualKey) + "。"; owner.Report(GameId, Warning, true); }
            if (owner.StartHelper(this, "--desktop")) owner.ReportReady(this);
        }
        public void AttachObserved(ProcessIdentity identity)
        {
            lock (owner.gate)
            {
                if (owner.disposed || Cancel.IsCancellationRequested || Pid == identity.Pid && Created == identity.CreationFileTime
                    || !Alive(identity.Pid, identity.CreationFileTime)) return;
                Pid = identity.Pid; Created = identity.CreationFileTime; AttachedAt = DateTimeOffset.UtcNow;
                View.Write(40, Pid); View.Write(48, Created); View.Write(24, 0L);
                using var process = Process.GetProcessById(Pid);
                if (IsWow64Process2(process.Handle, out var machine, out _)) Architecture = machine == 0x14c ? "x86" : "x64";
                if (!owner.StartHelper(this, "--desktop")) return;
                Warning = "攻略已随游戏继续显示；此启动方式请使用窗口或无边框全屏，并按 " + WindowsGuideHotkeys.Label(owner.VirtualKey) + "。";
                owner.Report(GameId, ReadFailure ?? Warning, true);
            }
        }
        public void FailedBeforeCreation() { lock (owner.gate) { owner.slots.Remove(this); Dispose(); } }
        public void Write(GuideOverlayDocument document)
        {
            if (document.Reading is null)
            {
                NetworkDocument = document;
                if (Channel != 0) return;
                document = document with { Reading = new(0, [], 0, 0, 0, null, HasNetwork: !Guides.IsEmpty, HasGuides: !Local.Guides.IsEmpty, HasNotes: !Local.Notes.IsEmpty) };
            }
            var sources=Guides.Select(g => g.Source + " · " + g.Language + " · " + g.Release).ToArray();
            byte[] data;
            try { data=GuideOverlayWire.Encode(document,sources); }
            catch (InvalidDataException) when (document.Flow is not null)
            { data=GuideOverlayWire.Encode(document with {Flow=null},sources); } // Optional geometry must not displace a valid body.
            View.Write(8, ++sequence); Thread.MemoryBarrier(); View.WriteArray(GuideOverlayWire.HeaderBytes, data, 0, data.Length);
            View.Write(12, data.Length); View.Write(84, Selected); View.Write(176, Channel); Thread.MemoryBarrier(); View.Write(8, ++sequence);
        }
        public void Dispose() { if (Interlocked.Exchange(ref closed, 1) != 0) return; Cancel.Cancel(); View.Write(16, 0); View.Write(32, 0L); View.Dispose(); Mapping.Dispose(); Cancel.Dispose(); payload.Dispose(); foreach (var handle in directories) handle.Dispose(); _ = CloseReadingAsync(); }
        private async Task CloseReadingAsync() { await DocumentGate.WaitAsync().ConfigureAwait(false); try { LocalDocument?.Dispose(); LocalDocument = null; } finally { DocumentGate.Release(); } }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
}
