using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public static class BangumiBookFactsMapping
{
    public static BangumiBookFacts Map(JsonElement root)
    {
        var facts = new List<BookFactValue>();
        var isbnValues = new List<string>();
        var external = new List<BookExternalLink>();
        var linkFields = new HashSet<string>(StringComparer.Ordinal) { "官方网站", "官网", "官方站点", "连载地址", "连载网站", "阅读地址", "试读", "試し読み", "电子版", "电子书", "纸质书", "纸质购买", "购买链接", "出版社", "出版方" };
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "作者", "原作", "作画", "插图", "插画", "出版社", "连载杂志", "连载刊物", "文库", "书系", "丛书", "ISBN", "ISBN-13", "ISBN-10", "册数", "话数", "卷数", "卷", "卷次", "发售日", "出版日期", "开始", "结束", "连载开始", "连载结束", "版本", "页数", "单行本", "新装版", "文库版" };
        if (root.TryGetProperty("infobox", out var box) && box.ValueKind == JsonValueKind.Array)
            foreach (var field in box.EnumerateArray().Take(256))
            {
                if (facts.Count >= 64) break;
                if (field.ValueKind != JsonValueKind.Object) throw new BangumiRelationsException(BangumiRelationsFailure.InvalidResponse, "Bangumi 书籍字段格式不一致。");
                if (!field.TryGetProperty("key", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() is not { } key || !(allowed.Contains(key) || linkFields.Contains(key))
                    || !field.TryGetProperty("value", out var value)) continue;
                var values = new List<string>();
                if (value.ValueKind == JsonValueKind.String) Add(value.GetString());
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (var entry in value.EnumerateArray().Take(64))
                    {
                        if (entry.ValueKind == JsonValueKind.String) Add(entry.GetString());
                        else if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.String)
                        {
                            var prefix = entry.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                            Add(string.IsNullOrWhiteSpace(prefix) ? v.GetString() : prefix + " · " + v.GetString(), v.GetString());
                        }
                    }
                if (values.Count > 0) facts.Add(new(key, values.Distinct().ToArray()));
                void Add(string? text, string? identifierValue = null)
                {
                    if (text is null || !AniListValidation.Text(text, 2000, false)) return;
                    values.Add(text.Trim());
                    var address = (identifierValue ?? text).Trim();
                    if (linkFields.Contains(key) && WatchLinks.SafeUrl(address) && external.Count < 96)
                        external.Add(new(0, key, address, key));
                    // Keep the label for display, but parse identity from its original value.
                    if (key is "ISBN" or "ISBN-13" or "ISBN-10" && BookIsbn.Normalize(identifierValue ?? text) is { } isbn)
                        isbnValues.Add(isbn);
                }
            }
        string[] Read(params string[] names) => facts.Where(x => names.Contains(x.Name, StringComparer.Ordinal)).SelectMany(x => x.Values).Distinct().Take(64).ToArray();
        int? Count(string key) => root.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var count) && count is >= 0 and <= 1000000 ? count : null;
        bool? series = root.TryGetProperty("series", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean() : null;
        var isbns = isbnValues.Distinct().Take(64).ToArray();
        return new(series, Count("volumes"), Count("eps"), isbns, Read("作者", "原作"), Read("插图", "插画", "作画"),
            Read("出版社"), Read("连载杂志", "连载刊物"), Read("文库", "书系", "丛书"), Read("卷", "卷次").FirstOrDefault())
            { RawFacts = facts.ToArray(), ExternalLinks = external.Distinct().ToArray(), HasExternalLinkMetadata = true };
    }
}
