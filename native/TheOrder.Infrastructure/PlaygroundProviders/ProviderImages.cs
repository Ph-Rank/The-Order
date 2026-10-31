using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TheOrder.Application.Playground;

namespace TheOrder.Infrastructure.PlaygroundProviders;

internal sealed partial class NativePlaygroundProvider
{
    private const string ImageMarker = "__the_order_image";

    private Dictionary<string, PlaygroundAttachment> ImageCatalog(PlaygroundProviderRequest request)
    {
        var images = ImmutableArray.CreateBuilder<PlaygroundAttachment>();
        if (request.ToolResults.Any(message => !message.Attachments.IsDefaultOrEmpty)) throw Invalid("本地工具结果不能附带图片。");
        foreach (var message in request.Messages)
        {
            if (message.Attachments.IsDefaultOrEmpty) continue;
            if (message.Role != "user" || message.Attachments.Any(image => image is null || image.ImageBase64 is null || image.Text is null || image.Text.Length != 0))
                throw Invalid("图片只能作为用户手动选择的独立图片快照发送。");
            images.AddRange(message.Attachments);
        }
        if (images.Count == 0) return new(StringComparer.Ordinal);
        if (request.Phase != PlaygroundPhase.Discussion || !Capabilities(request.Model, request.Endpoint).ImageInput)
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "当前模型的图片输入不支持或尚未核实；请先选择支持图片的模型。"));
        try { PlaygroundAttachmentRules.Validate(images.ToImmutable()); }
        catch (ArgumentException) { throw Invalid("本轮图片格式、尺寸或容量不符合附件限制。最多 4 张、单张 4 MiB、合计 8 MiB。"); }
        var result = new Dictionary<string, PlaygroundAttachment>(StringComparer.Ordinal);
        for (int messageIndex = 0; messageIndex < request.Messages.Length; messageIndex++)
        {
            var message = request.Messages[messageIndex];
            if (message.Attachments.IsDefaultOrEmpty) continue;
            for (int imageIndex = 0; imageIndex < message.Attachments.Length; imageIndex++)
            {
                var image = message.Attachments[imageIndex];
                if (image.MediaType is not ("image/png" or "image/jpeg")) throw Invalid("图片必须是已重新编码的 PNG 或 JPEG 快照。");
                result.Add(ImageReference(messageIndex, imageIndex, image), image);
            }
        }
        return result;
    }

    private static string ImageReference(int message, int index, PlaygroundAttachment image)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(image.Name + "\0" + image.MediaType + "\0" + image.ImageWidth.ToString(CultureInfo.InvariantCulture)
            + "x" + image.ImageHeight.ToString(CultureInfo.InvariantCulture) + "\0"));
        hash.AppendData(Encoding.UTF8.GetBytes(image.ImageBase64!));
        return message.ToString(CultureInfo.InvariantCulture) + ":" + index.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CheckImageReferences(JsonArray items, IReadOnlyDictionary<string, PlaygroundAttachment> images)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item?["content"] is not JsonArray content) continue;
            foreach (var block in content)
            {
                string type = String(block?["type"]);
                if (type is "image" or "input_image") throw Invalid("服务商续接状态不能保存原始图片数据。");
                if (type != ImageMarker) continue;
                string reference = String(block?["ref"]);
                if (block is not JsonObject marker || marker.Count != 2 || !images.ContainsKey(reference) || !seen.Add(reference))
                    throw Invalid("本轮图片快照在工具调用之间发生变化，请重新发送。");
            }
        }
        if (seen.Count != images.Count) throw Invalid("本轮图片快照与服务商续接状态不一致，请重新发送。");
    }

    private JsonArray ExpandImages(PlaygroundProviderRequest request, JsonArray items)
    {
        var images = ImageCatalog(request);
        CheckImageReferences(items, images);
        var result = (JsonArray)items.DeepClone();
        foreach (var item in result)
        {
            if (item?["content"] is not JsonArray content) continue;
            for (int index = 0; index < content.Count; index++)
            {
                if (String(content[index]?["type"]) != ImageMarker) continue;
                var image = images[String(content[index]?["ref"])];
                content[index] = Kind == PlaygroundProviderKind.Gemini
                    ? new JsonObject { ["type"] = "image", ["data"] = image.ImageBase64, ["mime_type"] = image.MediaType }
                    : Kind == PlaygroundProviderKind.Claude
                    ? new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = image.ImageBase64 } }
                    : new JsonObject { ["type"] = "input_image", ["image_url"] = "data:" + image.MediaType + ";base64," + image.ImageBase64 };
            }
        }
        return result;
    }
}
