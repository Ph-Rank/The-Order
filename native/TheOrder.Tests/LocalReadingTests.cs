using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OpenMcdf;
using TheOrder.Application;
using TheOrder.Infrastructure;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class LocalReadingTests
{
    private readonly LocalDocumentParser parser = new();
    private static string Text(ImmutableArray<ReadingPage> pages) => string.Join("\n", pages.SelectMany(p => p.Blocks).Select(b => b.Text));
    [Theory] [InlineData("utf8")] [InlineData("utf16")] [InlineData("gb18030")]
    public void TextEncodingsPreserveChineseAndJapanese(string kind)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); const string text = "攻略 · 宁宁\r\n次の選択肢\n存档 01";
        var encoding = kind == "utf8" ? Encoding.UTF8 : kind == "utf16" ? Encoding.Unicode : Encoding.GetEncoding(54936);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray(); var pages = parser.Parse(".txt", bytes, default);
        Assert.Contains("宁宁", Text(pages)); Assert.Contains("次の選択肢", Text(pages)); Assert.DoesNotContain('\r', Text(pages));
    }
    [Fact] public void LongTextIsPagedWithoutLosingFinalLine()
    {
        var text = string.Join('\n', Enumerable.Range(1, 2000).Select(i => $"第 {i} 步：选择路线，然后继续。"));
        var pages = parser.Parse(".txt", Encoding.UTF8.GetBytes(text), default);
        Assert.True(pages.Length > 1); Assert.Contains("第 2000 步", Text(pages)); Assert.Equal(2000, pages.SelectMany(p => p.Blocks).Count(b => b.Kind == "paragraph"));
    }
    [Fact] public void ThousandsOfShortLinesRemainReadableThroughTheBoundedOverlay()
    {
        var pages = parser.Parse(".txt", Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Repeat("a", 5000))), default);
        Assert.Equal(5000, pages.Sum(p => p.Blocks.Count(b => b.Kind == "paragraph")));
        foreach (var page in pages) Assert.NotEmpty(GuideOverlayWire.Encode(GuideOverlayDocument.Message("g", "", "short lines", "") with { Reading = new(1, ["guide.txt"], 0, 0, pages.Length, page) }));
    }
    internal static byte[] Zip(params (string Path, string Text)[] entries)
    {
        using var output = new MemoryStream(); using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Path).Open(), Encoding.UTF8); writer.Write(entry.Text); }
        return output.ToArray();
    }
    [Fact] public void WordRetainsHeadingsParagraphsAndTablesWithoutOpeningExternalLinks()
    {
        var bytes = Zip(("word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>共通路线</w:t></w:r></w:p>
            <w:p><w:r><w:t>先存档 01</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>选项 A</w:t></w:r></w:p><w:p><w:r><w:t>下一段</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>宁宁</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            </w:body></w:document>
            """), ("word/_rels/document.xml.rels", "<Relationships><Relationship Id='r1' Target='https://untrusted.invalid/macro' TargetMode='External'/></Relationships>"));
        var pages = parser.Parse(".docx", bytes, default); var blocks = pages.SelectMany(p => p.Blocks).ToArray();
        Assert.Contains(blocks, b => b.Kind == "heading" && b.Text == "共通路线"); Assert.Contains("先存档 01", Text(pages));
        Assert.Contains(blocks, b => !b.Rows.IsDefault && b.Rows[0][0] == "选项 A\n下一段" && b.Rows[0][1] == "宁宁");
    }
    internal static byte[] BinaryDoc(string text, bool encrypted = false)
    {
        byte[] word = new byte[4096]; using (var stream = new MemoryStream(word)) using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0xa5ec); writer.Write((ushort)0xc1); stream.Position = 10; writer.Write((ushort)(encrypted ? 0x100 : 0));
            stream.Position = 32; writer.Write((ushort)14); stream.Position = 62; writer.Write((ushort)22); stream.Position = 76; writer.Write(text.Length);
            stream.Position = 152; writer.Write((ushort)93); stream.Position = 418; writer.Write(0); writer.Write(21); stream.Position = 2048; writer.Write(Encoding.Unicode.GetBytes(text));
        }
        using var table = new MemoryStream(); using (var writer = new BinaryWriter(table, Encoding.UTF8, true)) { writer.Write((byte)2); writer.Write(16); writer.Write(0); writer.Write(text.Length); writer.Write((ushort)0); writer.Write(2048); writer.Write((ushort)0); }
        using var result = new MemoryStream(); using (var root = RootStorage.Create(result, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        { using (var s = root.CreateStream("WordDocument")) s.Write(word); using (var s = root.CreateStream("0Table")) s.Write(table.ToArray()); }
        return result.ToArray();
    }
    [Fact] public void LegacyWordReadsOnlyMainStoryAndRejectsEncryption()
    {
        var bytes = BinaryDoc("宁宁路线\r先存档\rEND"); Assert.Contains("宁宁路线", Text(parser.Parse(".doc", bytes, default))); Assert.Contains("END", Text(parser.Parse(".doc", bytes, default)));
        Assert.Throws<InvalidDataException>(() => parser.Parse(".doc", BinaryDoc("secret", true), default));
    }
    internal static byte[] ExcelFile() => Zip(("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/></Types>"),
        ("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>"),
        ("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='攻略表' sheetId='1' r:id='r1'/><sheet name='备注' sheetId='2' r:id='r2'/></sheets></workbook>"),
        ("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/><Relationship Id='r2' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet2.xml'/></Relationships>"),
        ("xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><dimension ref='A1:J2'/><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>存档 01</t></is></c><c r='J1' t='inlineStr'><is><t>最后一列</t></is></c></row><row r='2'><c r='B2'><f>1+1</f><v>2</v></c></row></sheetData></worksheet>"),
        ("xl/worksheets/sheet2.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><dimension ref='A1'/><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>只读说明</t></is></c></row></sheetData></worksheet>"));
    [Fact] public void WorkbookPreservesSheetsAndFinalColumnUsingCachedFormulaResult()
    {
        var blocks = parser.Parse(".xlsx", ExcelFile(), default).SelectMany(p => p.Blocks).ToArray();
        Assert.Contains(blocks, b => b.Text.StartsWith("攻略表")); Assert.Contains(blocks, b => b.Text.StartsWith("备注"));
        var cells = blocks.Where(b => !b.Rows.IsDefault).SelectMany(b => b.Rows).SelectMany(r => r).ToArray();
        Assert.Contains("最后一列", cells); Assert.Contains("存档 01", cells); Assert.Contains("只读说明", cells); Assert.Contains("2", cells); Assert.DoesNotContain("1+1", cells);
    }
    [Fact] public void CorruptAndXmlEntityDocumentsFailWithoutTreatingThemAsEmpty()
    {
        Assert.ThrowsAny<Exception>(() => parser.Parse(".docx", [1, 2, 3], default));
        var entity = Zip(("word/document.xml", "<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///C:/private'>]><x>&external;</x>"));
        Assert.Throws<System.Xml.XmlException>(() => parser.Parse(".docx", entity, default));
        Assert.Throws<OperationCanceledException>(() => parser.Parse(".txt", [65], new CancellationToken(true)));
    }
    [Fact] public async Task ReaderIsReadOnlyAndNeverDiscoversUnselectedFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "TheOrder-local-reader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "攻略.txt"); await File.WriteAllTextAsync(path, "手动选择后阅读"); byte[] before = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var service = new WindowsLocalReading(new WindowsFilesystemIdentity(), parser);
            var entry = new TheOrder.Domain.LibraryEntry("g", "g", null, null, [], null, "未开始", null, false, [], [], "", 0, 0, [], DateTimeOffset.UtcNow.ToString("O"), null, null, null, new(false, null));
            var found = await service.FindAsync(entry, default); Assert.Empty(found.Guides); Assert.Empty(found.Notes);
            using var document = await service.OpenAsync(new(path, "攻略.txt", "guide"), default);
            Assert.Contains("手动选择", (await document.PageAsync(0, default)).Blocks[0].Text); Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public void OverlayCarriesOnlyBoundedVisiblePageAndRejectsInvalidPixels()
    {
        var page = new ReadingPage("第 1 页", [new("paragraph", "只读")], 1, 1, [10, 20, 30, 255]);
        var doc = GuideOverlayDocument.Message("g", "", "标题", "") with { Reading = new(2, ["说明.txt"], 0, 0, 1, page) };
        Assert.True(GuideOverlayWire.Encode(doc).Length < 1024);
        Assert.Throws<InvalidDataException>(() => GuideOverlayWire.Encode(doc with { Reading = doc.Reading! with { Content = page with { ImageWidth = 2 } } }));
    }
    [Fact] public async Task LongPictureInsideWordIsSplitWithoutDroppingItsBottom()
    {
        using var encoded = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, encoded);
        var pixels = new byte[16 * 4096 * 4]; for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, 16, 4096, 96, 96, pixels); await encoder.FlushAsync(); encoded.Seek(0);
        byte[] image = new byte[checked((int)encoded.Size)]; using (var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(encoded)) await stream.ReadExactlyAsync(image);
        using var docx = new MemoryStream(); using (var zip = new ZipArchive(docx, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) w.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><w:body><w:p><a:blip r:embed='r1'/></w:p></w:body></w:document>");
            using (var w = new StreamWriter(zip.CreateEntry("word/_rels/document.xml.rels").Open())) w.Write("<Relationships><Relationship Id='r1' Target='media/long.png'/></Relationships>");
            using var picture = zip.CreateEntry("word/media/long.png").Open(); picture.Write(image);
        }
        string folder = Path.Combine(Path.GetTempPath(), "TheOrder-inline-picture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "guide.docx"); await File.WriteAllBytesAsync(file, docx.ToArray());
            using var reading = await new WindowsLocalReading(new WindowsFilesystemIdentity(), parser).OpenAsync(new(file, "guide.docx", "guide"), default);
            Assert.Equal(2, reading.PageCount); Assert.Equal(2048, (await reading.PageAsync(0, default)).ImageHeight); Assert.Equal(2048, (await reading.PageAsync(1, default)).ImageHeight);
        }
        finally { Directory.Delete(folder, true); }
    }
}
