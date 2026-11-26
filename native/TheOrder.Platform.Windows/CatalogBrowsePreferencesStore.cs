using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional companion: never changes native-ui v1, required capabilities, writer
// version or bootstrap. Older native binaries ignore this file. The profile's
// existing exclusive writer lease owns it; adapters neither acquire nor release it.
public sealed class CatalogBrowsePreferencesStore(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? probe = null) : ICatalogBrowsePreferencesStore, IAsyncDisposable
{
    public const string FileName = "native-catalog-browse-v1.json";
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, bool ShowNsfw);
    private readonly ProfilePreferencesFile file = new(configuration, FileName, typeof(CatalogBrowsePreferencesStore),
        "Native profile required for catalog browse preferences", probe);

    private static CatalogBrowsePreferenceRead Decode(byte[] bytes, NativeProfileStore profile)
    {
        if (bytes.Length > ProfilePreferencesFile.MaximumBytes) return new(new(), UiText.Get("内容展示设置文件过大，已隐藏 NSFW；原文件保留。"), true);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var names = ProfilePreferencesFile.ReadUniqueNames(document.RootElement);
            if (!ProfilePreferencesFile.IsVersionOne(document.RootElement))
                return new(new(), UiText.Get("内容展示设置版本不受支持，已隐藏 NSFW；原文件保留。"), true);
            if (names.Any(name => name is not ("Version" or "ProfileId" or "StoreId" or "ShowNsfw")))
                return new(new(), UiText.Get("内容展示设置包含不受支持的字段，已隐藏 NSFW；原文件保留。"), true);
            var value = JsonSerializer.Deserialize<Envelope>(bytes, ProfilePreferencesFile.JsonOptions) ?? throw new JsonException();
            if (value.ProfileId != profile.Manifest.ProfileId || value.StoreId != profile.Manifest.StoreId)
                return new(new(), UiText.Get("内容展示设置不属于当前资料，已隐藏 NSFW；原文件保留。"), true);
            return new(new(value.ShowNsfw));
        }
        catch (JsonException) { return new(new(), UiText.Get("内容展示设置格式损坏，已隐藏 NSFW；可重新选择。")); }
    }

    public Task<CatalogBrowsePreferenceRead> LoadAsync(CancellationToken cancellation = default) =>
        file.LoadAsync(new CatalogBrowsePreferenceRead(new()), Decode, cancellation);

    public Task SaveAsync(CatalogBrowsePreference preference, CancellationToken cancellation = default)
    {
        return file.SaveAsync(profile => new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, preference.ShowNsfw),
            Decode, read => (read.Warning, read.ReadOnly), cancellation);
    }

    public ValueTask DisposeAsync() => file.DisposeAsync();
}
