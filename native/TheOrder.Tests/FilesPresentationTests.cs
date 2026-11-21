using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class FilesPresentationTests
{
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static InstallationView View(string id) => new(new(new(Guid.Empty, id, Guid.Empty, [], null), [], null), []);
    private static WorkspaceViewModel Workspace(Library client) => new(client, new Preferences(), new("synthetic"), new LegacyClientTextRules());
    private static ScanCandidate Candidate(string id, bool available = true)
    {
        var path = new PathObservation(id, id, id, new("synthetic", id), true, false, 0);
        return new(id, id, path, [], "", new(new(available ? PreflightStatus.Valid : PreflightStatus.DuplicateExistingInstall, path, available ? "可导入" : "已在书架中")));
    }
    [Fact]
    public async Task ImportSelectionCountsOnlyCurrentAttachableItemsAndClearsOnNewDiscovery()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        await using var files = new FilesViewModel(new Files(), workspace);
        var first = Candidate("first"); var duplicate = Candidate("duplicate", false); var stale = Candidate("stale");
        files.ReviewDiscovered([first, duplicate]);
        Assert.True(files.HasCandidates); Assert.True(files.CanSelectImportItems); Assert.False(files.CanImportSelection);
        var changed = new List<string?>(); files.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        files.SelectForImport([first, first, duplicate, stale]);
        Assert.Equal(1, files.SelectedImportCount); Assert.Same(first, Assert.Single(files.SelectedImportCandidates)); Assert.True(files.CanImportSelection);
        files.ReviewDiscovered([duplicate]);
        Assert.Equal(0, files.SelectedImportCount); Assert.False(files.CanImportSelection); Assert.False(files.CanSelectImportItems);
        Assert.Contains(nameof(files.SelectedImportCount), changed); Assert.Contains(nameof(files.CanSelectImportItems), changed);
    }
    [Fact]
    public async Task ImportSelectionSurvivesFailedSaveAndClearsOnlyAfterCommit()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        var client = new Files { SaveResult = new(ClientOutcome.IoFailure, "未能保存") };
        await using var files = new FilesViewModel(client, workspace); var item = Candidate("game");
        files.ReviewDiscovered([item]); files.SelectForImport([item]);
        await files.ImportAsync(files.SelectedImportCandidates);
        Assert.Equal(ImportStage.Review, files.Stage); Assert.True(files.CanImportSelection); Assert.False(files.HasImportResult); Assert.Single(files.Candidates);
        client.SaveResult = new(ClientOutcome.Committed, "已经保存"); await files.ImportAsync(files.SelectedImportCandidates);
        Assert.True(files.HasImportResult); Assert.False(files.HasCandidates); Assert.False(files.CanImportSelection); Assert.Equal(0, files.SelectedImportCount);
    }
    [Fact]
    public async Task RescanClearsSelectionAndOnlyManualFoldersOfferRetry()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var gate = Gate<ScanResult>();
        var client = new Files { Scan = (_, _) => gate.Task }; await using var files = new FilesViewModel(client, workspace);
        var item = Candidate("first"); files.ReviewDiscovered([item]); files.SelectForImport([item]); Assert.False(files.CanRetryScan);
        var scanning = files.ScanAsync("manual-folder"); Assert.True(files.HasScanRoot); Assert.False(files.CanRetryScan); Assert.False(files.CanImportSelection); Assert.Empty(files.SelectedImportCandidates);
        gate.SetResult(new(ScanStatus.Cancelled, [], new(0, 0, 0, "manual-folder"), [], "")); await scanning;
        Assert.True(files.CanRetryScan); Assert.Equal(ImportStage.Cancelled, files.Stage);
        files.ReviewDiscovered([]); Assert.False(files.CanRetryScan); Assert.False(files.HasScanRoot); Assert.False(files.HasCandidates);
    }
    [Fact] public async Task DiscoveryActionsAreLimitedToSourceReviewAndNeverRetryAnOldManualRoot()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var client = new Files();
        await using var files = new FilesViewModel(client, workspace);
        await files.ScanAsync("old-manual-root"); var scans = client.Scans;
        var candidate = new ScanCandidate("discovered", "Synthetic", null!, [], "", null!);
        var changes = new List<string?>(); files.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        files.ReviewDiscovered([candidate]); Assert.True(files.IsSourceDiscovery); Assert.Null(files.ScanRoot);
        await files.RetryScanAsync(); Assert.Equal(scans, client.Scans);
        files.ReviewDiscovered([]); Assert.False(files.IsSourceDiscovery); Assert.Equal(ImportStage.NoCandidates, files.Stage);
        await files.ScanAsync("new-manual-root"); Assert.False(files.IsSourceDiscovery);
        Assert.Contains(nameof(files.IsSourceDiscovery), changes);
    }
    [Theory][InlineData(ClientOutcome.Committed)][InlineData(ClientOutcome.CommittedWithWarning)]
    public async Task SavedDraftClosesAndRefreshFailureCannotBecomeSaveFailure(ClientOutcome outcome)
    {
        var library = new Library(); await using var workspace = Workspace(library); await workspace.InitializeAsync();
        var client = new Files { SaveResult = new(outcome, "已经保存") }; await using var files = new FilesViewModel(client, workspace);
        await files.LoadInstallationsAsync("a"); files.Edit(null); var notifications = new List<string?>(); files.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        client.Load = (_, _) => Task.FromException<InstallationView>(new IOException());
        await files.SaveAsync(); Assert.False(files.HasDraft); Assert.Contains(nameof(files.HasDraft), notifications); Assert.Contains("已保存", files.InstallationMessage); Assert.Contains("刷新失败", files.InstallationMessage); Assert.Equal(1, client.Saves);
        await files.SaveAsync(); Assert.Equal(1, client.Saves);
    }
    [Theory]
    [InlineData(ClientOutcome.Committed, 1)]
    [InlineData(ClientOutcome.CommittedWithWarning, 1)]
    [InlineData(ClientOutcome.ValidationFailure, 0)]
    public async Task OnlySavedInstallationsAwaitLaunchRefresh(ClientOutcome outcome, int expected)
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        var client = new Files { SaveResult = new(outcome, "result") };
        await using var files = new FilesViewModel(client, workspace);
        var refreshed = new List<string>(); var release = Gate<bool>(); var entered = Gate<bool>();
        files.InstallationsCommitted = async id => { refreshed.Add(id); entered.SetResult(true); await release.Task; };
        await files.LoadInstallationsAsync("a"); files.Edit(null);
        var saving = files.SaveAsync();
        if (expected != 0) { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(saving.IsCompleted); Assert.Equal("a", Assert.Single(refreshed)); }
        release.SetResult(true); await saving;
        Assert.Equal(expected, refreshed.Count);
        Assert.Equal(expected == 0, files.HasDraft);
    }
    [Fact] public async Task ValidationFailureRetainsDraftAndDraftCannotBeSilentlyReplaced()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var client = new Files { SaveResult = new(ClientOutcome.ValidationFailure, "路径无效") };
        await using var files = new FilesViewModel(client, workspace); await files.LoadInstallationsAsync("a"); files.Edit(null); var draft = files.Draft!; draft.Folder = "keep";
        files.Edit(null); Assert.Same(draft, files.Draft); await files.SaveAsync(); Assert.Same(draft, files.Draft); Assert.Equal("keep", files.Draft!.Folder);
        files.CancelDraft(); Assert.False(files.HasDraft);
    }
    [Fact] public async Task ReadOnlyNeverStartsScanOrEdit()
    {
        var library = new Library { CapabilityValue = new(true, false, "只读") }; await using var workspace = Workspace(library); await workspace.InitializeAsync();
        var client = new Files(); await using var files = new FilesViewModel(client, workspace); await files.ScanAsync("synthetic"); files.Edit(null);
        Assert.Equal(0, client.Scans); Assert.False(files.HasDraft);
    }
    [Fact] public async Task ClosingCancelsAndDrainsScan()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var entered = Gate<bool>();
        var client = new Files { Scan = async (_, token) => { entered.SetResult(true); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); } };
        var files = new FilesViewModel(client, workspace); var task = files.ScanAsync("synthetic"); await entered.Task; Assert.True(files.IsScanning); Assert.False(workspace.CanEdit);
        await files.DisposeAsync(); await task; Assert.True(workspace.CanEdit);
    }
    [Theory][InlineData(ScanStatus.Cancelled, ImportStage.Cancelled)][InlineData(ScanStatus.Failed, ImportStage.Failed)][InlineData(ScanStatus.NoCandidates, ImportStage.NoCandidates)]
    public async Task DistinctScanOutcomesStayDistinct(ScanStatus outcome, ImportStage stage)
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var client = new Files { Scan = (_, _) => Task.FromResult(new ScanResult(outcome, [], new(0, 0, 0, "synthetic"), [], "result")) };
        await using var files = new FilesViewModel(client, workspace); await files.ScanAsync("synthetic"); Assert.Equal(stage, files.Stage); Assert.False(files.IsScanning);
    }
    [Fact] public async Task ClosingTracksAcceptedMutationAndDoesNotResubmit()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var gate = Gate<ClientResult>();
        var client = new Files { Save = _ => gate.Task }; var files = new FilesViewModel(client, workspace); await files.LoadInstallationsAsync("a"); files.Edit(null);
        var saving = files.SaveAsync(); var again = files.SaveAsync(); Assert.Same(saving, again); var close = files.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
        gate.SetResult(new(ClientOutcome.Committed, "已经保存")); await close; await saving; Assert.Equal(1, client.Saves);
    }
    [Fact] public async Task StaleInstallationLoadsCannotReplaceNewGameAndAllLoadsDrain()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var a = Gate<InstallationView>(); var b = Gate<InstallationView>();
        var client = new Files { Load = (id, _) => id == "a" ? a.Task : b.Task }; var files = new FilesViewModel(client, workspace);
        var first = files.LoadInstallationsAsync("a"); var second = files.LoadInstallationsAsync("b"); b.SetResult(View("b")); await second; Assert.Equal("b", files.Installations!.Details.Token.GameId);
        var close = files.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); a.SetResult(View("a")); await first; await close; Assert.Equal("b", files.Installations.Details.Token.GameId);
    }
    [Theory][InlineData(WorkspacePage.Library)][InlineData(WorkspacePage.Settings)]
    public async Task LeavingDuringInstallationLoadInvalidatesNavigation(WorkspacePage destination)
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        var held = Gate<InstallationView>(); var client = new Files { Load = (_, _) => held.Task };
        await using var files = new FilesViewModel(client, workspace); var navigation = new InstallationNavigation(files);
        var opening = navigation.OpenAsync("a", () => Task.FromResult(true), async () => { await workspace.NavigateAsync(WorkspacePage.Installations, null, () => Task.FromResult(UnsavedChoice.Stay)); });
        navigation.Invalidate(); await workspace.NavigateAsync(destination, null, () => Task.FromResult(UnsavedChoice.Stay));
        held.SetResult(View("a")); await opening; Assert.Equal(destination, workspace.Page);
    }
    [Fact]
    public async Task CurrentInstallationIntentNavigatesAfterLoad()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        var held = Gate<InstallationView>(); await using var files = new FilesViewModel(new Files { Load = (_, _) => held.Task }, workspace);
        var navigation = new InstallationNavigation(files);
        var opening = navigation.OpenAsync("a", () => Task.FromResult(true), async () => { await workspace.NavigateAsync(WorkspacePage.Installations, null, () => Task.FromResult(UnsavedChoice.Stay)); });
        held.SetResult(View("a")); await opening; Assert.Equal(WorkspacePage.Installations, workspace.Page); Assert.Equal("a", files.Installations!.Details.Token.GameId);
    }
    [Fact]
    public async Task BackFromInstallationPageUsesLoadedGameAfterDetailsSelectionWasCleared()
    {
        var library = new Library { Detail = id => new(id, new(id, "planned", null, false, "", [], []), "", "", "", "", "", "", new(Guid.Empty, id, Guid.Empty, new(id, "planned", null, false, "", [], []))) };
        await using var workspace = Workspace(library); await workspace.InitializeAsync();
        Assert.True(await workspace.NavigateAsync(WorkspacePage.Details, "a", () => Task.FromResult(UnsavedChoice.Stay)));
        await using var files = new FilesViewModel(new Files(), workspace);
        await new InstallationNavigation(files).OpenAsync("a", () => Task.FromResult(true), async () =>
            await workspace.NavigateAsync(WorkspacePage.Installations, null, () => Task.FromResult(UnsavedChoice.Stay)));
        Assert.Null(workspace.SelectedId); Assert.Equal("a", files.GameId);
        var returnId = ShellNavigationPolicy.ParentGameId(workspace.Page, workspace.SelectedId, files.GameId);
        Assert.True(await workspace.NavigateAsync(ShellNavigationPolicy.Parent(workspace.Page)!.Value, returnId, () => Task.FromResult(UnsavedChoice.Stay)));
        Assert.Equal(WorkspacePage.Details, workspace.Page); Assert.Equal("a", workspace.SelectedId);
    }
    [Fact]
    public async Task NewestInstallationNavigationWinsEvenWhenOlderLoadFinishesLast()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); var a = Gate<InstallationView>(); var b = Gate<InstallationView>();
        await using var files = new FilesViewModel(new Files { Load = (id, _) => id == "a" ? a.Task : b.Task }, workspace);
        var navigation = new InstallationNavigation(files); var destinations = new List<string>();
        Task Open(string id) => navigation.OpenAsync(id, () => Task.FromResult(true), async () => { destinations.Add(id); await workspace.NavigateAsync(WorkspacePage.Installations, null, () => Task.FromResult(UnsavedChoice.Stay)); });
        var first = Open("a"); var second = Open("b"); b.SetResult(View("b")); await second; a.SetResult(View("a")); await first;
        Assert.Equal(new[] { "b" }, destinations); Assert.Equal("b", files.Installations!.Details.Token.GameId);
    }
    [Fact]
    public async Task RetryReusesRootAndShowsDepthPartialEvenWithNoCandidates()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync(); int attempts = 0;
        var client = new Files { Scan = (root, _) => { Assert.Equal("synthetic", root); attempts++; return Task.FromResult(new ScanResult(ScanStatus.NoCandidates, [], new(2, 3, 0, root), [], "扫描已完成，但因深度限制可能未检查全部目录。") { Truncation = ScanTruncation.DepthLimit }); } };
        await using var files = new FilesViewModel(client, workspace); await files.ScanAsync("synthetic"); await files.RetryScanAsync();
        Assert.Equal(2, attempts); Assert.Equal(ImportStage.NoCandidates, files.Stage); Assert.Contains("深度限制", files.ScanMessage);
    }
    [Theory][InlineData(ClientOutcome.Committed, true)][InlineData(ClientOutcome.CommittedWithWarning, true)]
    [InlineData(ClientOutcome.IoFailure, false)][InlineData(ClientOutcome.CandidateStale, false)]
    public async Task RecognitionReceivesOnlySuccessfulImportReceiptAfterCommit(ClientOutcome outcome, bool expected)
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        var candidate = new ScanCandidate("scan-a", "合成游戏", null!, [], "合成扫描", null!);
        var client = new Files { SaveResult = new(outcome, "导入结果") { ImportedGameIds = ["committed-a"] },
            Scan = (_, _) => Task.FromResult(new ScanResult(ScanStatus.Completed, [candidate], new(1, 1, 1, "synthetic"), [], "")) };
        await using var files = new FilesViewModel(client, workspace);
        var callbacks = 0;
        files.RecognizeImported = ids => { callbacks++; Assert.Equal(ImportStage.Completed, files.Stage); Assert.Equal("committed-a", Assert.Single(ids)); Assert.False(workspace.IsSaving); return Task.CompletedTask; };
        await files.ScanAsync("synthetic"); await files.ImportAsync([candidate]);
        Assert.Equal(expected ? 1 : 0, callbacks);
    }
    [Fact]
    public async Task DuplicateReceiptDoesNotRecognizeAndRecognitionFailureCannotUndoImport()
    {
        await using var workspace = Workspace(new()); await workspace.InitializeAsync();
        var candidate = new ScanCandidate("scan-a", "合成游戏", null!, [], "合成扫描", null!);
        var client = new Files { Scan = (_, _) => Task.FromResult(new ScanResult(ScanStatus.Completed, [candidate], new(1, 1, 1, "synthetic"), [], "")) };
        await using var files = new FilesViewModel(client, workspace); var callbacks = 0;
        files.RecognizeImported = _ => { callbacks++; throw new IOException("synthetic callback failure"); };
        await files.ScanAsync("synthetic"); await files.ImportAsync([candidate]); Assert.Equal(0, callbacks);
        client.SaveResult = new(ClientOutcome.Committed, "导入已保存") { ImportedGameIds = ["committed-a"] };
        await files.ScanAsync("synthetic"); await files.ImportAsync([candidate]);
        Assert.Equal(ImportStage.Completed, files.Stage); Assert.Equal(1, callbacks); Assert.Contains("导入已保存", files.ScanMessage); Assert.Contains("自动识别未能启动", files.ScanMessage);
    }
    private sealed class Files : IOfflineFilesClient
    {
        public int Scans, Saves;
        public ClientResult SaveResult = new(ClientOutcome.Committed, "已经保存");
        public Func<CancellationToken, Task<ClientResult>>? Save;
        public Func<string, CancellationToken, Task<InstallationView>> Load = (id, _) => Task.FromResult(View(id));
        public Func<string, CancellationToken, Task<ScanResult>> Scan = (_, _) => Task.FromResult(new ScanResult(ScanStatus.NoCandidates, [], new(0, 0, 0, "synthetic"), [], ""));
        public Task<ScanResult> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken cancellation) { Scans++; return Scan(root, cancellation); }
        public Task<InstallationView> InstallationsAsync(string id, CancellationToken cancellation) => Load(id, cancellation);
        public Task<ClientResult> SaveAsync(InstallationDetails details, InstallationInput input, CancellationToken cancellation) { Saves++; return Save?.Invoke(cancellation) ?? Task.FromResult(SaveResult); }
        public Task<ClientResult> ImportAsync(ImmutableArray<ScanCandidate> candidates, CancellationToken cancellation) => Task.FromResult(SaveResult);
        public Task<ClientResult> RemoveAsync(InstallationDetails details, string id, CancellationToken cancellation) => Task.FromResult(SaveResult);
        public Task<ClientResult> PreferAsync(InstallationDetails details, string id, CancellationToken cancellation) => Task.FromResult(SaveResult);
        public Task<ClientResult> CoverAsync(GameDetails details, string? path, CancellationToken cancellation) => Task.FromResult(SaveResult);
    }
    private sealed class Preferences : IUiPreferencesStore
    {
        public Task<ClientPreferences> LoadAsync(CancellationToken cancellation) => Task.FromResult(new ClientPreferences());
        public Task SaveAsync(ClientPreferences preferences, CancellationToken cancellation) => Task.CompletedTask;
    }
    private sealed class Library : IOfflineLibraryClient
    {
        public Func<string, GameDetails> Detail = _ => throw new NotSupportedException();
        public LibraryCapability CapabilityValue = new(true, true, ""); public LibraryCapability Capability => CapabilityValue;
        public Task<ClientResult> LoadAsync(CancellationToken cancellation) => Task.FromResult(new ClientResult(ClientOutcome.Success, ""));
        public Task<LibraryQueryResult> QueryAsync(LibraryQuery query, CancellationToken cancellation) => Task.FromResult(new LibraryQueryResult([], 0, [], []));
        public Task<GameDetails> DetailsAsync(string id, CancellationToken cancellation) => Task.FromResult(Detail(id));
        public Task<ClientResult> SaveAsync(GameDetails details, PersonalInput input, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<ClientResult> BatchAsync(ImmutableArray<string> ids, BulkPersonalPatch patch, CancellationToken cancellation) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
