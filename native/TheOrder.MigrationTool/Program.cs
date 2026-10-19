using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Migration;
using TheOrder.Platform.Windows;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try { return await Run(args, cancellation.Token); }
catch (OperationCanceledException) { Console.Error.WriteLine("{\"error\":\"CANCELLED\"}"); return 130; }
catch (Exception e)
{
    // Do not echo input values, paths, imported notes, inner exceptions or credentials.
    var code = e is NativeProfileException native ? native.Code : e is StaleLibraryRequestException ? "STALE_PLAN_REPREVIEW_REQUIRED" : e is InvalidDataException ? "INVALID_OR_UNSUPPORTED_SOURCE_OR_PLAN" : "MIGRATION_REFUSED";
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = code, exception = e.GetType().Name })); return 2;
}

static async Task<int> Run(string[] args, CancellationToken cancellation)
{
    if (args.Length == 0 || args[0] is "help" or "--help")
    {
        Console.WriteLine("The Order migration core (Test only)\npreview --source FILE [--target TEST_DIR] [--source-time-zone ZONE] [--out NEW_REPORT_DIR]\ninit-test --target NEW_TEST_DIR\napply --source FILE --target TEST_DIR --commit PLAN_SHA256 [--source-time-zone ZONE] [--out NEW_REPORT_DIR]\ndetect --source FILE\ndemo --out NEW_LAB_DIR\nOptions without a command default to preview. No Production mode; no network or game directory probing."); return 0;
    }
    var command = args[0].StartsWith("--", StringComparison.Ordinal) ? "preview" : args[0];
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = command == args[0] ? 1 : 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--source" or "--target" or "--commit" or "--out" or "--source-time-zone") || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Invalid command options.");
    }
    string Need(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Explicit input required.");
    var codec = new NativeLibraryCodec();
    if (command == "init-test") { if (options.Count != 1) throw new ArgumentException(); MigrationTestTarget.Create(Need("--target"), codec); Console.WriteLine("{\"status\":\"CREATED\",\"profileKind\":\"Test\"}"); return 0; }
    if (command == "demo") { if (options.Count != 1) throw new ArgumentException(); await Demo(Need("--out"), cancellation); return 0; }
    if (command is not ("preview" or "detect" or "apply") || command != "apply" && options.ContainsKey("--commit")) throw new ArgumentException();
    if (options.TryGetValue("--out", out var requestedReport))
    {
        var safeReport = NativeProfilePaths.Validate(new(NativeProfileKind.Test, Path.GetFullPath(requestedReport)));
        if (Directory.Exists(safeReport) || File.Exists(safeReport)) throw new InvalidOperationException("Fresh report output required before any commit.");
    }
    var zone = options.TryGetValue("--source-time-zone", out var zoneId) ? TimeZoneInfo.FindSystemTimeZoneById(zoneId) : null;
    var data = await MigrationTestTarget.ReadSourceAsync(Need("--source"), new PotatoVnImportAdapter(zone), cancellation);
    var before = options.TryGetValue("--target", out var target) ? MigrationTestTarget.Preview(target, codec) : new LibraryDocument(LibraryAccess.ValidWritable, [], null);
    if (before.Access != LibraryAccess.ValidWritable) throw new InvalidDataException("Target is not writable.");
    var plan = target is not null ? MigrationTestTarget.PreviewPlan(target, data, codec, cancellation) : MigrationResolver.Preview(data, before.Entries, codec, cancellation);
    MigrationCommit? receipt = null;
    MigrationVerification? verification = null;
    if (command == "apply")
    {
        receipt = (await MigrationTestTarget.ApplyAsync(Need("--target"), data, Need("--commit"), codec, cancellation)).Value;
        try
        {
            var after = MigrationTestTarget.Preview(Need("--target"), codec); var expected = before.Entries.Length + receipt.Created;
            verification = new(after.Entries.Length == expected ? "PASS" : "COUNT_MISMATCH", before.Entries.Length, after.Entries.Length,
                before.Entries.Sum(e => e.EffectivePlaySeconds), after.Entries.Sum(e => e.EffectivePlaySeconds), true, expected);
        }
        catch (Exception) { verification = new("COMMITTED_REOPEN_NOT_VERIFIED", before.Entries.Length, -1, before.Entries.Sum(e => e.EffectivePlaySeconds), -1, false, before.Entries.Length + receipt.Created); }
    }
    var json = MigrationReport.Json(plan, before.Entries.Length, receipt, verification); Console.WriteLine(json);
    if (options.TryGetValue("--out", out var report))
    {
        try { SaveReport(report, json, MigrationReport.Markdown(plan, before.Entries.Length, receipt, verification)); }
        catch (Exception) when (receipt is not null) { Console.Error.WriteLine("{\"warning\":\"IMPORT_COMMITTED_REPORT_WRITE_FAILED\"}"); }
    }
    return verification is not null && verification.Status != "PASS" ? 3 : 0;
}

