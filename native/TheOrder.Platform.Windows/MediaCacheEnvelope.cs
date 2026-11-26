using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

// The v1 envelope is shared by AniList media and MusicBrainz releases.
internal static class MediaCacheEnvelope
{
    public const int MaximumPayload = 4 * 1024 * 1024;
    public const int MaximumBytes = 6 * 1024 * 1024;
    public const int MaximumKeyLength = 100;

    private sealed record Envelope(int Version, Guid ProfileId, Guid StoreId, string Key, string Hash, byte[] Payload);

    public static string FileName(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json";

    public static byte[] Encode(NativeProfileStore profile, string key, byte[] payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, profile.Manifest.ProfileId, profile.Manifest.StoreId,
            key, Convert.ToHexString(SHA256.HashData(payload)), payload), ProfileMetadataCache.JsonOptions);

    public static T? ReadVerified<T>(string path, NativeProfileStore profile, string? expected,
        Func<T, string> keyOf, Func<T?, bool> validate) where T : class
    {
        try
        {
            using var file = IsolatedLibrarySource.OpenChecked(path);
            if (file.Length is < 1 or > MaximumBytes) return null;
            var saved = JsonSerializer.Deserialize<Envelope>(file, ProfileMetadataCache.JsonOptions);
            if (saved is null || saved.Version != 1 || saved.ProfileId != profile.Manifest.ProfileId
                || saved.StoreId != profile.Manifest.StoreId || saved.Key is not { Length: <= MaximumKeyLength } key
                || expected is not null && key != expected || Path.GetFileName(path) != FileName(key)
                || saved.Payload is not { Length: > 0 and <= MaximumPayload }
                || saved.Hash != Convert.ToHexString(SHA256.HashData(saved.Payload))) return null;
            var value = JsonSerializer.Deserialize<T>(saved.Payload, ProfileMetadataCache.JsonOptions);
            return value is not null && keyOf(value) == key && validate(value) ? value : null;
        }
        catch (Exception error) when (ProfileMetadataCache.IsReadFailure(error)) { return null; }
    }
}
