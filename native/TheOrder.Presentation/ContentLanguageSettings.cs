using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed class ContentLanguageSettings(IContentLanguagePreferencesStore store, Action<ContentLanguagePreference>? apply = null) : ObservableState, IAsyncDisposable
{
    private bool initialized, readOnly, saving, disposed;
    private Task write = Task.CompletedTask;
    public ContentLanguagePreference Preference { get; private set; } = new();
    public bool CanChange => initialized && !readOnly && !saving && !disposed;
    public string Message { get; private set; } = "";
    public async Task InitializeAsync()
    {
        if (initialized || disposed) return;
        try
        {
            var read = await store.LoadAsync();
            if (disposed) return;
            if (!ContentLanguage.Valid(read.Preference)) throw new InvalidDataException();
            Preference = read.Preference; readOnly = read.ReadOnly;
            Message = read.Warning ?? "";
        }
        catch (Exception) { readOnly = true; Message = "资料语言偏好暂不可读取，仍可浏览原始资料。"; }
        Apply(Preference); initialized = true; Notify();
    }
    public Task ChangeAsync(ContentLanguagePreference value)
    {
        if (!ContentLanguage.Valid(value)) throw new ArgumentException("Unsupported content language");
        if (!CanChange || value == Preference) return Task.CompletedTask;
        saving = true; Notify(); return write = SaveAsync(value);
    }
    private async Task SaveAsync(ContentLanguagePreference value)
    {
        try
        {
            await store.SaveAsync(value);
            Preference = value; Apply(value);
            Message = "重新进入详情页后生效。";
        }
        catch (Exception) { Message = "资料语言偏好未能保存，仍保留原选项；请重试。"; }
        finally { saving = false; if (!disposed) Notify(); }
    }
    private void Apply(ContentLanguagePreference value) { if (apply is null) ContentLanguage.Configure(value); else apply(value); }
    private void Notify() { Changed(nameof(Preference)); Changed(nameof(CanChange)); Changed(nameof(Message)); }
    public async ValueTask DisposeAsync() { disposed = true; await write; }
}
