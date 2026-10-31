using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed partial class WindowsWalkthroughSessions
{
    public int SourceFor(string gameId) { lock (gate) return sourcePreferences.GetValueOrDefault(gameId); }
    public async Task<LocalReadingFiles> FindLocalFilesAsync(string gameId, CancellationToken cancellation)
    {
        var entry = library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId);
        if (entry is null || localReading is null) return new([], []);
        var files = await localReading.FindAsync(entry, cancellation).ConfigureAwait(false);
        lock (gate) return files with { Notes = files.Notes.AddRange((notePreferences.GetValueOrDefault(gameId) ?? []).Select(path => new LocalReadingFile(path, Path.GetFileName(path), "note"))).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToImmutableArray() };
    }
    public Task<ILocalReadingDocument> OpenLocalFileAsync(LocalReadingFile file, CancellationToken cancellation) =>
        localReading?.OpenAsync(file, cancellation) ?? throw new InvalidOperationException("本地阅读器未配置。");
    private Task PersistPreferencesAsync(bool enabled, int key, IReadOnlyDictionary<string, int> sources, Dictionary<string, string[]>? notes = null) => Task.Run(async () =>
    {
        var root = ProfileRoot(); var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
        try
        {
            if (configuration.Profile is not NativeProfileStore profile) throw new InvalidOperationException(); profile.ValidateAccess();
            Dictionary<string, LocalReadingPosition> positions; Dictionary<string, OverlayPagePreference> lastPages; lock (gate) { positions = new(readingPositions); lastPages = new(overlayPages); }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Preference(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, enabled, key) { CustomShortcut = true, Sources = new(sources), Notes = notes ?? new(notePreferences), Positions = positions, LastPages = lastPages });
            if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("说明文件设置超过容量限制。");
            await WindowsAtomicFile.WriteAsync(Path.Combine(root, PreferenceName), bytes).ConfigureAwait(false);
        }
        finally { foreach (var lease in held) lease.Dispose(); }
    });
    private static void ValidateNotes(Dictionary<string, string[]> notes)
    {
        if (notes.Count > 512) throw new InvalidDataException("说明文件记录过多。");
        foreach (var pair in notes)
        {
            if (pair.Key.Length > 200 || pair.Value is null || pair.Value.Length > 20 || pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pair.Value.Length) throw new InvalidDataException("说明文件记录无效。");
            foreach (var path in pair.Value) { TheOrder.Domain.LibraryExtras.Path(path); if (!LocalDocuments.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new InvalidDataException("说明文件类型不受支持。"); }
        }
    }
    public async Task ConfigureNotesAsync(string gameId, IEnumerable<string> paths)
    {
        if (!CanConfigure || library.CurrentSnapshot?.Entries.Any(e => e.Id == gameId) != true) throw new InvalidOperationException("作品已变化。");
        await preferenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Dictionary<string, string[]> next; lock (gate) next = new(notePreferences);
            var chosen = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); if (chosen.Length == 0) next.Remove(gameId); else next[gameId] = chosen; ValidateNotes(next);
            await PersistPreferencesAsync(Enabled, VirtualKey, sourcePreferences, next).ConfigureAwait(false);
            lock (gate) { notePreferences.Clear(); foreach (var pair in next) notePreferences.Add(pair.Key, pair.Value); available.Remove(gameId); }
        }
        finally { preferenceGate.Release(); }
        await RefreshLocalFilesAsync(gameId, CancellationToken.None).ConfigureAwait(false);
    }
    public async Task RefreshLocalFilesAsync(string gameId, CancellationToken cancellation)
    {
        var files = await FindLocalFilesAsync(gameId, cancellation).ConfigureAwait(false); Slot[] active;
        lock (gate)
        {
            available.Remove(gameId); active = slots.Where(s => Current(s) && s.GameId == gameId).ToArray();
            foreach (var slot in active) { slot.Local = files; slot.View.Write(148, (slot.Guides.IsEmpty ? 0 : 1) | (files.Guides.IsEmpty ? 0 : 2) | (files.Notes.IsEmpty ? 0 : 4)); }
        }
        foreach (var slot in active) await ChangeChannelAsync(slot, slot.Channel).ConfigureAwait(false);
    }
    public async Task ConfigureSourceAsync(string gameId, int source)
    {
        if (!CanConfigure || source is < 0 or > 1 || library.CurrentSnapshot?.Entries.Any(e => e.Id == gameId) != true) throw new InvalidOperationException("作品或攻略设置已变化。");
        await preferenceGate.WaitAsync().ConfigureAwait(false);
        Slot[] active;
        try
        {
            lock (gate) if (sourcePreferences.GetValueOrDefault(gameId) == source) return;
            Dictionary<string, int> next; lock (gate) next = new(sourcePreferences, StringComparer.Ordinal);
            if (source == 0) next.Remove(gameId); else next[gameId] = source;
            if (next.Count > 512) throw new InvalidDataException("攻略来源偏好过多。");
            await PersistPreferencesAsync(Enabled, VirtualKey, next).ConfigureAwait(false);
            lock (gate) { sourcePreferences.Clear(); foreach (var pair in next) sourcePreferences.Add(pair.Key, pair.Value); overlayPages[gameId] = new(source, overlayPages.GetValueOrDefault(gameId)?.Graph ?? true); positionRevision++; active = slots.Where(s => s.GameId == gameId).ToArray(); }
        }
        finally { preferenceGate.Release(); }
        foreach (var slot in active) await ChangeChannelAsync(slot, source).ConfigureAwait(false);
    }
    private async Task ChangeChannelAsync(Slot slot, int channel)
    {
        lock (gate)
        {
            if (!Current(slot) || channel is < 0 or > 2) return;
            CaptureReadingPosition(slot); slot.Channel = channel; slot.ReaderRevision++; slot.ReadingReady = false;
            if (channel != 2) slot.View.Write(144, channel);
            if (channel == 0)
            {
                slot.Write(slot.NetworkDocument ?? GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "此作品暂无网络攻略。可选择本地攻略或说明文件。"));
                slot.HasDocument = slot.NetworkDocument?.Routes.IsEmpty == false;
                return;
            }
        }
        var choices = channel == 1 ? slot.Local.Guides : slot.Local.Notes;
        string? previous = LastReadingFile(slot.GameId, channel, choices);
        int index = previous is null ? 0 : Array.FindIndex(choices.ToArray(), f => f.Path == previous);
        await ReadLocalAsync(slot, Math.Max(0, index), 0, restore: true).ConfigureAwait(false);
    }
    private async Task ReadLocalAsync(Slot slot, int fileIndex, int pageIndex, bool restore = false, float startProgress = 0)
    {
        long revision; int channel; ImmutableArray<LocalReadingFile> files;
        lock (gate)
        {
            if (!Current(slot) || slot.Channel == 0) return; channel = slot.Channel; files = channel == 1 ? slot.Local.Guides : slot.Local.Notes;
            if (fileIndex < 0 || fileIndex >= Math.Max(1, files.Length) || pageIndex is < 0 or > 2000) return;
            CaptureReadingPosition(slot); revision = ++slot.ReaderRevision; slot.LocalIndex = fileIndex; slot.ReadingReady = false;
            Write(null, 0, files.IsEmpty ? channel == 1 ? "尚未选择本地攻略。请在游戏工具 → 本地攻略中添加文件。" : "尚未选择说明文件。请在游戏工具 → 说明文件中添加文件。" : "正在读取…");
        }
        if (files.IsEmpty || localReading is null) return;
        bool held = false;
        try
        {
            await slot.DocumentGate.WaitAsync(slot.Token).ConfigureAwait(false); held = true;
            if (slot.Token.IsCancellationRequested) return;
            lock (gate) { if (!Current(slot) || slot.ReaderRevision != revision) return; }
            var file = files[fileIndex];
            var saved = restore ? ReadingPosition(slot.GameId, channel, file.Path) : null;
            if (slot.LocalPath != file.Path || slot.LocalDocument is null)
            {
                slot.LocalDocument?.Dispose(); slot.LocalDocument = null; slot.LocalPath = null;
                slot.LocalDocument = await localReading.OpenAsync(file, slot.Token).ConfigureAwait(false); slot.LocalPath = file.Path;
            }
            if (saved is not null) pageIndex = Math.Min(saved.Page, slot.LocalDocument.PageCount - 1);
            if (pageIndex >= slot.LocalDocument.PageCount) throw new InvalidDataException("此页不存在，请选择有效页码。");
            var page = await slot.LocalDocument.PageAsync(pageIndex, slot.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (!Current(slot) || revision != slot.ReaderRevision || channel != slot.Channel) return;
                Write(page, slot.LocalDocument.PageCount, slot.LocalDocument.Notice, saved?.Progress ?? startProgress, saved?.Zoom ?? 1);
                slot.LocalFilePositions[channel] = fileIndex; slot.LocalPagePositions[channel] = pageIndex;
                StorePosition(slot.GameId, channel, file.Path, pageIndex, saved?.Progress ?? startProgress, saved?.Zoom ?? 1, touch: true);
                slot.ReadingReady = true;
                slot.HasDocument = true; slot.ReadFailure = null; Report(slot.GameId, (channel == 1 ? "本地攻略" : "说明文件") + "已准备：" + WindowsGuideHotkeys.Label(VirtualKey) + " 显示或隐藏");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (gate)
                if (Current(slot) && revision == slot.ReaderRevision && channel == slot.Channel)
                    Write(null, 0, error is InvalidDataException ? error.Message : "文件无法读取，可能已移动、已加密或格式损坏。原文件保持不变，可选择其他文件。");
        }
        finally { if (held) slot.DocumentGate.Release(); }
        void Write(ReadingPage? page, int count, string notice, float progress = 0, float zoom = 1)
        {
            slot.Write(GuideOverlayDocument.Message(slot.GameId, slot.WorkId, slot.Title, "") with
            { Reading = new(channel, files.Select(f => f.Name).ToImmutableArray(), fileIndex, pageIndex, count, page, notice, !slot.Guides.IsEmpty, !slot.Local.Guides.IsEmpty, !slot.Local.Notes.IsEmpty, progress, zoom) });
        }
    }
}