static void NewDirectory(string root)
{
    var full = NativeProfilePaths.Validate(new(NativeProfileKind.Test, Path.GetFullPath(root)));
    if (Directory.Exists(full) || File.Exists(full)) throw new InvalidOperationException("Fresh output required.");
    Directory.CreateDirectory(full); NativeProfilePaths.Validate(new(NativeProfileKind.Test, full));
}
static void SaveReport(string directory, string json, string markdown)
{
    NewDirectory(directory);
    foreach (var (name, text) in new[] { ("preview.json", json), ("preview.md", markdown) })
    {
        using var file = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file); writer.Write(text);
    }
}
static async Task Demo(string directory, CancellationToken cancellation)
{
    var root = Path.GetFullPath(directory); NewDirectory(root); var source = Path.Combine(root, "demo.pvnExport.zip");
    var ids = new[] { "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222" };
    var games = new[]
    {
        new { Uuid=ids[0], Name=new {Value="CLANNAD 合成资料",IsLock=true}, OriginalName=new {Value="CLANNAD",IsLock=false},
            Ids=new string?[]{"4","18127"}, Tags=new {Value=new[]{"亲情","日本語・🌸"},IsLock=false},
            TotalPlayTime=122,PlayedTime=new Dictionary<string,int>{{"2026/10/18",60}},PlayType=2,MyRate=8,Comment="合成笔记：引号\"、假名かな。" },
        new { Uuid=ids[1], Name=new {Value="Fate/stay night 合成资料",IsLock=true}, OriginalName=new {Value="Fate/stay night",IsLock=false},
            Ids=new string?[]{"11","321"}, Tags=new {Value=new[]{"合成样本"},IsLock=false},
            TotalPlayTime=120,PlayedTime=new Dictionary<string,int>(),PlayType=1,MyRate=9,Comment="无日期历史，不生成每日统计。" }
    };
    using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
    {
        Entry("data.galgames.json", games);
        Entry("data.dataStatus.json", new { GameLiteDbUpgrade=false,CategoryLiteDbUpgrade=false,SourceLiteDbUpgrade=false });
        Entry("data.galgameSources.json", new[] { new { SourceType=1,Name="合成库",Galgames=new[]
        { new { Galgame=ids[0],EntryId="a",Path=@"Q:\TheOrderSynthetic\CLANNAD-original",LocalConfig=new {ExePath="game.exe"} },
          new { Galgame=ids[0],EntryId="b",Path=@"Q:\TheOrderSynthetic\CLANNAD-second-install",LocalConfig=new {ExePath="game.exe"} } } } });
        Entry("data.categoryGroups.json", new[] { new { Type=2,Name="自定义",Categories=new[] { new { Name="待回顾",GalgamesX=new[]{ids[0]} } } } });
        void Entry(string name, object value) { using var stream=zip.CreateEntry(name, CompressionLevel.Fastest).Open(); JsonSerializer.Serialize(stream,value); }
    }
    var sourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
    var codec=new NativeLibraryCodec(); var target=Path.Combine(root,"test-profile"); MigrationTestTarget.Create(target,codec);
    var before=MigrationTestTarget.Preview(target,codec); var data=await MigrationTestTarget.ReadSourceAsync(source,new PotatoVnImportAdapter(),cancellation);
    var plan=MigrationTestTarget.PreviewPlan(target,data,codec,cancellation);
    SaveReport(Path.Combine(root,"preview"),MigrationReport.Json(plan,0),MigrationReport.Markdown(plan,0));
    var committed=(await MigrationTestTarget.ApplyAsync(target,data,plan.Id,codec,cancellation)).Value;
    var after=MigrationTestTarget.Preview(target,codec); var statistics=PlaytimeStatistics.Build(after.Entries,TimeZoneInfo.Utc);
    if(after.Entries.Length!=2||after.Entries[0].Installations.Length!=2||statistics.TotalSeconds!=14520||statistics.UnknownSeconds!=10920||statistics.Days.Length!=1)
        throw new InvalidDataException("Demo accounting invariant failed.");
    SaveReport(Path.Combine(root,"committed"),MigrationReport.Json(plan,0,committed),MigrationReport.Markdown(plan,0,committed));
    var file=Path.Combine(target,"library.json"); var libraryHash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    var repeat=MigrationTestTarget.PreviewPlan(target,data,codec,cancellation); var repeated=(await MigrationTestTarget.ApplyAsync(target,data,repeat.Id,codec,cancellation)).Value;
    var reopened=MigrationTestTarget.Preview(target,codec);
    if(repeated.Created!=0||repeated.Filled!=0||ImportHash.Snapshot(reopened.Entries,codec)!=ImportHash.Snapshot(after.Entries,codec)
        ||Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))!=libraryHash||Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)))!=sourceHash)
        throw new InvalidDataException("Demo idempotency invariant failed.");
    SaveReport(Path.Combine(root,"repeat"),MigrationReport.Json(repeat,2,repeated),MigrationReport.Markdown(repeat,2,repeated));
    var summary=new {status="PASS",source="PotatoVN known export layout, synthetic values",created=committed.Created,installations=reopened.Entries.Sum(e=>e.Installations.Length),
        totalSeconds=statistics.TotalSeconds,datedSeconds=statistics.Days.Sum(d=>d.Seconds),undatedSeconds=statistics.UnknownSeconds,
        inventedSessions=reopened.Entries.Sum(e=>e.Sessions.Length),inventedLaunches=reopened.Entries.Sum(e=>e.PlayCount),repeatCreated=repeated.Created,
        sourceUnchanged=true,repeatLibraryBytesUnchanged=true,persistenceReopened=true,productionAccessed=false,winUiStarted=false};
    var output=JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}); File.WriteAllText(Path.Combine(root,"demo-summary.json"),output);Console.WriteLine(output);
}
