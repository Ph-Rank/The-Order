using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheOrder.Infrastructure.Migration;

// No archive path is ever used as an output path. Database members are copied to
// randomly named, exclusively owned temporary files; SQL from an input is never run.
internal sealed class InterchangeArchive : IDisposable
{
    private readonly ZipArchive zip;
    internal Dictionary<string, ZipArchiveEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal InterchangeArchive(Stream source)
    {
        if (!source.CanSeek || source.Length > PotatoVnImportAdapter.MaximumArchiveBytes) throw new InvalidDataException("SOURCE_TOO_LARGE");
        PotatoVnImportAdapter.CheckDirectory(source); source.Position = 0;
        zip = new(source, ZipArchiveMode.Read, true);
        try
        {
            long expanded = 0;
            foreach (var e in zip.Entries)
            {
                var name = e.FullName.Replace('\\', '/');
                if (name.Length is 0 or > 1000 || name.Any(char.IsControl) || name.StartsWith('/') || name.Contains(':')
                    || name.Split('/').Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' '))
                    || ((e.ExternalAttributes >> 16) & 0xf000) == 0xa000 || !Entries.TryAdd(name, e))
                    throw new InvalidDataException("UNSAFE_OR_DUPLICATE_ARCHIVE_PATH");
                expanded = checked(expanded + e.Length);
                if (expanded > 2L * 1024 * 1024 * 1024 || e.Length > 1024L * 1024 * 1024) throw new InvalidDataException("ARCHIVE_EXPANSION_LIMIT");
            }
        }
        catch { zip.Dispose(); throw; }
    }
    internal bool Has(string name) => Entries.ContainsKey(name);
    internal byte[] Read(string name, int maximum = 32 * 1024 * 1024)
    {
        var e = Entries.GetValueOrDefault(name) ?? throw new InvalidDataException("MISSING_BACKUP_MEMBER");
        if (e.Length > maximum || e.Length > 1024 * Math.Max(e.CompressedLength, 1)) throw new InvalidDataException("METADATA_EXPANSION_LIMIT");
        using var input = e.Open(); using var output = new MemoryStream(); var buffer = new byte[65536];
        int n; while ((n = input.Read(buffer, 0, buffer.Length)) != 0)
        { if (output.Length + n > maximum) throw new InvalidDataException("METADATA_EXPANSION_LIMIT"); output.Write(buffer, 0, n); }
        if (output.Length != e.Length) throw new InvalidDataException("TRUNCATED_BACKUP_MEMBER");
        return output.ToArray();
    }
    internal string Text(string name) => new UTF8Encoding(false, true).GetString(Read(name)).TrimStart('\ufeff');
    internal JsonElement Json(string name) => ImportJson.Parse(Read(name));
    public void Dispose() => zip.Dispose();
}

internal sealed class InterchangeTemp : IDisposable
{
    private readonly string parent = Path.GetFullPath(Path.GetTempPath());
    internal string Root { get; }
    internal InterchangeTemp()
    {
        Root = Path.Combine(parent, "the-order-interchange-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(Root)) throw new IOException("TEMP_COLLISION");
        Directory.CreateDirectory(Root);
    }
    internal string Write(byte[] bytes, string safeName = "snapshot.db")
    {
        if (!Regex.IsMatch(safeName, "^[a-z0-9.-]{1,60}$", RegexOptions.CultureInvariant)) throw new InvalidDataException();
        var path = Path.Combine(Root, safeName);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); output.Write(bytes); return path;
    }
    public void Dispose()
    {
        // Only our new UUID child can be removed. Never follow a replaced directory.
        if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent.TrimEnd(Path.DirectorySeparatorChar)
            || !Path.GetFileName(Root).StartsWith("the-order-interchange-", StringComparison.Ordinal)) return;
        try
        {
            if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) return;
            var pending = new Stack<string>(); pending.Push(Root);
            while (pending.TryPop(out var current))
                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return;
                    pending.Push(dir);
                }
            Directory.Delete(Root, true);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class ImportJson
{
    internal static JsonElement Parse(ReadOnlyMemory<byte> bytes)
    {
        using var d = JsonDocument.Parse(bytes, new() { MaxDepth = 32 }); var nodes = 0; Check(d.RootElement, ref nodes); return d.RootElement.Clone();
    }
    internal static JsonElement Parse(string text) => Parse(Encoding.UTF8.GetBytes(text));
    private static void Check(JsonElement e, ref int nodes)
    {
        if (++nodes > 3_000_000) throw new InvalidDataException("SOURCE_NODE_LIMIT");
        if (e.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name.Length > 200 || p.Name is "$type" or "_type" or "$values" || !keys.Add(p.Name) || keys.Count > 10000)
                    throw new InvalidDataException("DUPLICATE_OR_TYPED_JSON_PROPERTY");
                Check(p.Value, ref nodes);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        { if (e.GetArrayLength() > 10000) throw new InvalidDataException("SOURCE_ARRAY_LIMIT"); foreach (var v in e.EnumerateArray()) Check(v, ref nodes); }
        else if (e.ValueKind == JsonValueKind.String && e.GetString()!.Length > 100000) throw new InvalidDataException("SOURCE_FIELD_TOO_LARGE");
    }
    internal static JsonElement At(this JsonElement e, string property) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) ? v : default;
    internal static string Str(this JsonElement e, int max = 2000)
    {
        var value = e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ValueKind == JsonValueKind.Number ? e.GetRawText() : throw new InvalidDataException("INVALID_TEXT_FIELD");
        if (value.Length > max || value.Contains('\0')) throw new InvalidDataException("SOURCE_FIELD_TOO_LARGE"); return value;
    }
    internal static long Int(this JsonElement e, long max = 1_000_000_000_000) => e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? 0
        : e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) && n >= 0 && n <= max ? n : throw new InvalidDataException("INVALID_TIME_OR_COUNT");
    internal static IEnumerable<JsonElement> Items(this JsonElement e) => e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? []
        : e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToArray() : throw new InvalidDataException("INVALID_ARRAY_FIELD");
}
