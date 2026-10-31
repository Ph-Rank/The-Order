using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

internal sealed partial class NativePlaygroundProvider
{
    private const int MaximumPrivateStateCharacters = 2 * 1024 * 1024;

    private JsonArray ConversationItems(PlaygroundProviderRequest request)
    {
        var tape = request.ConversationTape ?? throw Invalid();
        if (Kind != PlaygroundProviderKind.Gemini || request.Phase != PlaygroundPhase.Discussion
            || !Capabilities(request.Model, request.Endpoint).Verified
            || string.IsNullOrWhiteSpace(request.ConversationBinding) || request.ConversationBinding.Length > 512
            || tape.Binding != request.ConversationBinding || tape.Data.Length > MaximumPrivateStateCharacters
            || request.Messages.Any(m => m.Role != "user" || !m.Attachments.IsDefaultOrEmpty))
            throw Invalid("原生会话状态与当前连接或图片边界不一致。");
        var envelope = JsonNode.Parse(tape.Data, documentOptions: new() { MaxDepth = 48 }) ?? throw Invalid();
        if (String(envelope["kind"]) != "gemini-conversation-v1" || String(envelope["wire"]) != Wire
            || String(envelope["binding"]) != request.ConversationBinding || String(envelope["provider"]) != Kind.ToString()
            || String(envelope["model"]) != CleanModel(request.Model)
            || String(envelope["endpoint"]) != PlaygroundProviderEndpoints.Normalize(Kind, request.Endpoint)
            || String(envelope["conversation"]) != request.ConversationId.ToString("N")
            || !Guid.TryParseExact(String(envelope["last_turn"]), "N", out var previousTurn)
            || previousTurn == request.TurnId || envelope["items"] is not JsonArray items || items.Count < 1
            || ContainsTapeImage(items))
            throw Invalid("已拒绝失效或跨会话、服务商、模型的原生会话状态。");
        // The host validates permissions, admitted historical messages and the
        // exact library snapshot before it supplies this chain binding. Provider
        // identities are checked again here, before credentials or HTTP access.
        return (JsonArray)items.DeepClone();
    }

    private PlaygroundConversationTape? MakeConversationTape(PlaygroundProviderRequest request, JsonArray items, JsonArray output)
    {
        if (Kind != PlaygroundProviderKind.Gemini || !Capabilities(request.Model, request.Endpoint).Verified
            || string.IsNullOrWhiteSpace(request.ConversationBinding)
            || request.ConversationBinding.Length > 512 || ContainsTapeImage(items) || ContainsTapeImage(output)) return null;
        var history = (JsonArray)items.DeepClone();
        foreach (var step in output) history.Add(step?.DeepClone());
        if (history.Count < 1) return null;
        string data = new JsonObject { ["kind"] = "gemini-conversation-v1", ["wire"] = Wire,
            ["binding"] = request.ConversationBinding, ["provider"] = Kind.ToString(), ["model"] = CleanModel(request.Model),
            ["endpoint"] = PlaygroundProviderEndpoints.Normalize(Kind, request.Endpoint),
            ["conversation"] = request.ConversationId.ToString("N"), ["last_turn"] = request.TurnId.ToString("N"),
            ["items"] = history }.ToJsonString();
        // A valid final answer remains complete even if a chain cannot fit the
        // bounded ephemeral cache. The host then starts a clearly portable chain.
        return data.Length <= MaximumPrivateStateCharacters ? new(request.ConversationBinding, data) : null;
    }

    private static bool ContainsTapeImage(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (String(value["type"]) is ImageMarker or "image" or "input_image") return true;
            return value.Any(field => ContainsTapeImage(field.Value));
        }
        return node is JsonArray array && array.Any(ContainsTapeImage);
    }
}
