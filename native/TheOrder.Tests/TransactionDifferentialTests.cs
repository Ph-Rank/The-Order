using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class TransactionDifferentialTests
{
    internal const string Clock = "2026-10-04T00:00:00.000Z";
    internal static readonly LegacyLibraryCodec Codec = new();
    internal static JsonDocument Oracle() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "transaction-oracle.json")));
    public static IEnumerable<object[]> Cases()
    {
        using var doc = Oracle();
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray();
    }
    public static IEnumerable<object[]> ValidationCases()
    {
        using var doc = Oracle();
        return doc.RootElement.GetProperty("validation").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray();
    }
    [Theory, MemberData(nameof(ValidationCases)), Trait("Category", "Differential")]
    public void WritableCodecRetainsPhaseOneCompatibility(string name)
    {
        using var doc = Oracle(); var c = doc.RootElement.GetProperty("validation").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        ImmutableArray<LibraryEntry> entries = [];
        var error = Record.Exception(() => entries = Codec.Decode(Encoding.UTF8.GetBytes(c.GetProperty("json").GetString()!)));
        Assert.True(c.GetProperty("accepted").GetBoolean() == (error is null), error?.ToString() ?? "Unexpected acceptance");
        if (error is null) EqualEntries(c.GetProperty("games"), entries);
    }
    [Theory, MemberData(nameof(Cases)), Trait("Category", "Differential")]
    public async Task ActualElectronMutationMatchesNativeAndRestart(string name)
    {
        using var doc = Oracle();
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        using var profile = new TransactionProfile(Encoding.UTF8.GetBytes(c.GetProperty("input").GetRawText()));
        var newId = c.GetProperty("operation").GetProperty("kind").GetString() == "add" ? c.GetProperty("result").GetProperty("id").GetString()! : "new-id";
        using (var library = profile.Open(idFactory: () => newId))
        {
            EqualEntries(c.GetProperty("before"), library.Snapshot.Entries);
            object? result = null;
            var error = await Record.ExceptionAsync(async () =>
            {
                var op = c.GetProperty("operation");
                var kind = op.GetProperty("kind").GetString();
                var id = op.TryGetProperty("id", out var prop) ? prop.GetString()! : "";
                switch (kind)
                {
                    case "update": result = (await library.UpdateAsync(id, LegacyLibraryCodec.ReadPersonalPatch(op.GetProperty("patch")))).Value; break;
                    case "delete": await library.DeleteAsync(id); break;
                    case "import": result = (await library.ImportAsync(Encoding.UTF8.GetBytes(op.GetProperty("backup").GetRawText()))).Value; break;
                    case "add": result = (await library.AddAsync(Work(op.GetProperty("work")))).Value; break;
                    case "link":
                        var options = op.GetProperty("options");
                        result = (await library.LinkAsync(id, Work(op.GetProperty("work")), options.TryGetProperty("preserveName", out var keep) && keep.GetBoolean(), options.TryGetProperty("preserveMergedName", out var merged) && merged.GetBoolean())).Value;
                        break;
                    default: throw new InvalidOperationException();
                }
            });
            Assert.True(c.GetProperty("accepted").GetBoolean() == (error is null), error?.ToString() ?? "Native accepted a legacy rejection");
            EqualEntries(CurrentProgressExpectation(c.GetProperty("after"), name), library.Snapshot.Entries);
            if (error is null && result is LibraryEntry entry) EqualEntries(CurrentProgressExpectation(WrapArray(c.GetProperty("result")), name), [entry]);
            if (error is null && result is ImportCount count)
            {
                Assert.Equal(c.GetProperty("result").GetProperty("added").GetInt32(), count.Added);
                Assert.Equal(c.GetProperty("result").GetProperty("skipped").GetInt32(), count.Skipped);
            }
            using var serialized = JsonDocument.Parse(c.GetProperty("serialized").GetString()!);
            EqualDocument(CurrentProgressExpectation(serialized.RootElement, name).GetRawText(), File.ReadAllText(profile.Main));
            if (c.GetProperty("backup").ValueKind == JsonValueKind.Null) Assert.False(File.Exists(profile.Backup));
            else EqualDocument(c.GetProperty("backup").GetString()!, File.ReadAllText(profile.Backup));
        }
        using var reopened = profile.Open(); EqualEntries(CurrentProgressExpectation(c.GetProperty("after"), name), reopened.Snapshot.Entries);
    }
    private static JsonElement CurrentProgressExpectation(JsonElement legacy, string name)
    {
        if (name != "merge-left-wins") return legacy;
        // The user-approved six-state priority supersedes Electron's left-wins status only.
        // Keep the frozen oracle and compare every other field, serialized value and restart.
        var node = JsonNode.Parse(legacy.GetRawText())!;
        var games = node is JsonArray array ? array : node["games"]!.AsArray();
        var merged = Assert.Single(games, x => x?["id"]?.GetValue<string>() == "a")!;
        Assert.Equal("dropped", merged["status"]!.GetValue<string>());
        merged["status"] = "completed";
        return JsonSerializer.SerializeToElement(node);
    }
    [Fact]
    public void OracleIsFreshForAllMutationDependencies()
    {
        using var doc = Oracle();
        Assert.Equal(54, doc.RootElement.GetProperty("cases").GetArrayLength());
        Assert.Equal(43, doc.RootElement.GetProperty("validation").GetArrayLength());
        Assert.Equal(5, doc.RootElement.GetProperty("lifecycle").GetArrayLength());
        Assert.Equal(5, doc.RootElement.GetProperty("recovery").GetArrayLength());
        Assert.Equal(new[] { "electron/library.ts", "electron/local-cover.ts", "electron/title-matching.ts", "native/scripts/generate-transaction-oracle.mjs" }, doc.RootElement.GetProperty("hashes").EnumerateObject().Select(p => p.Name).Order().ToArray());
        using var package = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ReferenceSources.Repository(), "package.json")));
        Assert.Equal(package.RootElement.GetProperty("devDependencies").GetProperty("electron").GetString(), doc.RootElement.GetProperty("electron").GetString());
        foreach (var hash in doc.RootElement.GetProperty("hashes").EnumerateObject())
        {
            var source = File.ReadAllText(Path.Combine(ReferenceSources.Repository(), hash.Name)).Replace("\r\n", "\n");
            Assert.Equal(hash.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        }
    }
    [Theory]
    [InlineData("latest-request")]
    [InlineData("delete-restore")]
    [InlineData("personal-coexists")]
    [InlineData("rename-invalidates")]
    [InlineData("merge-newer-target")]
    [Trait("Category", "Differential")]
    public async Task LifecycleSequenceMatchesRealElectron(string name)
    {
        using var doc = Oracle(); var c = doc.RootElement.GetProperty("lifecycle").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        using var p = new TransactionProfile(Encoding.UTF8.GetBytes(c.GetProperty("input").GetRawText())); using var library = p.Open();
        var tokens = new Dictionary<string, RequestToken>(); var accepted = new List<bool>();
        foreach (var s in c.GetProperty("steps").EnumerateArray())
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                var id = s.TryGetProperty("id", out var v) ? v.GetString()! : "";
                var token = s.TryGetProperty("token", out v) ? v.GetString()! : "";
                switch (s.GetProperty("kind").GetString())
                {
                    case "begin": tokens[token] = await library.BeginRequestAsync(id); break;
                    case "link": await library.CommitLinkAsync(id, Work(s.GetProperty("work")), token: tokens[token]); break;
                    case "delete": await library.DeleteAsync(id); break;
                    case "import": await library.ImportAsync(Encoding.UTF8.GetBytes(s.GetProperty("backup").GetRawText())); break;
                    case "update": await library.UpdateAsync(id, LegacyLibraryCodec.ReadPersonalPatch(s.GetProperty("patch"))); break;
                    case "finish": await library.FinishRequestAsync(tokens[token]); break;
                    default: throw new InvalidOperationException();
                }
            });
            accepted.Add(error is null);
        }
        Assert.Equal(c.GetProperty("accepted").EnumerateArray().Select(v => v.GetBoolean()), accepted);
        EqualEntries(c.GetProperty("after"), library.Snapshot.Entries);
    }
    internal static WorkMetadata Work(JsonElement value)
    {
        using var normal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "normal", "library.json")));
        var template = Codec.Decode(Encoding.UTF8.GetBytes(normal.RootElement.GetRawText()))[0];
        var json = Encoding.UTF8.GetString(Codec.Encode([template with { CoverChoice = null, Metadata = null }], Clock));
        // Replace only the template's null metadata; raw UTF-16 tokens in the work remain intact.
        json = json.Replace("\"metadata\": null", "\"metadata\":" + value.GetRawText(), StringComparison.Ordinal);
        return Codec.Decode(Encoding.UTF8.GetBytes(json))[0].Metadata!;
    }
    private static JsonElement WrapArray(JsonElement entry) => JsonDocument.Parse("[" + entry.GetRawText() + "]").RootElement.Clone();
    internal static void EqualEntries(JsonElement expected, ImmutableArray<LibraryEntry> actual)
    {
        var parsed = LegacyLibraryReader.Parse(Encoding.UTF8.GetBytes("{\"format\":\"kun-gal-manager\",\"version\":1,\"games\":" + expected.GetRawText() + "}"));
        Assert.Equal(Codec.Encode(parsed, Clock), Codec.Encode(actual, Clock));
    }
    // Compare property presence/order-independent objects, ordered arrays, exact UTF-16 strings,
    // and numeric values; JSON whitespace/escape spelling is not an export contract.
    internal static void EqualDocument(string expected, string actual)
    {
        using var left = JsonDocument.Parse(expected); using var right = JsonDocument.Parse(actual);
        Compare(left.RootElement, right.RootElement);
    }
    private static void Compare(JsonElement a, JsonElement b)
    {
        Assert.Equal(a.ValueKind, b.ValueKind);
        if (a.ValueKind == JsonValueKind.Object)
        {
            Assert.Equal(a.EnumerateObject().Select(p => p.Name).Order(), b.EnumerateObject().Select(p => p.Name).Order());
            foreach (var p in a.EnumerateObject()) Compare(p.Value, b.GetProperty(p.Name));
        }
        else if (a.ValueKind == JsonValueKind.Array)
        {
            Assert.Equal(a.GetArrayLength(), b.GetArrayLength());
            for (var i = 0; i < a.GetArrayLength(); i++) Compare(a[i], b[i]);
        }
        else if (a.ValueKind == JsonValueKind.String) Assert.Equal(DecodeString(a), DecodeString(b));
        else if (a.ValueKind == JsonValueKind.Number) Assert.Equal(a.GetDouble(), b.GetDouble());
    }
    private static string DecodeString(JsonElement e)
    {
        // Independently decode JSON escapes as UTF-16 code units (including lone surrogates).
        var raw = e.GetRawText(); var b = new StringBuilder();
        for (int i = 1; i < raw.Length - 1; i++)
        {
            if (raw[i] != '\\') { b.Append(raw[i]); continue; }
            var c = raw[++i];
            if (c == 'u') { b.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16)); i += 4; }
            else b.Append(c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', _ => c });
        }
        return b.ToString();
    }
}

internal sealed class TransactionProfile : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "the-order-transaction-" + Guid.NewGuid().ToString("N"));
    public string Main => Path.Combine(Root, "library.json");
    public string Backup => Path.Combine(Root, "library.json.bak");
    public TransactionProfile(byte[]? document = null)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, IsolatedLibrarySource.MarkerName), IsolatedLibrarySource.MarkerValue);
        if (document is not null) File.WriteAllBytes(Main, document);
    }
    public TransactionalLibrary Open(IPersistenceProbe? probe = null, Func<string>? idFactory = null) => TransactionalLibrary.Open(TransactionDifferentialTests.Codec,
        new IsolatedLibraryPersistence(Root, probe), () => TransactionDifferentialTests.Clock, idFactory);
    public void Dispose()
    {
        if (Path.GetDirectoryName(Root) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()) || !Path.GetFileName(Root).StartsWith("the-order-transaction-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup");
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
