using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// Optional companion: never changes native-ui v1, required capabilities, writer
// version or bootstrap. Older native binaries ignore this file. The profile's
// existing exclusive writer lease owns it; adapters neither acquire nor release it.
public sealed class MaterialPreferencesStore(NativeConfiguration configuration,
    Func<PersistenceStage, Task>? probe = null) : IMaterialPreferencesStore, IAsyncDisposable
{
    public const string FileName = "native-material-v1.json";
    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, string Mode);
    private readonly ProfilePreferencesFile file = new(configuration, FileName, typeof(MaterialPreferencesStore),
        "Native profile required for material persistence", probe);

    private static MaterialPreferenceRead Decode(byte[] bytes, NativeProfileStore profile)
    {
        if (bytes.Length > ProfilePreferencesFile.MaximumBytes) return new(new(), UiText.Get("材质设置文件过大，已使用标准材质；原文件保留。"), true);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var names = ProfilePreferencesFile.ReadUniqueNames(document.RootElement);
            if (!ProfilePreferencesFile.IsVersionOne(document.RootElement))
                return new(new(), UiText.Get("材质设置版本不受支持，已使用标准材质；原文件保留。"), true);
            if (names.Any(name => name is not ("Version" or "ProfileId" or "StoreId" or "Mode")))
                return new(new(), UiText.Get("材质设置包含不受支持的字段，已使用标准材质；原文件保留。"), true);
            var value = JsonSerializer.Deserialize<Envelope>(bytes, ProfilePreferencesFile.JsonOptions) ?? throw new JsonException();
            if (value.ProfileId != profile.Manifest.ProfileId || value.StoreId != profile.Manifest.StoreId)
                return new(new(), UiText.Get("材质设置不属于当前资料，已使用标准材质；原文件保留。"), true);
            return MaterialActivationPolicy.Known(value.Mode) ? new(new(value.Mode))
                : new(new(), UiText.Get("材质选项未知，已使用标准材质；可重新选择。"));
        }
        catch (JsonException) { return new(new(), UiText.Get("材质设置格式损坏，已使用标准材质；可重新选择。")); }
    }

    public Task<MaterialPreferenceRead> LoadAsync(CancellationToken cancellation = default) =>
        file.LoadAsync(new MaterialPreferenceRead(new()), Decode, cancellation);

    public Task SaveAsync(MaterialPreference preference, CancellationToken cancellation = default)
    {
        if (!MaterialActivationPolicy.Known(preference.Mode)) throw new ArgumentException("Unknown material mode", nameof(preference));
        return file.SaveAsync(profile => new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId, preference.Mode),
            Decode, read => (read.Warning, read.ReadOnly), cancellation);
    }

    public ValueTask DisposeAsync() => file.DisposeAsync();
}
