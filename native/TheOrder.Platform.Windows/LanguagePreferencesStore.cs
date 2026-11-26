using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional companion: never changes native-ui v1, required capabilities, writer
// version or bootstrap. Older native binaries ignore this file. The profile's
// existing exclusive writer lease owns it; adapters neither acquire nor release it.
public sealed class LanguagePreferencesStore(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? probe = null) : ILanguagePreferencesStore, IAsyncDisposable
{
    public const string FileName = "native-language-v1.json";
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, string Language);
    private readonly ProfilePreferencesFile file = new(configuration, FileName, typeof(LanguagePreferencesStore),
        "Native profile required for language preferences", probe);

    private static LanguagePreferenceRead Decode(byte[] bytes, NativeProfileStore profile)
    {
        if (bytes.Length > ProfilePreferencesFile.MaximumBytes) return new(new(), "语言设置文件过大，已跟随系统；原文件保留。", true);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var names = ProfilePreferencesFile.ReadUniqueNames(document.RootElement);
            if (!ProfilePreferencesFile.IsVersionOne(document.RootElement))
                return new(new(), "语言设置版本不受支持，已跟随系统；原文件保留。", true);
            if (names.Any(name => name is not ("Version" or "ProfileId" or "StoreId" or "Language")))
                return new(new(), "语言设置包含不受支持的字段，已跟随系统；原文件保留。", true);
            var value = JsonSerializer.Deserialize<Envelope>(bytes, ProfilePreferencesFile.JsonOptions) ?? throw new JsonException();
            if (value.ProfileId != profile.Manifest.ProfileId || value.StoreId != profile.Manifest.StoreId)
                return new(new(), "语言设置不属于当前资料，已跟随系统；原文件保留。", true);
            if (!UiText.ValidChoice(value.Language)) return new(new(), "语言设置包含不受支持的语言，已跟随系统；原文件保留。", true);
            return new(new(value.Language));
        }
        catch (JsonException) { return new(new(), "语言设置格式损坏，已跟随系统；可重新选择。"); }
    }

    public Task<LanguagePreferenceRead> LoadAsync(CancellationToken cancellation = default) =>
        file.LoadAsync(new LanguagePreferenceRead(new()), Decode, cancellation);

    public Task SaveAsync(LanguagePreference preference, CancellationToken cancellation = default)
    {
        if (!UiText.ValidChoice(preference.Language)) throw new ArgumentException("Unsupported UI language");
        return file.SaveAsync(profile => new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, preference.Language),
            Decode, read => (read.Warning, read.ReadOnly), cancellation);
    }

    public ValueTask DisposeAsync() => file.DisposeAsync();
}
