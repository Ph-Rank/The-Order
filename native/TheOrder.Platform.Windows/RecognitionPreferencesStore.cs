using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional companion: never changes native-ui v1, required capabilities, writer
// version or bootstrap. Older native binaries ignore this file. The profile's
// existing exclusive writer lease owns it; adapters neither acquire nor release it.
public sealed class RecognitionPreferencesStore(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? probe = null) : IRecognitionPreferencesStore, IAsyncDisposable
{
    public const string FileName = "native-recognition-ui-v1.json";
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, bool UseOnline, bool IncludePe);
    private readonly ProfilePreferencesFile file = new(configuration, FileName, typeof(RecognitionPreferencesStore),
        "Native profile required for recognition preferences", probe);

    private static RecognitionPreferenceRead Decode(byte[] bytes, NativeProfileStore profile)
    {
        if (bytes.Length > ProfilePreferencesFile.MaximumBytes) return new(new(), UiText.Get("识别设置文件过大，已使用默认选项；原文件保留。"), true);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var names = ProfilePreferencesFile.ReadUniqueNames(document.RootElement);
            if (!ProfilePreferencesFile.IsVersionOne(document.RootElement))
                return new(new(), UiText.Get("识别设置版本不受支持，已使用默认选项；原文件保留。"), true);
            if (names.Any(name => name is not ("Version" or "ProfileId" or "StoreId" or "UseOnline" or "IncludePe")))
                return new(new(), UiText.Get("识别设置包含不受支持的字段，已使用默认选项；原文件保留。"), true);
            var value = JsonSerializer.Deserialize<Envelope>(bytes, ProfilePreferencesFile.JsonOptions) ?? throw new JsonException();
            if (value.ProfileId != profile.Manifest.ProfileId || value.StoreId != profile.Manifest.StoreId)
                return new(new(), UiText.Get("识别设置不属于当前资料，已使用默认选项；原文件保留。"), true);
            return new(new(value.UseOnline, value.IncludePe));
        }
        catch (JsonException) { return new(new(), UiText.Get("识别设置格式损坏，已使用默认选项；可重新选择。")); }
    }

    public Task<RecognitionPreferenceRead> LoadAsync(CancellationToken cancellation = default) =>
        file.LoadAsync(new RecognitionPreferenceRead(new()), Decode, cancellation);

    public Task SaveAsync(RecognitionPreference preference, CancellationToken cancellation = default)
    {
        return file.SaveAsync(profile => new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, preference.UseOnline, preference.IncludePe),
            Decode, read => (read.Warning, read.ReadOnly), cancellation);
    }

    public ValueTask DisposeAsync() => file.DisposeAsync();
}
