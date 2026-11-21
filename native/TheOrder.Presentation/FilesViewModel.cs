using System.Collections.Immutable;
using System.Collections.ObjectModel;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Presentation;

public enum ImportStage { Idle, Scanning, Review, NoCandidates, Cancelled, Failed, Completed }
public sealed class ArgumentDraft(string value) : ObservableState
{
    private string text = value;
    public string Text { get => text; set { text = value; Changed(); } }
}
public sealed class InstallationDraft : ObservableState
{
    private string folder = "", executable = "", working = "";
    private bool preferred;
    public string SaveFolder { get; set; } = "";
    public string? SteamAppId { get; }
    public string LocaleEmulator { get; set; } = "";
    public string ScalingTool { get; set; } = "";
    public bool Administrator { get; set; }
    public bool HighDpi { get; set; }
    public bool UseSteam { get; set; }
    private int localeMode, magpieMode, muteMode;
    public int LocaleMode { get => localeMode; set { localeMode = value; NotifyAuxiliaries(); } }
    public int MagpieMode { get => magpieMode; set { magpieMode = value; NotifyAuxiliaries(); } }
    public int MuteMode { get => muteMode; set { muteMode = value; NotifyAuxiliaries(); } }
    public bool MuteInBackground { get => muteMode == (int)LaunchOverride.Enabled; set => MuteMode = (int)(value ? LaunchOverride.Enabled : LaunchOverride.Disabled); }
    public bool UsesGlobal => localeMode == 0 && magpieMode == 0 && muteMode == 0;
    public string HelpersSummary => UsesGlobal ? "使用全局启动设置" : "已为此游戏文件夹自定义辅助工具";
    public string LegacyToolsNotice => string.Join("\n", new[] {
        LocaleEmulator.Length == 0 ? null : "保留旧版 Locale Emulator 路径：" + LocaleEmulator,
        ScalingTool.Length == 0 ? null : "保留旧版 Magpie 路径：" + ScalingTool
    }.Where(x => x is not null));
    public void ResetAuxiliaries()
    {
        LocaleEmulator = ScalingTool = ""; localeMode = magpieMode = muteMode = 0;
        NotifyAuxiliaries(); Changed(nameof(LegacyToolsNotice));
    }
    private void NotifyAuxiliaries()
    { foreach (var name in new[] { nameof(LocaleMode), nameof(MagpieMode), nameof(MuteMode), nameof(UsesGlobal), nameof(HelpersSummary) }) Changed(name); }
    public string Id { get; }
    public string Folder { get => folder; set { folder = value; Changed(); } }
    public string Executable { get => executable; set { executable = value; Changed(); } }
    public string WorkingDirectory { get => working; set { working = value; Changed(); } }
    public bool Preferred { get => preferred; set { preferred = value; Changed(); } }
    public ObservableCollection<ArgumentDraft> Arguments { get; } = [];
    public InstallationDraft(InstallationRow? row = null)
    {
        Id = row?.Installation.Id ?? Guid.NewGuid().ToString();
        Folder = row?.Installation.Folder ?? ""; Executable = row?.Installation.Executable ?? "";
        WorkingDirectory = row?.Installation.WorkingDirectory ?? ""; Preferred = row?.Preferred ?? false;
        SaveFolder = row?.Installation.SaveFolder ?? ""; SteamAppId = row?.Installation.SteamAppId;
        var compatibility = row?.Installation.Compatibility;
        LocaleEmulator = compatibility?.LocaleEmulator ?? ""; ScalingTool = compatibility?.ScalingTool ?? "";
        Administrator = compatibility?.Administrator ?? false; HighDpi = compatibility?.HighDpi ?? false; UseSteam = compatibility?.UseSteam ?? false;
        var auxiliaries = row is null ? new LaunchAuxiliaries() : compatibility?.Auxiliaries ?? LaunchPolicy.LegacyOverrides(compatibility);
        localeMode = (int)auxiliaries.LocaleEmulator; magpieMode = (int)auxiliaries.Magpie; muteMode = (int)auxiliaries.BackgroundMute;
        foreach (var arg in row?.Installation.Arguments ?? []) Arguments.Add(new(arg));
    }
    public InstallationInput Input => new(Id, Folder, Executable.Length == 0 ? null : Executable, WorkingDirectory.Length == 0 ? null : WorkingDirectory, [.. Arguments.Select(a => a.Text)], Preferred)
    { SaveFolder = Empty(SaveFolder), SteamAppId = SteamAppId, Compatibility = new(Administrator, HighDpi,
        localeMode == (int)LaunchOverride.Enabled ? Empty(LocaleEmulator) : null,
        magpieMode == (int)LaunchOverride.Enabled ? Empty(ScalingTool) : null, UseSteam, MuteInBackground)
        { Auxiliaries = new((LaunchOverride)localeMode, (LaunchOverride)magpieMode, (LaunchOverride)muteMode) } };
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
public sealed class FilesViewModel(IOfflineFilesClient client, WorkspaceViewModel workspace) : ObservableState, IAsyncDisposable
{
    public Func<string, Task>? InstallationsCommitted { get; set; }
    private CancellationTokenSource? scanCancellation;
    private Task? scanTask;
    private readonly List<Task> detailTasks = [];
    private readonly CancellationTokenSource detailsLifetime = new();
    private Task? mutation;
    private long detailGeneration;
    private long scanGeneration;
    private bool disposed;
    public ImportStage Stage { get; private set; }
    public string ScanMessage { get; private set; } = "";
    public ScanProgress? Progress { get; private set; }
    public string? ScanRoot { get; private set; }
    public IReadOnlyList<ScanCandidate> Candidates { get; private set; } = Array.Empty<ScanCandidate>();
    public bool IsScanning => Stage == ImportStage.Scanning;
    private ImmutableArray<ScanCandidate> importSelection = [];
    public ImmutableArray<ScanCandidate> SelectedImportCandidates => importSelection;
    public int SelectedImportCount => importSelection.Length;
    public bool CanImportSelection => Stage == ImportStage.Review && !importSelection.IsEmpty;
    public bool CanSelectImportItems => Stage == ImportStage.Review && Candidates.Any(c => c.Preflight?.CanAttach == true);
    public bool HasScanRoot => !string.IsNullOrEmpty(ScanRoot);
    public bool CanRetryScan => HasScanRoot && !IsScanning;
    public bool HasImportResult => Stage == ImportStage.Completed;
    public bool HasCandidates => Candidates.Count > 0;
    private bool sourceDiscovery;
    public bool IsSourceDiscovery => sourceDiscovery && Stage == ImportStage.Review;
    public InstallationView? Installations { get; private set; }
    public string? GameId { get; private set; }
    public InstallationDraft? Draft { get; private set; }
    public bool HasDraft => Draft is not null;
    public string InstallationMessage { get; private set; } = "";
    public string LastPreflight { get; private set; } = "";
    public Func<ImmutableArray<string>, Task>? RecognizeImported { get; set; }
    public Task ScanAsync(string root)
    {
        if (disposed || IsScanning || !workspace.CanEdit) return Task.CompletedTask;
        scanCancellation?.Dispose(); scanCancellation = new();
        return scanTask = ScanCore(root, scanCancellation.Token);
    }
    private async Task ScanCore(string root, CancellationToken cancellation)
    {
        var generation = ++scanGeneration;
        ScanRoot = root; Progress = null; sourceDiscovery = false;
        Stage = ImportStage.Scanning; Candidates = Array.Empty<ScanCandidate>(); importSelection = []; workspace.SetExternalBusy(true);
        ScanMessage = UiText.Get("正在扫描…可以取消。"); NotifyScan();
        try
        {
            var result = await client.ScanAsync(root, new Progress<ScanProgress>(p => { if (!disposed && IsScanning && generation == scanGeneration) { Progress = p; ScanMessage = UiText.Format($"已检查 {p.Directories} 个文件夹，找到 {p.Candidates} 个含启动文件的文件夹。"); NotifyScan(); } }), cancellation);
            if (disposed) return;
            Stage = result.Status switch { ScanStatus.Completed => ImportStage.Review, ScanStatus.NoCandidates => ImportStage.NoCandidates, ScanStatus.Cancelled => ImportStage.Cancelled, _ => ImportStage.Failed };
            Candidates = Stage == ImportStage.Review ? result.Candidates : Array.Empty<ScanCandidate>();
            Progress = result.Progress;
            ScanMessage = result.Status switch
            {
                ScanStatus.Completed when !result.IsPartial => UiText.Format($"找到 {Candidates.Count} 个含启动文件的文件夹，请勾选要加入书架的游戏。"),
                ScanStatus.NoCandidates when !result.IsPartial => UiText.Get("没有找到可导入的游戏。请选择包含游戏 exe 或 cmd 的文件夹；压缩包需先解压。"),
                ScanStatus.Cancelled => UiText.Get("扫描已取消，尚未加入任何游戏。可以重新扫描或选择其他文件夹。"),
                _ => result.Message
            };
            if (!result.Warnings.IsEmpty) ScanMessage += UiText.Get(" 部分文件夹无法读取，可检查权限后重新扫描。");
        }
        catch (OperationCanceledException) { Stage = ImportStage.Cancelled; ScanMessage = UiText.Get("扫描已取消，尚未加入任何游戏。可以重新扫描或选择其他文件夹。"); }
        catch (Exception) { Stage = ImportStage.Failed; ScanMessage = UiText.Get("扫描未完成，请检查目录权限后重试。"); }
        finally { workspace.SetExternalBusy(false); NotifyScan(); }
    }
    private void NotifyScan()
    {
        Changed(nameof(Stage)); Changed(nameof(ScanMessage)); Changed(nameof(Progress)); Changed(nameof(Candidates)); Changed(nameof(IsScanning)); Changed(nameof(IsSourceDiscovery));
        Changed(nameof(ScanRoot)); Changed(nameof(HasScanRoot)); Changed(nameof(CanRetryScan)); Changed(nameof(HasImportResult)); Changed(nameof(HasCandidates)); Changed(nameof(CanSelectImportItems)); NotifyImportSelection();
    }
    public void SelectForImport(IEnumerable<ScanCandidate> selected)
    {
        var requested = selected.ToHashSet();
        importSelection = Stage == ImportStage.Review ? [.. Candidates.Where(c => c.Preflight?.CanAttach == true && requested.Contains(c))] : [];
        NotifyImportSelection();
    }
    private void NotifyImportSelection() { Changed(nameof(SelectedImportCandidates)); Changed(nameof(SelectedImportCount)); Changed(nameof(CanImportSelection)); }
    public void CancelScan() => scanCancellation?.Cancel();
    public void ReviewDiscovered(ImmutableArray<ScanCandidate> candidates)
    {
        if (disposed || IsScanning || !workspace.CanEdit) return;
        sourceDiscovery = true; ScanRoot = null;
        Stage = candidates.IsEmpty ? ImportStage.NoCandidates : ImportStage.Review;
        Candidates = candidates; importSelection = []; Progress = null;
        ScanMessage = candidates.IsEmpty ? "当前没有待导入的游戏。已忽略的游戏可在“自动发现游戏”中恢复提醒。" : $"发现 {candidates.Length} 个尚未加入书架的游戏，请勾选要导入的项目。"; NotifyScan();
    }
    public Task RetryScanAsync() => ScanRoot is { } root ? ScanAsync(root) : Task.CompletedTask;
    private Task Track(Func<Task> action) => disposed ? Task.CompletedTask : mutation is { IsCompleted: false } ? mutation : mutation = action();
    public Task WaitForMutationAsync() => mutation ?? Task.CompletedTask;
    public Task ImportAsync(ImmutableArray<ScanCandidate> selected) => Track(() => ImportCore(selected));
    private async Task ImportCore(ImmutableArray<ScanCandidate> selected)
    {
        if (Stage != ImportStage.Review || selected.IsEmpty) return;
        var result = await workspace.ExecuteFileMutationAsync(token => client.ImportAsync(selected, token));
        ScanMessage = result.Message;
        if (result.IsCommitted) { Stage = ImportStage.Completed; Candidates = Array.Empty<ScanCandidate>(); importSelection = []; }
        NotifyScan();
        if (result.IsCommitted && !result.ImportedGameIds.IsDefaultOrEmpty && !disposed && RecognizeImported is { } recognize)
        {
            try { await recognize(result.ImportedGameIds); }
            catch (Exception) { ScanMessage += UiText.Get(" 导入已保存，但自动识别未能启动，请在作品详情中重新识别，或在设置中补全缺失资料。"); NotifyScan(); }
        }
    }
    public Task LoadInstallationsAsync(string id)
    {
        if (disposed) return Task.CompletedTask;
        var generation = ++detailGeneration; GameId = id; Draft = null; Installations = null; NotifyInstallations();
        detailTasks.RemoveAll(t => t.IsCompleted);
        var task = LoadCore(id, generation); detailTasks.Add(task); return task;
    }
    private async Task LoadCore(string id, long generation)
    {
        try
        {
            var result = await client.InstallationsAsync(id, detailsLifetime.Token);
            if (disposed || generation != detailGeneration) return;
            Installations = result; InstallationMessage = "";
        }
        catch (Exception) { if (generation == detailGeneration) InstallationMessage = UiText.Get("安装信息未能读取，请重新打开。"); }
        NotifyInstallations();
    }
    public void Edit(InstallationRow? row) { if (!workspace.CanEdit || Draft is not null) return; Draft = new(row); NotifyInstallations(); }
    public void CancelDraft() { Draft = null; NotifyInstallations(); }
    public Task SaveAsync() => Track(SaveCore);
    private async Task SaveCore()
    {
        if (Draft is null || Installations is null) return;
        var input = Draft.Input; var details = Installations.Details;
        var result = await workspace.ExecuteFileMutationAsync(token => client.SaveAsync(details, input, token));
        LastPreflight = result.Outcome.ToString(); InstallationMessage = result.Message;
        if (result.IsCommitted) { Draft = null; await RefreshAfterCommit(); }
        NotifyInstallations();
    }
    public Task RemoveAsync(InstallationRow row) => Track(() => RemoveCore(row));
    private async Task RemoveCore(InstallationRow row)
    {
        if (Installations is null || Draft is not null) return;
        var result = await workspace.ExecuteFileMutationAsync(token => client.RemoveAsync(Installations.Details, row.Installation.Id, token));
        InstallationMessage = result.Message; if (result.IsCommitted) await RefreshAfterCommit(); NotifyInstallations();
    }
    public Task PreferAsync(InstallationRow row) => Track(() => PreferCore(row));
    private async Task PreferCore(InstallationRow row)
    {
        if (Installations is null || Draft is not null) return;
        var result = await workspace.ExecuteFileMutationAsync(token => client.PreferAsync(Installations.Details, row.Installation.Id, token));
        InstallationMessage = result.Message; if (result.IsCommitted) await RefreshAfterCommit(); NotifyInstallations();
    }
    private async Task RefreshAfterCommit()
    {
        Installations = null; Draft = null;
        if (disposed) return;
        var id = GameId!; var generation = detailGeneration;
        try
        {
            var refreshed = await client.InstallationsAsync(id, CancellationToken.None);
            if (disposed || generation != detailGeneration) return;
            Installations = refreshed;
            if (InstallationsCommitted is { } refreshLaunch) await refreshLaunch(id);
        }
        catch (Exception) { InstallationMessage += UiText.Get(" 已保存，但安装界面刷新失败，请重新打开；不要重复提交。"); }
    }
    private void NotifyInstallations() { Changed(nameof(Installations)); Changed(nameof(Draft)); Changed(nameof(HasDraft)); Changed(nameof(InstallationMessage)); Changed(nameof(LastPreflight)); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; scanCancellation?.Cancel(); detailsLifetime.Cancel();
        if (scanTask is not null) await scanTask; await Task.WhenAll(detailTasks); if (mutation is not null) await mutation;
        scanCancellation?.Dispose(); detailsLifetime.Dispose();
    }
}
