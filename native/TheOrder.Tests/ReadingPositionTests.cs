using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class ReadingPositionTests
{
    [Theory] [InlineData(0x6004b)] [InlineData(0x77)]
    public async Task CustomShortcutSurvivesRestartAndPreservesReadingPreferences(int shortcut)
    {
        string folder = Path.Combine(Path.GetTempPath(), "TheOrder-custom-shortcut-" + Guid.NewGuid().ToString("N"));
        try
        {
            var codec = new NativeLibraryCodec(); using var profile = NativeProfileStore.Open(new(NativeProfileKind.Test, folder), codec);
            var persistence = profile.CreateLibraryPersistence();
            try { await persistence.SaveAsync(persistence.ReadMain()!, codec.Encode([SessionTestRig.Entry()], SessionTestRig.Date)); }
            finally { (persistence as IDisposable)?.Dispose(); }
            var configuration = profile.Configuration(true);
            await using var client = new OfflineLibraryClient(new IsolatedLibraryFactory(codec, configuration), new LegacyClientTextRules()); await client.LoadAsync(default);
            await using (var first = new WindowsWalkthroughSessions(client, new NoNetwork(), configuration, new Access()))
            {
                await first.InitializeAsync(); await first.ConfigureSourceAsync("g", 1);
                await first.ConfigureAsync(false, shortcut);
            }
            await using var restarted = new WindowsWalkthroughSessions(client, new NoNetwork(), configuration, new Access()); await restarted.InitializeAsync();
            Assert.True(restarted.CanConfigure); Assert.False(restarted.Enabled); Assert.Equal(shortcut, restarted.VirtualKey); Assert.Equal(1, restarted.SourceFor("g"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class NoNetwork : IWalkthroughReader
    { public Task<WalkthroughPage> ListAsync(string id, string? cursor, bool nsfw, CancellationToken token) => Task.FromResult(new WalkthroughPage([], null)); public Task<WalkthroughDocument> ReadAsync(string id, string work, bool nsfw, CancellationToken token) => throw new InvalidOperationException(); }
    private sealed class Access : INextMoeAccess
    {
        public OnlineAuthContext AuthContext { get; } = new(); public Task<string?> ReadAsync(CancellationToken token) => Task.FromResult<string?>(null);
        public Task<NextMoeAuthStatus> StatusAsync(CancellationToken token) => Task.FromResult(new NextMoeAuthStatus(NextMoeAccessMode.Anonymous, null, false, []));
        public Task<string> UserTokenAsync(string? scope, CancellationToken token) => throw new OnlineException(OnlineFailure.UserIdentityRequired);
        public Task<bool> RefreshRejectedAsync(string rejected, CancellationToken token) => Task.FromResult(false);
    }
    [Fact] public async Task ManualNotesAndReadingPlaceSurviveRestartWithoutDiscoveringOtherFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "TheOrder-reading-memory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string a = Path.Combine(folder, "notes-a.txt"), b = Path.Combine(folder, "notes-b.txt"); await File.WriteAllTextAsync(a, "说明 A"); await File.WriteAllTextAsync(b, "说明 B");
        try
        {
            var codec = new NativeLibraryCodec(); using var profile = NativeProfileStore.Open(new(NativeProfileKind.Test, Path.Combine(folder, "profile")), codec);
            var entry = SessionTestRig.Entry() with { Installations = [], PreferredInstallationId = null, GuidePaths = [a] };
            var persistence = profile.CreateLibraryPersistence(); try { await persistence.SaveAsync(persistence.ReadMain()!, codec.Encode([entry], SessionTestRig.Date)); } finally { (persistence as IDisposable)?.Dispose(); }
            var configuration = profile.Configuration(true); await using var client = new OfflineLibraryClient(new IsolatedLibraryFactory(codec, configuration), new LegacyClientTextRules()); await client.LoadAsync(default);
            var local = new WindowsLocalReading(new WindowsFilesystemIdentity(), new LocalDocumentParser());
            await File.WriteAllTextAsync(Path.Combine(folder, "profile", "native-guide-overlay-v1.json"), System.Text.Json.JsonSerializer.Serialize(new { Version = 1, ProfileId = profile.Manifest.ProfileId, StoreId = profile.Manifest.StoreId, Enabled = true, VirtualKey = 0x77, NotesVirtualKey = 0x78 }));
            await using (var first = new WindowsWalkthroughSessions(client, new NoNetwork(), configuration, new Access(), localReading: local))
            {
                await first.InitializeAsync(); Assert.Equal(0x4a, first.VirtualKey); Assert.DoesNotContain("Ctrl", first.Message); await first.ConfigureNotesAsync("g", [a, b]);
                first.RememberReadingPosition("g", 2, b, 3, .625f, 1.2f, touch: true);
                first.RememberReadingPosition("g", 1, a, 1, .25f, 1, touch: true);
                first.RememberReadingPosition("g", 2, Path.Combine(folder, "unselected.txt"), 5, .5f, 1);
            }
            await using var restarted = new WindowsWalkthroughSessions(client, new NoNetwork(), configuration, new Access(), localReading: local); await restarted.InitializeAsync();
            var chosen = await restarted.FindLocalFilesAsync("g", default); Assert.Equal([a, b], chosen.Notes.Select(f => f.Path));
            Assert.Equal(b, restarted.LastReadingFile("g", 2, chosen.Notes)); var memory = restarted.ReadingPosition("g", 2, b);
            Assert.NotNull(memory); Assert.Equal(3, memory.Page); Assert.Equal(.625f, memory.Progress); Assert.Equal(1.2f, memory.Zoom);
            Assert.Null(restarted.ReadingPosition("g", 2, Path.Combine(folder, "unselected.txt"))); Assert.Null(restarted.ReadingPosition("g", 1, b));
        }
        finally { Directory.Delete(folder, true); }
    }
}
