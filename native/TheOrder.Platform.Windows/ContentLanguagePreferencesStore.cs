using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional native companion using the same profile lease, path pins and atomic
// writer as other settings. Does not alter native-language-v1, library or decisions.
public sealed class ContentLanguagePreferencesStore(NativeConfiguration configuration, Func<PersistenceStage, Task>? probe = null)
    : IContentLanguagePreferencesStore, IAsyncDisposable
{
    public const string FileName = "native-content-language-v1.json";
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, string Preferred, string Fallback);
    private readonly ProfilePreferencesFile file = new(configuration, FileName, typeof(ContentLanguagePreferencesStore),
        "Native profile required", probe);

    private static ContentLanguagePreferenceRead Decode(byte[] bytes, NativeProfileStore profile)
    {
        if (bytes.Length > ProfilePreferencesFile.MaximumBytes) return new(new(), "资料语言偏好文件过大，原文件保留。", true);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var names = ProfilePreferencesFile.ReadUniqueNames(document.RootElement);
            if (!ProfilePreferencesFile.IsVersionOne(document.RootElement)
                || names.Any(x => x is not ("Version" or "ProfileId" or "StoreId" or "Preferred" or "Fallback")))
                return new(new(), "资料语言偏好版本不受支持，原文件保留。", true);
            var value = JsonSerializer.Deserialize<Envelope>(bytes, ProfilePreferencesFile.JsonOptions) ?? throw new JsonException();
            if (value.ProfileId != profile.Manifest.ProfileId || value.StoreId != profile.Manifest.StoreId)
                return new(new(), "资料语言偏好不属于当前资料，原文件保留。", true);
            var preference = new ContentLanguagePreference(value.Preferred, value.Fallback);
            if (!ContentLanguage.Valid(preference)) return new(new(), "资料语言选项不受支持，原文件保留。", true);
            return new(preference);
        }
        catch (JsonException) { return new(new(), "资料语言偏好格式损坏，仍使用默认选项；可重新选择。"); }
    }

    public Task<ContentLanguagePreferenceRead> LoadAsync(CancellationToken cancellation = default) =>
        file.LoadAsync(new ContentLanguagePreferenceRead(new()), Decode, cancellation);

    public Task SaveAsync(ContentLanguagePreference value, CancellationToken cancellation = default)
    {
        if (!ContentLanguage.Valid(value)) throw new ArgumentException("Unsupported content language");
        return file.SaveAsync(profile => new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, value.Preferred, value.Fallback),
            Decode, read => (read.Warning, read.ReadOnly), cancellation);
    }

    public ValueTask DisposeAsync() => file.DisposeAsync();
}
