using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed record OverlayPagePreference(int Channel, bool Graph);

public sealed partial class WindowsWalkthroughSessions
{
    private readonly Dictionary<string, LocalReadingPosition> readingPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OverlayPagePreference> overlayPages = new(StringComparer.Ordinal);
    private long positionRevision, savedPositionRevision; private Task positionSave = Task.CompletedTask;
    private DateTimeOffset lastPositionSave;
    private void LoadOverlayPages(Dictionary<string, OverlayPagePreference> pages)
    {
        if (pages.Count > 512 || pages.Any(p => p.Key.Length > 200 || p.Value is null || p.Value.Channel is < 0 or > 2)) throw new InvalidDataException("阅读页面记录无效。");
        foreach (var pair in pages) overlayPages.Add(pair.Key, pair.Value);
    }
    public OverlayPagePreference? LastOverlayPage(string gameId) { lock (gate) return overlayPages.GetValueOrDefault(gameId); }
    private int InitialOverlayPage(Slot slot)
    {
        int selected = LastOverlayPage(slot.GameId)?.Channel ?? SourceFor(slot.GameId);
        if (selected == 2 && slot.Local.Notes.IsEmpty) selected = SourceFor(slot.GameId);
        if (selected == 1 && slot.Local.Guides.IsEmpty) selected = 0;
        if (selected != 2 && slot.Guides.IsEmpty && !slot.Local.Guides.IsEmpty) selected = 1;
        return selected;
    }
    private void CaptureOverlayPage(Slot slot)
    {
        bool visible = slot.View.ReadInt32(16) != 0; if (visible || slot.View.ReadInt32(180) != 0) slot.WasVisible = true; if (!slot.WasVisible) return;
        int channel = slot.View.ReadInt32(176); if (channel is < 0 or > 2) return;
        bool graph = slot.View.ReadInt32(72) != 0; var previous = overlayPages.GetValueOrDefault(slot.GameId);
        if (previous?.Channel == channel && previous.Graph == graph) return;
        if (overlayPages.Count >= 512 && !overlayPages.ContainsKey(slot.GameId)) overlayPages.Remove(overlayPages.Keys.First());
        overlayPages[slot.GameId] = new(channel, graph); positionRevision++;
    }
    private static string PositionKey(string game, int channel, string path) => game + "/" + channel + "/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
    private void LoadReadingPositions(Dictionary<string, LocalReadingPosition> positions)
    {
        if (positions.Count > 1024 || positions.Any(p => p.Key.Length > 280 || p.Value is null || p.Value.Page is < 0 or > 1999 || !float.IsFinite(p.Value.Progress) || p.Value.Progress is < 0 or > 1 || !float.IsFinite(p.Value.Zoom) || p.Value.Zoom is < .6f or > 4 || p.Value.At < 0)) throw new InvalidDataException("阅读位置记录无效。");
        foreach (var pair in positions) readingPositions.Add(pair.Key, pair.Value);
    }
    public LocalReadingPosition? ReadingPosition(string gameId, int channel, string path)
    { lock (gate) return readingPositions.GetValueOrDefault(PositionKey(gameId, channel, path)); }
    public string? LastReadingFile(string gameId, int channel, ImmutableArray<LocalReadingFile> files)
    { lock (gate) return files.Select(f => (f.Path, Position: readingPositions.GetValueOrDefault(PositionKey(gameId, channel, f.Path)))).Where(f => f.Position is not null).OrderByDescending(f => f.Position!.At).FirstOrDefault().Path; }
    public void RememberReadingPosition(string gameId, int channel, string path, int page, float progress, float zoom, bool touch = false)
    {
        if (channel is not (1 or 2) || page is < 0 or > 1999 || !float.IsFinite(progress) || !float.IsFinite(zoom)) return;
        lock (gate)
        {
            if (disposed || !CanConfigure) return;
            var entry = library.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId); if (entry is null) return;
            var allowed = channel == 1 ? entry.GuidePaths : (notePreferences.GetValueOrDefault(gameId) ?? []).ToImmutableArray();
            if (!allowed.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) return;
            StorePosition(gameId, channel, path, page, progress, zoom, touch);
        }
    }
    private void StorePosition(string game, int channel, string path, int page, float progress, float zoom, bool touch = false)
    {
        string key = PositionKey(game, channel, path); progress = MathF.Round(Math.Clamp(progress, 0, 1), 4); zoom = MathF.Round(Math.Clamp(zoom, .6f, 4), 3);
        var previous = readingPositions.GetValueOrDefault(key);
        if (!touch && previous is not null && previous.Page == page && previous.Progress == progress && previous.Zoom == zoom) return;
        if (readingPositions.Count >= 1024 && !readingPositions.ContainsKey(key)) readingPositions.Remove(readingPositions.MinBy(p => p.Value.At).Key);
        readingPositions[key] = new(page, progress, zoom, DateTimeOffset.UtcNow.UtcTicks); positionRevision++;
    }
    private void CaptureReadingPosition(Slot slot)
    {
        if (!slot.ReadingReady || slot.Channel is not (1 or 2) || slot.LocalPath is null || slot.View.ReadInt32(116) != slot.WrittenSequence || slot.View.ReadInt32(248) != 4) return;
        int page = slot.LocalPagePositions[slot.Channel], measuredPage = slot.View.ReadInt32(244), maximum = slot.View.ReadInt32(240), offset = slot.View.ReadInt32(200), zoom = slot.View.ReadInt32(108);
        if (page != measuredPage || maximum < 0 || offset < 0 || zoom is < 600 or > 4000) return;
        StorePosition(slot.GameId, slot.Channel, slot.LocalPath, page, maximum > 0 ? (float)offset / maximum : 0, zoom / 1000f);
    }
    private void QueueReadingSave()
    {
        if (!CanConfigure || configuration.Profile is null || savedPositionRevision == positionRevision || !positionSave.IsCompleted || DateTimeOffset.UtcNow - lastPositionSave < TimeSpan.FromSeconds(2)) return;
        lastPositionSave = DateTimeOffset.UtcNow; positionSave = SaveReadingPositionsAsync();
    }
    private async Task SaveReadingPositionsAsync()
    {
        if (!CanConfigure || configuration.Profile is null) return;
        await preferenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            long revision; Dictionary<string, int> sources; lock (gate) { revision = positionRevision; sources = new(sourcePreferences); }
            if (revision == savedPositionRevision) return;
            await PersistPreferencesAsync(Enabled, VirtualKey, sources).ConfigureAwait(false);
            lock (gate) savedPositionRevision = revision;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NativeProfileException) { /* Retain the prior atomic settings file; retry on the next reading change. */ }
        finally { preferenceGate.Release(); }
    }
}
