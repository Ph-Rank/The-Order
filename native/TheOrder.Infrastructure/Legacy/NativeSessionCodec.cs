using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Legacy;

internal sealed class UnsupportedSessionSchemaException : Exception;
internal static class NativeSessionCodec
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info => {
            if (info.Type == typeof(NativeSession))
            {
                var computed = info.Properties.FirstOrDefault(p => p.Name == "isTerminal");
                if (computed is not null) info.Properties.Remove(computed);
                var minimum = info.Properties.FirstOrDefault(p => p.Name == "awaitingRecordingMinimum");
                if (minimum is not null) minimum.ShouldSerialize = (_, value) => value is true;
            }
            // Keep legacy v1 bytes readable by the old strict reader. Domain records
            // remain serialization-independent; new overrides are emitted only in v2.
            if (info.Type == typeof(LaunchCompatibility)) {
                var auxiliaries = info.Properties.FirstOrDefault(p => p.Name == "auxiliaries");
                if (auxiliaries is not null) auxiliaries.ShouldSerialize = (_, value) => value is not null;
            }
        });
        return new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, TypeInfoResolver = resolver,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new CompatibleString() } };
    }
    private sealed class CompatibleString : JsonConverter<string>
    {
        private readonly LegacyLibraryCodec.Utf16Writer writer = new();
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        { using var document = JsonDocument.ParseValue(ref reader); return LegacyJson.String(document.RootElement); }
        public override void Write(Utf8JsonWriter output, string value, JsonSerializerOptions options) => writer.Write(output, value, options);
    }
    private static readonly HashSet<string> Fields = ["version", "gameId", "installationId", "startedAtUtc", "createdObservedAtUtc", "endedAtUtc",
        "accountingCutoffAtUtc", "accumulated100ns", "lifecycle", "reason", "userPaused", "revision", "writerOwner", "recovery", "recoveryToken", "awaitingRecordingMinimum"];
    internal static NativeSession Read(JsonElement value, long seconds)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("native session 必须为对象。");
        if (value.EnumerateObject().Select(p => p.Name).Distinct().Count() != value.EnumerateObject().Count()) throw new InvalidDataException("重复的 native session 字段。");
        if (!value.TryGetProperty("version", out var v) || !v.TryGetInt32(out var version)) throw new InvalidDataException("缺少 session version。");
        if (version != 1 || value.EnumerateObject().Any(p => !Fields.Contains(p.Name))) throw new UnsupportedSessionSchemaException();
        FutureEnum<SessionLifecycle>(value, "lifecycle"); FutureEnum<SessionReason>(value, "reason");
        if (value.TryGetProperty("recovery", out var recovery) && recovery.ValueKind == JsonValueKind.Object
            && recovery.EnumerateObject().Any(p => p.Name is not ("count" or "lastAttemptId"))) throw new UnsupportedSessionSchemaException();
        try
        {
            var native = value.Deserialize<NativeSession>(Options) ?? throw new InvalidDataException("空 session。");
            native.Validate();
            if (native.Accumulated100ns / NativeSession.TicksPerSecond != seconds || native.IsTerminal && native.AwaitingRecordingMinimum)
                throw new InvalidDataException("Session accounted marker 不一致。");
            return native;
        }
        catch (JsonException e) { throw new InvalidDataException("Session v1 字段无效或缺失。", e); }
    }
    private static void FutureEnum<T>(JsonElement value, string name) where T : struct, Enum
    {
        if (!value.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null) return;
        if (e.ValueKind == JsonValueKind.String && (!Enum.TryParse<T>(e.GetString(), false, out var parsed) || !Enum.IsDefined(parsed)))
            throw new UnsupportedSessionSchemaException();
    }
}
