using System.Text.Json;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    // A private envelope has its own raw capacity limit. Its escaped JSON and
    // opaque protocol signatures are not readable conversation text or tokens.
    // Never parse a message/result/arguments STRING as a second JSON document:
    // a document containing {"signature":"..."} is still fully counted text.
    private static long NativeEnvelopeCharacters(string data)
    {
        if (data.Length > MaximumTapeCharacters)
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported,
                "原生续接状态超过内存容量，已停止且未自动重发。"));
        try
        {
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 48 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return LiteralCharacters(document.RootElement);
            long count = 0;
            foreach (var property in document.RootElement.EnumerateObject())
                count += property.Name.Length + (property.Name == "items" && property.Value.ValueKind == JsonValueKind.Array
                    ? ProtocolCharacters(property.Value) : LiteralCharacters(property.Value));
            return count;
        }
        catch (JsonException) { return data.Length; }
    }
    private static long LiteralCharacters(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!.Length,
        JsonValueKind.Array => value.EnumerateArray().Sum(LiteralCharacters),
        JsonValueKind.Object => value.EnumerateObject().Sum(p => (long)p.Name.Length + LiteralCharacters(p.Value)),
        JsonValueKind.Null or JsonValueKind.Undefined => 0,
        _ => value.GetRawText().Length
    };
    private static long ProtocolCharacters(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Sum(ProtocolCharacters);
        if (value.ValueKind != JsonValueKind.Object) return LiteralCharacters(value);
        string type = value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
        string role = value.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
        bool reasoning = type is "thought" or "thinking" or "redacted_thinking" or "reasoning";
        bool signed = reasoning || type is "function_call" or "model_output" or "text" or "output_text";
        bool contents = reasoning || type is "message" or "model_output" or "user_input" || role is "assistant" or "model" or "user";
        long count = 0;
        foreach (var property in value.EnumerateObject())
        {
            count += property.Name.Length;
            if (property.Value.ValueKind == JsonValueKind.String
                && (signed && (property.Name is "signature" or "thought_signature")
                    || reasoning && property.Name == "encrypted_content")) continue;
            count += contents && (property.Name is "content" or "summary") && property.Value.ValueKind == JsonValueKind.Array
                ? ProtocolCharacters(property.Value) : LiteralCharacters(property.Value);
        }
        return count;
    }
}
