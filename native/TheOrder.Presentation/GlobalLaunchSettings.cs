using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed class GlobalLaunchSettings(IGlobalLaunchDefaultsStore store, LaunchPolicy policy, IFilesystemIdentity filesystem) : ObservableState, IAsyncDisposable
{
    private bool initialized, readOnly, saving, disposed;
    private Task write = Task.CompletedTask;
    private GlobalLaunchDefaults draft = new();
    public GlobalLaunchDefaults Saved { get; private set; } = new();
    public string Message { get; private set; } = "";
    public bool CanChange => initialized && !readOnly && !saving && !disposed;
    public bool HasChanges => draft != Saved;
    public bool CanSave => CanChange && HasChanges;
    public bool LocaleEmulatorEnabled { get => draft.LocaleEmulatorEnabled; set { draft = draft with { LocaleEmulatorEnabled = value }; Notify(); } }
    public string LocaleEmulatorPath { get => draft.LocaleEmulatorPath ?? ""; set { draft = draft with { LocaleEmulatorPath = Empty(value) }; Notify(); } }
    public bool MagpieEnabled { get => draft.MagpieEnabled; set { draft = draft with { MagpieEnabled = value }; Notify(); } }
    public string MagpiePath { get => draft.MagpiePath ?? ""; set { draft = draft with { MagpiePath = Empty(value) }; Notify(); } }
    public bool BackgroundMuteEnabled { get => draft.BackgroundMuteEnabled; set { draft = draft with { BackgroundMuteEnabled = value }; Notify(); } }
    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public async Task InitializeAsync()
    {
        if (initialized || disposed) return;
        try {
            var loaded = await store.LoadAsync(); if (disposed) return;
            LaunchPolicy.Validate(loaded.Value); Saved = draft = loaded.Value; readOnly = loaded.ReadOnly;
            policy.Apply(Saved, !readOnly);
            Message = loaded.Warning ?? "";
        } catch { readOnly = true; policy.Apply(new(), false); Message = "全局启动设置暂不可读取，原资料保留。继承设置的游戏暂停启动，请重试打开程序。"; }
        initialized = true; Notify();
    }
    public Task SaveAsync()
    {
        if (!CanSave) return Task.CompletedTask;
        var value = draft; saving = true; Notify(); return write = SaveCore(value);
    }
    private async Task SaveCore(GlobalLaunchDefaults value)
    {
        try {
            LaunchPolicy.Validate(value);
            await Task.Run(() => {
                foreach (var (enabled, path, name) in new[] { (value.LocaleEmulatorEnabled, value.LocaleEmulatorPath, "LEProc.exe"), (value.MagpieEnabled, value.MagpiePath, "Magpie.exe") })
                    if (enabled && (path is null || filesystem.Inspect(path, false) is not { IsValid: true, Observation: { IsDirectory: false, ViaReparsePoint: false } }))
                        throw new InvalidDataException($"请先选择可读取的 {name}，或关闭此工具。未保存本次修改。");
            });
            await store.SaveAsync(value);
            // Applying may wait for a synchronous OS creation already in progress;
            // leave the UI thread available throughout that short admission.
            await Task.Run(() => policy.Apply(value)); Saved = value;
            Message = "启动设置已保存。";
        } catch (InvalidDataException e) { Message = e.Message; }
        catch { Message = "启动设置未能保存，仍使用原设置。请重试。"; }
        finally { saving = false; if (!disposed) Notify(); }
    }
    public void Cancel() { if (!CanChange) return; draft = Saved; Message = "已放弃未保存的修改。"; Notify(); }
    private void Notify()
    {
        foreach (var name in new[] { nameof(LocaleEmulatorEnabled), nameof(LocaleEmulatorPath), nameof(MagpieEnabled), nameof(MagpiePath), nameof(BackgroundMuteEnabled), nameof(CanChange), nameof(CanSave), nameof(HasChanges), nameof(Message) }) Changed(name);
    }
    public async ValueTask DisposeAsync() { disposed = true; await write; }
}
