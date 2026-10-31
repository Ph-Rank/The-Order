using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;
using OpenMcdf;
using TheOrder.Application;

namespace TheOrder.Infrastructure;

// A reading projection only: no Office automation, macros, formula evaluation,
// external relationships, HTML execution or write-back to the source file.
public sealed class LocalDocumentParser : ILocalDocumentParser
{
    private const int MaximumCharacters = 2_000_000;
    static LocalDocumentParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public ImmutableArray<ReadingPage> Parse(string extension, byte[] bytes, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (bytes.Length is < 1 or > 64 * 1024 * 1024) throw new InvalidDataException("文档为空或超过 64 MiB。");
        extension = extension.ToLowerInvariant();
        if (extension is ".docx" or ".xlsx") CheckZip(bytes);
        var blocks = extension switch
        {
            ".txt" or ".md" => Paragraphs(DecodeText(bytes)),
            ".rtf" => Paragraphs(RtfText(bytes)),
            ".doc" => Paragraphs(DocText(bytes)),
            ".docx" => Docx(bytes, cancellation),
            ".xls" or ".xlsx" => Excel(bytes, cancellation),
            _ => throw new InvalidDataException("不支持此文档格式。")
        };
        var pages = ImmutableArray.CreateBuilder<ReadingPage>(); var current = ImmutableArray.CreateBuilder<ReadingBlock>(); int length = 0;
        foreach (var block in blocks)
        {
            cancellation.ThrowIfCancellationRequested();
            int cost = block.Text.Length + (block.Rows.IsDefault ? 0 : block.Rows.Sum(r => r.Sum(c => c.Length))) + (block.Image is null ? 0 : 9000);
            if (length + cost > 9000 && current.Count > 0 || block.Kind == "table" && current.Count > 0 || current.Count >= 256)
            { if (current.Any(b => b.Kind != "space")) pages.Add(new($"第 {pages.Count + 1} 页", current.ToImmutable())); current.Clear(); length = 0; }
            current.Add(block); length += cost;
            if (pages.Count > 1000) throw new InvalidDataException("文档超过 1000 个阅读分页。");
        }
        if (current.Any(b => b.Kind != "space")) pages.Add(new($"第 {pages.Count + 1} 页", current.ToImmutable()));
        if (pages.Count == 0) pages.Add(new("第 1 页", [new("paragraph", "此文档没有可读取的正文。") ]));
        return pages.ToImmutable();
    }
    private static string Clean(string value) => new(value.Replace("\r\n", "\n").Replace('\r', '\n')
        .Select(c => c == '\t' || c == '\n' || !char.IsControl(c) ? c : ' ').ToArray());
    private static IEnumerable<ReadingBlock> Paragraphs(string text)
    {
        text = Clean(text); if (text.Length > MaximumCharacters) throw new InvalidDataException("文档正文超过 200 万字符。");
        foreach (var line in text.Split('\n'))
        {
            var value = line.TrimEnd();
            if (value.Length == 0) { yield return new("space", ""); continue; }
            for (int i = 0; i < value.Length; i += 8000) yield return new("paragraph", value.Substring(i, Math.Min(8000, value.Length - i)));
        }
    }
    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            // GB18030 is the common encoding for bundled Chinese guides. SJIS is
            // chosen only when decoding cleanly yields Japanese kana at a much higher rate.
            var chinese = Encoding.GetEncoding(54936).GetString(bytes); var japanese = Encoding.GetEncoding(932).GetString(bytes);
            int Kana(string s) => s.Count(c => c is >= '\u3041' and <= '\u30fe');
            return Kana(japanese) > Kana(chinese) * 2 + 8 && !japanese.Contains('\ufffd') ? japanese : chinese;
        }
    }
    internal static void CheckZip(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); long expanded = 0;
        if (zip.Entries.Count > 4096) throw new InvalidDataException("文档压缩结构过大。");
        foreach (var item in zip.Entries)
            if (item.Length < 0 || item.Length > 32 * 1024 * 1024 || (expanded = checked(expanded + item.Length)) > 64 * 1024 * 1024)
                throw new InvalidDataException("文档展开后超过阅读器限制。");
    }
    private static XDocument Xml(ZipArchiveEntry entry)
    {
        using var source = entry.Open(); using var reader = XmlReader.Create(source, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    private static IEnumerable<ReadingBlock> Docx(byte[] bytes, CancellationToken cancellation)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main",
            r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var document = Xml(zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Word 文档缺少正文。"));
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        if (zip.GetEntry("word/_rels/document.xml.rels") is { } rel)
            foreach (var e in Xml(rel).Root?.Elements() ?? [])
                if ((string?)e.Attribute("TargetMode") != "External" && (string?)e.Attribute("Id") is { } id && (string?)e.Attribute("Target") is { } target
                    && target.StartsWith("media/", StringComparison.Ordinal) && !target.Contains("..") && !target.Contains('\\')) relationships[id] = "word/" + target;
        int total = 0;
        string Text(XElement element) => Clean(element.Name != w + "p" ? string.Join("\n", element.Descendants(w + "p").Select(Text)) : string.Concat(element.Descendants().Where(e => e.Name == w + "t" || e.Name == w + "tab" || e.Name == w + "br")
            .Select(e => e.Name == w + "tab" ? "\t" : e.Name == w + "br" ? "\n" : e.Value)));
        foreach (var element in document.Root?.Element(w + "body")?.Elements() ?? [])
        {
            cancellation.ThrowIfCancellationRequested();
            if ((total += element.Value.Length) > MaximumCharacters) throw new InvalidDataException("Word 正文超过阅读限制。");
            if (element.Name == w + "tbl")
            {
                var rows = element.Elements(w + "tr").Select(tr => tr.Elements(w + "tc").Select(Text).ToImmutableArray()).ToArray();
                foreach (var chunk in rows.Chunk(24)) yield return new("table", "表格", chunk.ToImmutableArray());
            }
            else if (element.Name == w + "p")
            {
                var text = Text(element); bool heading = ((string?)element.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val"))?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true;
                foreach (var part in Paragraphs(text)) yield return heading ? part with { Kind = "heading" } : part;
                foreach (var image in element.Descendants(a + "blip"))
                    if ((string?)image.Attribute(r + "embed") is { } id && relationships.TryGetValue(id, out var target) && zip.GetEntry(target) is { } entry
                        && Path.GetExtension(target).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                    { using var stream = entry.Open(); using var output = new MemoryStream(); stream.CopyTo(output); yield return new("image", "文档插图", Image: output.ToArray()); }
            }
        }
    }
    private static IEnumerable<ReadingBlock> Excel(byte[] bytes, CancellationToken cancellation)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var workbook = ExcelReaderFactory.CreateReader(stream, new() { LeaveOpen = true }); int cells = 0, sheets = 0, characters = 0;
        do
        {
            if (++sheets > 128 || workbook.FieldCount > 128 || workbook.RowCount > 50000) throw new InvalidDataException("工作簿超过 128 列或阅读器容量限制。");
            var batch = new List<ImmutableArray<string>>(); int row = 0, first = 1;
            IEnumerable<ReadingBlock> Flush()
            {
                for (int column = 0; column < workbook.FieldCount; column += 8)
                {
                    string Letter(int i) { string s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
                    var header = ImmutableArray.CreateBuilder<string>(); header.Add("行"); for (int c = column; c < Math.Min(column + 8, workbook.FieldCount); c++) header.Add(Letter(c));
                    var rows = new List<ImmutableArray<string>> { header.ToImmutable() };
                    foreach (var values in batch) rows.Add([values[0], .. values.Skip(column + 1).Take(8)]);
                    if (rows.Skip(1).All(r => r.Skip(1).All(string.IsNullOrEmpty))) continue;
                    yield return new("table", $"{workbook.Name} · {first}—{row} 行 · {Letter(column)}—{Letter(Math.Min(column + 7, workbook.FieldCount - 1))} 列", rows.ToImmutableArray());
                }
            }
            while (workbook.Read())
            {
                cancellation.ThrowIfCancellationRequested(); row++; var values = ImmutableArray.CreateBuilder<string>(); values.Add(row.ToString(CultureInfo.InvariantCulture));
                for (int c = 0; c < workbook.FieldCount; c++)
                {
                    object? value = workbook.GetValue(c); if (value is not null && ++cells > 50000) throw new InvalidDataException("工作簿超过 5 万个非空单元格。");
                    string text = value switch { DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), bool b => b ? "是" : "否", _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
                    if (text.Length > 8000 || (characters += text.Length) > MaximumCharacters) throw new InvalidDataException("单元格内容过长或工作簿文字超过阅读器容量。"); values.Add(Clean(text));
                }
                if (batch.Count == 0) first = row;
                batch.Add(values.ToImmutable());
                if (batch.Count == 24) { foreach (var block in Flush()) yield return block; batch.Clear(); }
            }
            if (batch.Count > 0) foreach (var block in Flush()) yield return block;
        } while (workbook.NextResult());
    }
    // Word 97—2007+ compound binary body, using MS-DOC 2.4.1's piece table.
    // Formatting/embedded objects are deliberately absent from this text projection.
    internal static string DocText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith("{\\rtf"u8)) return RtfText(bytes);
        using var source = new MemoryStream(bytes, writable: false); using var root = RootStorage.Open(source, StorageModeFlags.LeaveOpen);
        byte[] Read(string name) { using var stream = root.OpenStream(name); if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Word 数据流过大。"); var value = new byte[checked((int)stream.Length)]; stream.ReadExactly(value); return value; }
        var word = Read("WordDocument");
        ushort U16(int p) { if (p < 0 || p > word.Length - 2) throw new InvalidDataException("Word 头不完整。"); return BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(p)); }
        int I32(int p) { if (p < 0 || p > word.Length - 4) throw new InvalidDataException("Word 头不完整。"); return BinaryPrimitives.ReadInt32LittleEndian(word.AsSpan(p)); }
        if (U16(0) != 0xa5ec || U16(2) < 0xc1 || (U16(10) & 0x8100) != 0) throw new InvalidDataException("不支持加密或 Word 97 之前的 DOC。");
        int pos = checked(32 + U16(32) * 2 + 2); int longs = U16(pos); int longStart = pos + 2; int mainCharacters = I32(longStart + 3 * 4);
        pos = checked(longStart + longs * 4); int pairs = U16(pos); pos += 2;
        if (pairs < 34 || mainCharacters < 0 || mainCharacters > MaximumCharacters) throw new InvalidDataException("Word 正文结构不受支持。");
        int fc = I32(pos + 33 * 8), lcb = I32(pos + 33 * 8 + 4); var table = Read((U16(10) & 0x200) == 0 ? "0Table" : "1Table");
        if (fc < 0 || lcb < 5 || fc > table.Length - lcb) throw new InvalidDataException("Word 分段表无效。");
        int end = fc + lcb;
        while (fc < end && table[fc] == 1) { if (fc > end - 3) throw new InvalidDataException(); int n = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(fc + 1)); fc = checked(fc + 3 + n); }
        if (fc > end - 5 || table[fc++] != 2) throw new InvalidDataException("Word 分段表缺失。");
        int length = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(fc)); fc += 4;
        if (length < 4 || (length - 4) % 12 != 0 || length > end - fc) throw new InvalidDataException("Word 分段表长度无效。");
        int count = (length - 4) / 12; var output = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            int start = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(fc + i * 4)), stop = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(fc + (i + 1) * 4));
            if (start < 0 || stop < start) throw new InvalidDataException("Word 字符范围无效。"); if (start >= mainCharacters) break;
            int n = Math.Min(stop, mainCharacters) - start; uint encoded = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(fc + (count + 1) * 4 + i * 8 + 2));
            bool compressed = (encoded & 0x40000000) != 0; int offset = checked((int)(encoded & 0x3fffffff) / (compressed ? 2 : 1)), size = checked(n * (compressed ? 1 : 2));
            if (offset < 0 || offset > word.Length - size) throw new InvalidDataException("Word 字符位置无效。");
            output.Append((compressed ? Encoding.GetEncoding(1252) : Encoding.Unicode).GetString(word, offset, size));
        }
        // Hide field instructions (e.g. INCLUDETEXT paths), retain only field results.
        var visible = new StringBuilder(); var fields = new Stack<bool>();
        foreach (char c in output.ToString())
        { if (c == '\u0013') fields.Push(false); else if (c == '\u0014' && fields.Count > 0) { fields.Pop(); fields.Push(true); } else if (c == '\u0015' && fields.Count > 0) fields.Pop(); else if (fields.All(v => v)) visible.Append(c == '\a' ? '\t' : c); }
        return visible.ToString();
    }
    private static string RtfText(byte[] bytes)
    {
        string input = Encoding.Latin1.GetString(bytes); var result = new StringBuilder(); var stack = new Stack<(bool Skip, int Uc)>(); bool skip = false; int uc = 1;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '{') { stack.Push((skip, uc)); if (stack.Count > 256) throw new InvalidDataException("RTF 嵌套过深。"); continue; }
            if (c == '}') { if (stack.TryPop(out var state)) (skip, uc) = state; continue; }
            if (c != '\\') { if (!skip && c is not ('\r' or '\n')) result.Append(c); continue; }
            if (++i >= input.Length) break; c = input[i];
            if (c is '\\' or '{' or '}') { if (!skip) result.Append(c); continue; }
            if (c == '*') { skip = true; continue; }
            if (c == '\'' && i + 2 < input.Length) { if (!skip && byte.TryParse(input.AsSpan(i + 1, 2), NumberStyles.HexNumber, null, out byte b)) result.Append(Encoding.GetEncoding(1252).GetString([b])); i += 2; continue; }
            int start = i; while (i < input.Length && char.IsAsciiLetter(input[i])) i++; string word = input[start..i]; int numberStart = i; if (i < input.Length && input[i] == '-') i++; while (i < input.Length && char.IsAsciiDigit(input[i])) i++;
            int.TryParse(input.AsSpan(numberStart, i - numberStart), out int number); if (i >= input.Length || input[i] != ' ') i--;
            if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict" or "object" or "header" or "footer" or "fldinst") skip = true;
            if (word == "bin") { if (number < 0 || number > input.Length - i - 1) throw new InvalidDataException(); i += number; }
            if (word == "uc") uc = Math.Clamp(number, 0, 8);
            if (!skip) { if (word is "par" or "line" or "row") result.Append('\n'); else if (word is "tab" or "cell") result.Append('\t'); else if (word == "u") { result.Append((char)(ushort)number); for (int f = 0; f < uc && i + 1 < input.Length && input[i + 1] is not ('{' or '}'); f++) i++; } }
            if (result.Length > MaximumCharacters) throw new InvalidDataException("RTF 正文过长。");
        }
        return result.ToString();
    }
}
