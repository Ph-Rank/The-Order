using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class GuideSessionTests
{
    private sealed class Access : INextMoeAccess
    {
        public OnlineAuthContext AuthContext { get; } = new();
        public Task<string?> ReadAsync(CancellationToken token) => Task.FromResult<string?>(null);
        public Task<NextMoeAuthStatus> StatusAsync(CancellationToken token) => Task.FromResult(new NextMoeAuthStatus(NextMoeAccessMode.Anonymous, null, false, []));
        public Task<string> UserTokenAsync(string? scope, CancellationToken token) => throw new OnlineException(OnlineFailure.UserIdentityRequired);
        public Task<bool> RefreshRejectedAsync(string rejected, CancellationToken token) => Task.FromResult(false);
    }
    private sealed class Reader : IWalkthroughReader
    {
        public int Lists, Reads;
        public Func<CancellationToken, Task<WalkthroughPage>> List = _ => Task.FromResult(new WalkthroughPage([WalkthroughTests.Document().Summary], null));
        public Func<string, string, CancellationToken, Task<WalkthroughDocument>> Read = (id, work, _) => Task.FromResult(WalkthroughTests.Document(id, work));
        public Task<WalkthroughPage> ListAsync(string id, string? cursor, bool nsfw, CancellationToken token) { Assert.True(nsfw); Lists++; return List(token); }
        public Task<WalkthroughDocument> ReadAsync(string id, string work, bool nsfw, CancellationToken token) { Assert.True(nsfw); Reads++; return Read(id, work, token); }
    }
    private sealed class LocalReader(bool notes = false) : ILocalReadingLibrary
    {
        public int Opened;
        public Task<LocalReadingFiles> FindAsync(LibraryEntry entry, CancellationToken cancellation) => Task.FromResult(new LocalReadingFiles([new(@"C:\manual\攻略.txt", "攻略.txt", "guide")], notes ? [new(@"C:\manual\说明.txt", "说明.txt", "note")] : []));
        public Task<ILocalReadingDocument> OpenAsync(LocalReadingFile file, CancellationToken cancellation) { Opened++; return Task.FromResult<ILocalReadingDocument>(new LocalDoc()); }
        private sealed class LocalDoc : ILocalReadingDocument
        {
            public string Name => "攻略.txt"; public string Notice => "只读"; public int PageCount => 1;
            public Task<ReadingPage> PageAsync(int index, CancellationToken cancellation) => Task.FromResult(new ReadingPage("第 1 页", [new("paragraph", "合成的本地攻略")]));
            public void Dispose() { }
        }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LocalGuideWorksWithoutNetworkAssociationOrNetworkResults(bool linked)
    {
        var local = new LocalReader(); await using var test = new Setup(linked: linked, local: local); var snapshot = await test.Snapshot();
        test.Reader.List = _ => Task.FromResult(new WalkthroughPage([], null));
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot); Assert.NotNull(plan);
        using var map = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = map.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        Assert.Equal(1, view.ReadInt32(176)); Assert.Equal(1, view.ReadInt32(144)); Assert.Equal(1, local.Opened);
        int length = view.ReadInt32(12); var data = new byte[length]; view.ReadArray(GuideOverlayWire.HeaderBytes, data, 0, length);
        Assert.Contains("合成的本地攻略", System.Text.Encoding.UTF8.GetString(data)); Assert.Equal(linked ? 1 : 0, test.Reader.Lists);
    }
    [Fact] public async Task BothGuideTypesPreferNetworkWithoutOpeningLocalFile()
    {
        var local = new LocalReader(); await using var test = new Setup(local: local); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot)!;
        using var map = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = map.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        Assert.Equal(0, view.ReadInt32(176)); Assert.Equal(0, local.Opened); Assert.Equal(3, view.ReadInt32(148)); Assert.Equal(0, view.ReadInt32(80)); Assert.Equal(0x4a, view.ReadInt32(76));
    }
    [Fact] public async Task NativeAcknowledgedReadingPlaceIsRestoredInTheNextSession()
    {
        var local = new LocalReader(); await using var test = new Setup(linked: false, local: local); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot)!;
        using var firstMap = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"]); using var view = firstMap.CreateViewAccessor();
        view.Write(248, 4); view.Write(244, 0); view.Write(240, 1000); view.Write(200, 625); view.Write(108, 1200); view.Write(116, view.ReadInt32(8));
        test.Sessions.Tick(); await test.Sessions.PrepareForGameAsync("g", default); var next = test.Sessions.Prepare(snapshot)!;
        using var secondMap = MemoryMappedFile.OpenExisting(next.Environment["THE_ORDER_GUIDE_SESSION"]); using var restored = secondMap.CreateViewAccessor();
        int length = restored.ReadInt32(12); Assert.True(length > 8); Assert.Equal(.625f, restored.ReadSingle(GuideOverlayWire.HeaderBytes + length - 8)); Assert.Equal(1.2f, restored.ReadSingle(GuideOverlayWire.HeaderBytes + length - 4));
    }
    [Fact] public async Task FirstOpeningUsesGuideThenResumesLastOpenedNotesAndViewMode()
    {
        var local = new LocalReader(notes: true); await using var test = new Setup(local: local); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default); var first = test.Sessions.Prepare(snapshot)!;
        using var map = MemoryMappedFile.OpenExisting(first.Environment["THE_ORDER_GUIDE_SESSION"]); using var view = map.CreateViewAccessor();
        Assert.Equal(0, view.ReadInt32(176)); Assert.Null(test.Sessions.LastOverlayPage("g"));
        view.Write(180, 1); view.Write(176, 2); view.Write(72, 0); test.Sessions.Tick();
        Assert.Equal(new OverlayPagePreference(2, false), test.Sessions.LastOverlayPage("g"));
        await test.Sessions.PrepareForGameAsync("g", default); var second = test.Sessions.Prepare(snapshot)!;
        using var restoredMap = MemoryMappedFile.OpenExisting(second.Environment["THE_ORDER_GUIDE_SESSION"]); using var restored = restoredMap.CreateViewAccessor();
        Assert.Equal(2, restored.ReadInt32(176)); Assert.Equal(0, restored.ReadInt32(72)); Assert.Equal(0x4a, restored.ReadInt32(76));
    }
    private sealed class Factory(SessionTestRig rig) : ITransactionalLibraryFactory
    { public Task<TransactionalLibrary> OpenAsync(CancellationToken token) => Task.FromResult(rig.Open()); }
    private sealed class Setup : IAsyncDisposable
    {
        public TransactionProfile Profile = new();
        public SessionTestRig Rig;
        public Reader Reader = new(); public Access Access = new();
        public OfflineLibraryClient Client; public WindowsWalkthroughSessions Sessions;
        public Setup(bool linked = true, TimeSpan? timeout = null, IGuideFlowLayout? layout = null, ILocalReadingLibrary? local = null)
        {
            var work = new WorkMetadata("198", "Synthetic", "Synthetic", [], null, null, null, null, "", null, null, [], [], [], [], [], null, null, SessionTestRig.Date);
            Rig = new(SessionTestRig.Codec.Encode([SessionTestRig.Entry() with { Metadata = linked ? work : null }], SessionTestRig.Date));
            Client = new(new Factory(Rig), new LegacyClientTextRules());
            string components = Path.Combine(Profile.Root, "components"); Directory.CreateDirectory(components);
            string[] names = ["x86/TheOrder.GuideHost.exe", "x64/TheOrder.GuideHost.exe", "x86/TheOrder.GuideRuntime.dll",
                "x64/TheOrder.GuideRuntime.dll", "TheOrder.Guide.addon32", "TheOrder.Guide.addon64"];
            foreach (var name in names) { string file = Path.Combine(components, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, [1, 2, 3]); }
            File.WriteAllBytes(Path.Combine(components, "payload.json"), JsonSerializer.SerializeToUtf8Bytes(new { files = names.Select(name => new { path = name, bytes = 3,
                sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })) }) }));
            Sessions = new(Client, Reader, new(Profile.Root), Access, layout, local) { ComponentDirectoryForTest = components,
                PreparationTimeout = timeout ?? TimeSpan.FromSeconds(15) };
        }
        public async Task<LaunchSnapshot> Snapshot()
        { await Client.LoadAsync(default); await Rig.Engine.InitializeAsync(); return (await Rig.Engine.PrepareAsync("g")).Snapshot!; }
        public async ValueTask DisposeAsync() { await Sessions.DisposeAsync(); await Client.DisposeAsync(); await Rig.DisposeAsync(); Profile.Dispose(); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task NoGuideOrFailedQueryNeverCreatesCompanionSession(bool failure)
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        test.Reader.List = _ => failure ? throw new OnlineException(OnlineFailure.Offline) : Task.FromResult(new WalkthroughPage([], null));
        await test.Sessions.PrepareForGameAsync("g", default);
        Assert.Null(test.Sessions.Prepare(snapshot)); Assert.Equal(0, test.Reader.Reads);
        Assert.True(test.Sessions.Notice!.Warning); Assert.Equal("g", test.Sessions.Notice.GameId);
        Assert.Contains(failure ? "失败" : "暂无", test.Sessions.Notice.Message);
        Assert.Empty(Directory.GetDirectories(test.Profile.Root, "guide-session-*"));
    }
    private sealed class FailedOptionalLayout(bool invalid) : IGuideFlowLayout
    {
        public Task<GuideFlowLayout?> CreateAsync(WalkthroughDocument _,CancellationToken cancellation)=>invalid
            ? Task.FromResult<GuideFlowLayout?>(new(float.NaN,1,[],[],false))
            : Task.FromException<GuideFlowLayout?>(new InvalidDataException("Synthetic layout failure"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task OptionalLayoutFailureStillPreparesTheCompleteBody(bool invalid)
    {
        await using var test=new Setup(layout:new FailedOptionalLayout(invalid));var snapshot=await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g",default);var plan=test.Sessions.Prepare(snapshot);Assert.NotNull(plan);
        using var mapping=MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"],MemoryMappedFileRights.Read);
        using var view=mapping.CreateViewAccessor(0,GuideOverlayWire.MaximumBytes,MemoryMappedFileAccess.Read);
        int length=view.ReadInt32(12);Assert.True(length>0);var data=new byte[length];view.ReadArray(GuideOverlayWire.HeaderBytes,data,0,length);
        using var reader=new BinaryReader(new MemoryStream(data),System.Text.Encoding.UTF8);
        int titleBytes=reader.ReadInt32();Assert.True(titleBytes>0);Assert.Contains("等待游戏显示连接",test.Sessions.Notice!.Message);
        Assert.False(test.Sessions.Notice.Warning);
    }
    [Fact] public async Task UnlinkedGameExplainsWhyShortcutWasNotPrepared()
    {
        await using var test = new Setup(false); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default);
        Assert.Null(test.Sessions.Prepare(snapshot)); Assert.Equal(0, test.Reader.Lists);
        Assert.True(test.Sessions.Notice!.Warning); Assert.Contains("尚未关联", test.Sessions.Notice.Message);
    }
    [Fact] public async Task PositiveAvailabilityCreatesOnlyTransientDataAndDisablingClosesExistingSessions()
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot);
        Assert.NotNull(plan); Assert.Equal(1, test.Reader.Reads);
        Assert.Equal(WindowsGuideHotkeys.DefaultKey, test.Sessions.VirtualKey);
        Assert.Contains("等待游戏显示连接", test.Sessions.Notice!.Message);
        using var mapping = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = mapping.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        Assert.True(view.ReadInt32(12) > 0); Assert.True(view.ReadInt64(32) > 0);
        string configuration = Assert.Single(Directory.GetFiles(test.Profile.Root, "ReShade.ini", SearchOption.AllDirectories));
        Assert.DoesNotContain("手紙", File.ReadAllText(configuration));
        test.Sessions.Configure(false, 0x79);
        Assert.Equal(0L, view.ReadInt64(32)); Assert.Equal(0, view.ReadInt32(16));
        Assert.Null(test.Sessions.Prepare(snapshot));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LateAvailabilityFromPreviousLoginOrDisabledSettingCannotEnterGame(bool identityChange)
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        var release = new TaskCompletionSource<WalkthroughPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Reader.List = _ => release.Task; var pending = test.Sessions.PrepareForGameAsync("g", default);
        if (identityChange) { test.Access.AuthContext.BeginMutation(); test.Access.AuthContext.EndMutation(); }
        else test.Sessions.Configure(false, WindowsGuideHotkeys.DefaultKey);
        release.SetResult(new([WalkthroughTests.Document().Summary], null)); await pending;
        Assert.Null(test.Sessions.Prepare(snapshot)); Assert.Equal(0, test.Reader.Reads);
    }
    [Fact] public async Task ReadingThemeAndDisplayDensityUpdateLiveWithoutRefetchingOrChangingSteps()
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        test.Sessions.SetAppearance(true, 192); await test.Sessions.PrepareForGameAsync("g", default);
        var plan = test.Sessions.Prepare(snapshot)!;
        using var mapping = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = mapping.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        Assert.Equal(1, view.ReadInt32(212)); Assert.Equal(192, view.ReadInt32(216));
        int documentRevision = view.ReadInt32(8); test.Sessions.SetAppearance(false, 144);
        Assert.Equal(0, view.ReadInt32(212)); Assert.Equal(144, view.ReadInt32(216));
        Assert.Equal(documentRevision, view.ReadInt32(8)); Assert.Equal(1, test.Reader.Lists); Assert.Equal(1, test.Reader.Reads);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CancelledOrUnexpectedlyFailedDetailCannotLeavePreparingNotice(bool cancelled)
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        test.Reader.Read = (_, _, _) => cancelled
            ? Task.FromCanceled<WalkthroughDocument>(new CancellationToken(true))
            : Task.FromException<WalkthroughDocument>(new InvalidOperationException("synthetic private diagnostic"));
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot);
        Assert.NotNull(plan); Assert.True(test.Sessions.Notice!.Warning);
        Assert.DoesNotContain("正在", test.Sessions.Notice.Message);
        Assert.DoesNotContain("synthetic private diagnostic", test.Sessions.Notice.Message);
    }
    [Fact] public async Task AuthInvalidationBeforeProcessAttachmentEndsPreparationAndDiscardsLateDetail()
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        var delayed = new TaskCompletionSource<WalkthroughDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Reader.Read = (_, _, _) => delayed.Task;
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot);
        Assert.NotNull(plan);
        test.Access.AuthContext.BeginMutation(); test.Access.AuthContext.EndMutation();
        test.Sessions.Tick(); delayed.SetResult(WalkthroughTests.Document());
        Assert.True(test.Sessions.Notice!.Warning); Assert.Contains("登录", test.Sessions.Notice.Message);
        Assert.DoesNotContain("正在", test.Sessions.Notice.Message);
    }
    [Fact] public async Task NonCooperativeDetailHasDeadlineAndCannotOverwriteItsTimeout()
    {
        await using var test = new Setup(timeout: TimeSpan.FromMilliseconds(60)); var snapshot = await test.Snapshot();
        var delayed = new TaskCompletionSource<WalkthroughDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Reader.Read = (_, _, _) => delayed.Task;
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot)!;
        using var map = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = map.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!test.Sessions.Notice!.Warning && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(test.Sessions.Notice!.Warning); Assert.Contains("超时", test.Sessions.Notice.Message);
        var notice = test.Sessions.Notice; var sequence = view.ReadInt32(8);
        delayed.SetResult(WalkthroughTests.Document()); await Task.Delay(40);
        Assert.Equal(notice, test.Sessions.Notice); Assert.Equal(sequence, view.ReadInt32(8));
    }
    [Fact] public async Task NonCooperativePaginationAlsoHasOneOverallPreparationDeadline()
    {
        await using var test = new Setup(timeout: TimeSpan.FromMilliseconds(60)); var snapshot = await test.Snapshot();
        var delayed = new TaskCompletionSource<WalkthroughPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Reader.List = _ => test.Reader.Lists == 1
            ? Task.FromResult(new WalkthroughPage([WalkthroughTests.Document().Summary], "cur_more")) : delayed.Task;
        await test.Sessions.PrepareForGameAsync("g", default); Assert.NotNull(test.Sessions.Prepare(snapshot));
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!test.Sessions.Notice!.Warning && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Contains("超时", test.Sessions.Notice!.Message); Assert.Equal(0, test.Reader.Reads);
        delayed.SetResult(new([], null));
    }
    [Fact] public async Task ObservedGameCanAttachEvenWhenInitialLauncherHadNoUsableIdentity()
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot)!;
        using var map = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = map.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var current = new ProcessIdentity(self.Id, self.StartTime.ToFileTimeUtc(), self.MainModule!.FileName, null);
        test.Sessions.Tick(new(LaunchOutcome.Started, ProcessTrackingState.RunningConfirmed, snapshot, null, current, "synthetic observed child"));
        Assert.Equal(current.Pid, view.ReadInt32(40)); Assert.Equal(current.CreationFileTime, view.ReadInt64(48));
    }
    [Fact] public async Task LauncherExitBeforeHandoffPublicationDoesNotDestroyChildGuideSession()
    {
        await using var test = new Setup(); var snapshot = await test.Snapshot();
        await test.Sessions.PrepareForGameAsync("g", default); var plan = test.Sessions.Prepare(snapshot)!;
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        // A mismatched generation models an initial process that no longer exists.
        plan.Attach(self.Id, 1);
        using var map = MemoryMappedFile.OpenExisting(plan.Environment["THE_ORDER_GUIDE_SESSION"], MemoryMappedFileRights.Read);
        using var view = map.CreateViewAccessor(0, GuideOverlayWire.MaximumBytes, MemoryMappedFileAccess.Read);
        var initial = new ProcessIdentity(self.Id, 1, self.MainModule!.FileName, null);
        test.Sessions.Tick(new(LaunchOutcome.Started, ProcessTrackingState.RunningConfirmed, snapshot, initial, initial, "synthetic initial exit"));
        Assert.True(view.ReadInt64(32)>0);
        var child = initial with { CreationFileTime = self.StartTime.ToFileTimeUtc() };
        test.Sessions.Tick(new(LaunchOutcome.Started, ProcessTrackingState.RunningConfirmed, snapshot, initial, child, "synthetic settled child"));
        Assert.Equal(child.CreationFileTime, view.ReadInt64(48)); Assert.True(view.ReadInt64(32)>0);
    }
}
