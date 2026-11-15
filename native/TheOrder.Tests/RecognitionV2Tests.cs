using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Recognition;
using TheOrder.Platform.Windows;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

// Recognition v2 (recognition-v2-release-aware-1): evidence robustness, release-aware edition
// identity, uniqueness and homonym protection. Each case is a failure mode or a dangerous false
// positive observed in replay evaluation; titles are synthetic.
public sealed class RecognitionV2Tests
{
    private static WorkMetadata W(string id, string title, string[]? aliases = null, string[]? refs = null) =>
        Work(id, title) with { TitleEvidence = [new(title, "official", false, "ja")], Aliases = [.. aliases ?? []],
            References = [.. (refs ?? []).Select(r => new WorkReference(r.Split(':')[0], r.Split(':')[1], null))] };
    private static ReleaseIdentity R(string work, string title, string kind = "default") => new("r" + work + title.Length, work, title, title, kind, null);
    private static RecognitionEvidence E(params (string Source, string Value)[] titles) => Evidence([.. titles.Select(t => new LocalTitleEvidence(t.Value, t.Source))]);
    private static Dictionary<string, ImmutableArray<ReleaseIdentity>> Releases(params ReleaseIdentity[] rows) =>
        rows.GroupBy(r => r.WorkId).ToDictionary(g => g.Key, g => g.ToImmutableArray());
    private static ImmutableArray<RankedRecognitionCandidate> Rank(RecognitionEvidence evidence, WorkMetadata[] works, Dictionary<string, ImmutableArray<ReleaseIdentity>>? releases = null)
        => new ReleaseAwareRanking().Rank(evidence, works, releases);

    // --- Executable evidence

    [Fact]
    public void ByteCountedVersionStringsAreReadWithinTheirBlock()
    {
        using var stream = new MemoryStream(RecognitionRemediationTests.Pe(true, ("FileDescription", "星空の航路 Remake for FHD"), ("ProductName", "TVP(KIRIKIRI) Z core")));
        var parsed = new ExecutableEvidenceParser().Read(stream, "hoshi.exe", default);
        Assert.Contains(parsed.Evidence.Titles, t => t.Source == "file-description" && t.Value == "星空の航路 Remake for FHD");
    }

    [Fact]
    public async Task MalformedVersionResourceOnlyDropsThatExecutablesEvidence()
    {
        using var files = new TransactionProfile();
        var folder = Path.Combine(files.Root, "星空の航路"); Directory.CreateDirectory(folder);
        var bytes = RecognitionRemediationTests.Pe(("ProductName", "星空の航路"));
        // wValueLength that fits neither as WORDs nor as bytes: the oracle's "must never be trusted".
        int at = Array.IndexOf(bytes, (byte)'P', 0x500); BitConverter.TryWriteBytes(bytes.AsSpan(at - 4), (ushort)0xffff);
        File.WriteAllBytes(Path.Combine(folder, "hoshi.exe"), bytes);
        var reader = new WindowsRecognitionEvidence(new LocalEvidenceParser(), RecognitionFingerprint.Evidence, new ExecutableEvidenceParser());
        var entry = Entry("星空の航路", folder) with { Installations = [new("i", folder, Path.Combine(folder, "hoshi.exe"), [])], PreferredInstallationId = "i" };
        var local = await reader.ReadAsync(entry, true, default);
        Assert.Contains(local.Evidence.Titles, t => t.Source == "folder" && t.Value == "星空の航路");
        Assert.DoesNotContain(local.Evidence.Titles, t => t.Source == "product-name");
        Assert.NotEmpty(local.Evidence.Warnings);
    }

    // --- Local title cleaning

    [Theory]
    [InlineData("星空の航路 ダウンロード版", "星空の航路")]
    [InlineData("星空の航路 パッケージ版", "星空の航路")]
    [InlineData("Hoshi no Kouro - Download Edition", "Hoshi no Kouro")]
    [InlineData("Hoshi no Kouro US Edition", "Hoshi no Kouro")]
    [InlineData("星空的航路（含全CG存档）", "星空的航路")]
    [InlineData("星空的航路_简体中文版（含全CG存档）", "星空的航路")]
    [InlineData("SummerHoshi（夏日汉化组）", "SummerHoshi")]
    [InlineData("近月少女的礼仪_v1.21", "近月少女的礼仪")]
    [InlineData("星空の航路 Remake for FHD", "星空の航路 Remake for FHD")]
    [InlineData("星空の航路 FD", "星空の航路 FD")]
    [InlineData("星空の航路 SANA EDITION", "星空の航路 SANA EDITION")]
    [InlineData("10th Anniversary Edition", "10th Anniversary Edition")]
    [InlineData("通常版", "通常版")]
    public void ReleaseFormatNoiseIsRemovedButEditionsStay(string input, string expected) => Assert.Equal(expected, LocalTitleCleaner.Clean(input));

    [Theory]
    [InlineData("LustHoshiSeason264", "executable-name", "LustHoshiSeason2")]
    [InlineData("hoshinokouro64", "executable-name", "hoshinokouro")]
    [InlineData("hoshinokouro_x64", "executable-name", "hoshinokouro")]
    [InlineData("Season264", "folder", "Season264")]
    public void BitnessIsOnlyStrippedFromFileNames(string input, string source, string expected) => Assert.Equal(expected, LocalTitleCleaner.Clean(input, source));

    // --- Edition identity

    [Fact]
    public void OfficialEditionTitleBeatsAnExactAliasOnTheParent()
    {
        var ranked = Rank(E(("folder", "星空の航路 REFLECTION BLUE")),
            [W("200", "星空の航路", aliases: ["星空の航路 REFLECTION BLUE"]), W("201", "星空の航路 REFLECTION BLUE")]);
        Assert.Equal("201", ranked[0].Work.Id);
        Assert.True(ranked[0].HighConfidence);
        Assert.True(ranked.Single(c => c.Work.Id == "200").VersionNeedsConfirmation);
    }

    [Fact]
    public void MachineOnlyTitleCannotBecomeAnAutomaticAliasProof()
    {
        var work = W("200", "星空的航路之夜") with { TitleEvidence = [new("星空的航路之夜", "official", true, "zh-Hans")] };
        var ranked = Rank(E(("folder", "星空的航路之夜")), [work]);
        Assert.False(ranked[0].HighConfidence);
    }

    [Fact]
    public void AnIndexReferenceAloneCannotResolveTwoUnprovenSameTitleIdentities()
    {
        var evidence = E(("folder", "星空の航路")) with { References = [new("vndb:v1", "title-index")] };
        var ranked = Rank(evidence, [W("200", "星空の航路", refs: ["vndb:v1"]), W("201", "星空の航路", refs: ["vndb:v2"])]);
        Assert.False(ranked[0].HighConfidence);
        Assert.False(ranked.Single(c => c.Work.Id == "201").Vetoed);
    }

    [Fact]
    public void EditionNamedByAReleaseOfTheWorkIsResolved()
    {
        var ranked = Rank(E(("folder", "星空の航路 REMASTERED")), [W("200", "星空の航路")], Releases(R("200", "星空の航路 REMASTERED")));
        Assert.False(ranked[0].Vetoed);
    }

    [Fact]
    public void FanDiscWithoutAMatchingReleaseStillNeedsConfirmation()
    {
        var ranked = Rank(E(("folder", "星空の航路 FD")), [W("200", "星空の航路")], Releases(R("200", "星空の航路 初回限定版")));
        Assert.True(ranked[0].Vetoed); Assert.True(ranked[0].VersionNeedsConfirmation); Assert.False(AutomaticRecognitionPolicy.Eligible(ranked[0]));
    }

    [Fact]
    public void SeparatelyCatalogedEditionWinsOverTheBaseWork()
    {
        var ranked = Rank(E(("folder", "星空の航路 SANA EDITION")), [W("200", "星空の航路"), W("201", "星空の航路 SANA EDITION")]);
        Assert.Equal("201", ranked[0].Work.Id); Assert.True(ranked[0].HighConfidence);
        var base_ = ranked.Single(c => c.Work.Id == "200"); Assert.True(base_.VersionNeedsConfirmation);
    }

    [Fact]
    public void PlainLocalTitleIsNotProofOfARemake()
    {
        var plain = Rank(E(("folder", "星空の航路")), [W("200", "星空の航路 Remake")]);
        Assert.True(plain[0].VersionNeedsConfirmation);
        var carried = Rank(E(("folder", "星空の航路"), ("manual-title", "星空の航路 Remake")), [W("200", "星空の航路 Remake")]);
        Assert.False(carried[0].Vetoed);
    }

    [Fact]
    public void ExecutableBuildVersionAndPackagingAreNotEditions()
    {
        var ranked = Rank(E(("folder", "星空の航路"), ("executable-name", "星空の航路 -R18 Edition-"), ("executable-name", "hoshi_v1.21")), [W("200", "星空の航路")]);
        Assert.False(ranked[0].Vetoed); Assert.True(AutomaticRecognitionPolicy.Eligible(ranked[0])); Assert.True(ranked[0].HighConfidence);
    }

    [Fact]
    public void ChapterConflictRemainsHard()
    {
        var ranked = Rank(E(("folder", "航路 第2章")), [W("200", "航路 第1章")]);
        Assert.True(ranked[0].Vetoed); Assert.False(ranked[0].VersionNeedsConfirmation);
    }

    // --- Uniqueness and homonyms

    [Fact]
    public void TwoWorksWithTheSameExactTitleAreATie()
    {
        var ranked = Rank(E(("folder", "Hoshi no Kouro")), [W("200", "Hoshi no Kouro"), W("201", "Hoshi no Kouro")]);
        Assert.True(AutomaticRecognitionPolicy.Eligible(ranked[0])); Assert.False(ranked[0].HighConfidence);
    }

    [Fact]
    public void IndexHomonymBlocksATitleOnlyMatchButNotAPhysicalIdentifier()
    {
        var evidence = E(("folder", "Hoshi no Kouro")) with { Homonyms = [new("Hoshi no Kouro", "folder", ["vndb:v1", "vndb:v2"])] };
        var work = W("200", "Hoshi no Kouro", refs: ["vndb:v1", "steam:4321"]);
        Assert.False(Rank(evidence, [work])[0].HighConfidence);
        var physical = evidence with { References = [new("steam:4321", "steam-config")] };
        Assert.True(Rank(physical, [work])[0].HighConfidence);
    }

    [Fact]
    public void MoreSpecificLocalTitleBreaksATruncationTie()
    {
        // The folder is a truncation; the program's full title names only one work.
        var specific = Rank(E(("folder", "Hoshi"), ("file-description", "Hoshi ~Kouro no Fugue~")), [W("200", "Hoshi ~Kouro no Fugue~"), W("201", "Hoshi")]);
        Assert.Equal("200", specific[0].Work.Id); Assert.True(specific[0].HighConfidence);
        var swapped = Rank(E(("folder", "Hoshi"), ("file-description", "Hoshi ~Kouro no Fugue~")), [W("201", "Hoshi ~Kouro no Fugue~"), W("200", "Hoshi")]);
        Assert.Equal("201", swapped[0].Work.Id); Assert.True(swapped[0].HighConfidence);
        // Two unrelated exact titles do not dominate each other.
        var unrelated = Rank(E(("folder", "Hoshi"), ("file-description", "Tsuki no Kouro")), [W("200", "Tsuki no Kouro"), W("201", "Hoshi")]);
        Assert.False(unrelated[0].HighConfidence);
    }

    // Live NextMoe validation: some catalogs hold an edition as its own work and keep unmerged
    // duplicate records of one work.

    [Fact]
    public void SeparatelyCatalogedEditionBeatsAReleaseOfTheParent()
    {
        var ranked = Rank(E(("folder", "星空の航路 REMASTERED")), [W("200", "星空の航路"), W("201", "星空の航路 REMASTERED")],
            Releases(R("200", "星空の航路 REMASTERED")));
        Assert.Equal("201", ranked[0].Work.Id); Assert.True(ranked[0].HighConfidence);
        Assert.True(ranked.Single(c => c.Work.Id == "200").VersionNeedsConfirmation);
    }

    [Fact]
    public async Task IndexIdentityFoundThroughABaseTitleDoesNotVetoTheEdition()
    {
        var index = new TitleIndex(() => new MemoryStream(Gzip("""{"version":1,"records":[{"names":["星空の航路"],"references":["vndb:v1","bangumi:10"]}]}""")));
        var built = await RecognitionIdentityBuilder.BuildAsync(Entry(), E(("folder", "星空の航路 SANA EDITION"), ("readme-title", "星空の航路")), index, default);
        Assert.DoesNotContain(built.Evidence.References, r => r.Source == "title-index"); Assert.Empty(built.Evidence.IndexProofs);
        var ranked = Rank(built.Evidence, [W("200", "星空の航路", refs: ["vndb:v1", "bangumi:10"]), W("201", "星空の航路 SANA EDITION", refs: ["bangumi:11"])]);
        Assert.Equal("201", ranked[0].Work.Id); Assert.True(ranked[0].HighConfidence);
    }

    [Fact]
    public void IndexIdentifierDoesNotProveAnUnidentifiedRecordIsADuplicate()
    {
        var evidence = E(("folder", "星空の航路")) with { References = [new("vndb:v1", "title-index")] };
        var identified = W("200", "星空の航路", refs: ["vndb:v1"]); var duplicate = W("201", "星空の航路", refs: ["bangumi:20"]);
        var ranked = Rank(evidence, [duplicate, identified]);
        Assert.False(ranked[0].HighConfidence);
        Assert.False(Rank(E(("folder", "星空の航路")), [duplicate, W("202", "星空の航路")])[0].HighConfidence);
        var homonym = evidence with { Homonyms = [new("星空の航路", "folder", ["vndb:v1", "vndb:v2"])] };
        Assert.False(Rank(homonym, [duplicate, identified])[0].HighConfidence);
    }

    [Fact]
    public void SameEditionAndSharedCatalogIdentityCanConfirmADuplicate()
    {
        var evidence = E(("folder", "星空の航路")) with {
            References = [new("vndb:v1", "title-index")],
            IndexProofs = [new("星空の航路", "folder", ["星空の航路"], ["vndb:v1"])] };
        var identified = W("200", "星空の航路", refs: ["vndb:v1", "bangumi:20"]);
        var duplicate = W("201", "星空の航路", refs: ["bangumi:20"]);
        Assert.True(Rank(evidence, [duplicate, identified])[0].HighConfidence);
        Assert.False(Rank(evidence, [duplicate with { References = [new("vndb", "v2", null), new("bangumi", "20", null)] }, identified])[0].HighConfidence);
    }

    [Fact]
    public void ShortSingleWordAndInferredIndexCannotProveIdentityOfAnAbsentHomonym()
    {
        var evidence = E(("folder", "Dahlia"), ("executable-name", "dahlia_chs")) with {
            References = [new("vndb:v2", "title-index")],
            IndexProofs = [new("Dahlia", "folder", ["Dahlia"], ["vndb:v2"])] };
        var work = W("200", "Dahlia", refs: ["vndb:v2", "steam:123456"]);
        var releases = Releases(R("200", "Dahlia"));
        Assert.False(Rank(evidence, [work], releases)[0].HighConfidence);
        Assert.True(Rank(evidence with { References = evidence.References.Add(new("steam:123456", "steam-config")) }, [work], releases)[0].HighConfidence);
        Assert.True(Rank(E(("folder", "Longer Distinctive Title")), [W("201", "Longer Distinctive Title")])[0].HighConfidence);
    }

    [Fact]
    public void ExactReleaseTitleNamesTheWork()
    {
        var ranked = Rank(E(("folder", "星空の航路 ～もうひとつの空～")), [W("200", "星空の航路")], Releases(R("200", "星空の航路 ～もうひとつの空～")));
        Assert.True(ranked[0].Confidence > .9); Assert.True(ranked[0].HighConfidence);
    }

    [Fact]
    public void LongTranslatedAliasIsEligibleButShortAliasIsNot()
    {
        Assert.True(Rank(E(("folder", "星空的航路之夜")), [W("200", "星空の航路", aliases: ["星空的航路之夜"])])[0].HighConfidence);
        Assert.False(AutomaticRecognitionPolicy.Eligible(Rank(E(("folder", "HNK")), [W("200", "星空の航路", aliases: ["HNK"])])[0]));
    }

    // --- Identity building and persistence

    [Fact]
    public async Task IndexReferencesAreOneIdentityNotConflictingLocalIds()
    {
        var index = new TitleIndex(() => new MemoryStream(Gzip("""{"version":1,"records":[{"names":["星空の航路"],"references":["vndb:v1","steam:11","steam:12"]}]}""")));
        var built = await RecognitionIdentityBuilder.BuildAsync(Entry(), E(("folder", "星空の航路")), index, default);
        Assert.Single(built.Evidence.References, r => r.Value.StartsWith("steam:", StringComparison.Ordinal));
        var local = await RecognitionIdentityBuilder.BuildAsync(Entry(), E(("folder", "星空の航路")) with { References = [new("steam:99", "steam-config")] }, index, default);
        Assert.Equal(["steam:99"], local.Evidence.References.Where(r => r.Value.StartsWith("steam:", StringComparison.Ordinal)).Select(r => r.Value));
    }

    [Fact]
    public async Task SharedIndexTitleIsRecordedAsHomonymAndCleanedTitlesAreLookedUp()
    {
        var index = new TitleIndex(() => new MemoryStream(Gzip("""{"version":1,"records":[{"names":["星空の航路"],"references":["vndb:v1"]},{"names":["星空の航路"],"references":["vndb:v2"]}]}""")));
        var built = await RecognitionIdentityBuilder.BuildAsync(Entry(), E(("folder", "星空の航路 ダウンロード版")), index, default);
        Assert.Contains(built.Evidence.Titles, t => t.Source == "folder" && t.Value == "星空の航路");
        var homonym = Assert.Single(built.Evidence.Homonyms); Assert.Equal(new[] { "vndb:v1", "vndb:v2" }, homonym.Identities.ToArray());
        Assert.Empty(built.Evidence.IndexProofs);
    }

    [Fact]
    public void EvidenceWrittenBeforeV2DeserializesWithoutHomonyms()
    {
        var json = JsonSerializer.Serialize(E(("folder", "星空の航路")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json); Assert.True(document.RootElement.TryGetProperty("homonyms", out _));
        var old = json.Replace(",\"homonyms\":[]", "", StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<RecognitionEvidence>(old, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(restored.Homonyms.IsDefault); Assert.Empty(restored.Homonyms);
    }

    [Fact]
    public void RankingAndUniquenessDoNotDependOnCatalogOrder()
    {
        var evidence = E(("folder", "星空の航路 SANA EDITION"), ("executable-name", "hoshi64"));
        var works = new[] { W("20", "星空の航路"), W("10", "星空の航路 SANA EDITION"), W("30", "星空の航路 第2章"), W("40", "全然別の作品") };
        var releases = Releases(R("20", "星空の航路 ダウンロード版"));
        var expected = Rank(evidence, works, releases);
        foreach (var order in new[] { works.Reverse().ToArray(), [.. works.Skip(2), .. works.Take(2)] })
        {
            var actual = Rank(evidence, order, releases);
            Assert.Equal(expected.Select(c => (c.Work.Id, c.Confidence, c.HighConfidence, c.Vetoed)), actual.Select(c => (c.Work.Id, c.Confidence, c.HighConfidence, c.Vetoed)));
        }
        Assert.All(expected.Where(c => c.Vetoed), c => Assert.False(c.HighConfidence));
        Assert.Single(expected, c => c.HighConfidence);
    }

    // --- Configured pipeline: queue -> RecognitionEngine -> ReleaseAwareRanking -> policy ->
    // RecognitionReviewService contextual re-check -> transactional library commit.

    private static async Task<(RecognitionReviewTests.Lab Lab, Guid Id)> Imported(string name, WorkMetadata[] works, params ReleaseIdentity[] releases)
    {
        var lab = new RecognitionReviewTests.Lab(entry: Entry(name, @"C:\synthetic\" + name));
        lab.Catalog.Works = works; lab.Catalog.ReleaseRows = [.. releases];
        await lab.Init();
        var id = await lab.Queue.EnqueueAsync("a", RecognitionMode.MissingMetadata, true);
        await lab.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5)); await lab.Reviews.FlushAsync();
        return (lab, id);
    }

    private sealed class FixedReader(RecognitionEvidence evidence) : IRecognitionEvidenceReader
    {
        public Task<LocalEvidenceSnapshot> ReadAsync(LibraryEntry entry, bool includePe, CancellationToken cancellation) =>
            Task.FromResult(new LocalEvidenceSnapshot(evidence, [], [], new string('a', 64)) { IncludePe = includePe });
    }

    [Fact]
    public async Task CommitRecheckMustKeepACompetitorsPreviouslyFetchedReleaseEvidence()
    {
        var index = new TitleIndex(() => new MemoryStream(Gzip("""{"version":1,"records":[{"names":["星空の航路","星空の航路 REMASTERED"],"references":["vndb:v1"]}]}""")));
        var reader = new FixedReader(E(("folder", "星空の航路"), ("manual-title", "星空の航路 REMASTERED")));
        await using var lab = new RecognitionReviewTests.Lab(entry: Entry("星空の航路", @"C:\synthetic\hoshi"), titleIndex: index, reader: reader);
        lab.Catalog.Works = [W("200", "星空の航路", aliases: ["星空の航路 REMASTERED"], refs: ["vndb:v1"]), W("201", "遠い別世界")];
        lab.Catalog.ReleaseRows = [R("200", "星空の航路"), R("201", "星空の航路 REMASTERED")];
        await lab.Init();
        var id = await lab.Queue.EnqueueAsync("a", RecognitionMode.MissingMetadata, true);
        await lab.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("200", AutomaticRecognitionPolicy.Select(lab.Queue.RequireCurrentResult(id))?.Work.Id);
        // The selected work's refreshed release no longer supports its stronger original proof.
        // The competitor's already-fetched release still makes the result ambiguous.
        lab.Catalog.ReleaseRows = [R("201", "星空の航路 REMASTERED")];
        Assert.False((await lab.Reviews.ApplyAutomaticAsync(id, default)).IsCommitted);
        Assert.Null(Assert.Single(lab.Library.Snapshot.Entries).Metadata);
    }

    [Fact]
    public async Task ReleaseResolvedEditionIsAppliedAndPersonalFieldsSurvive()
    {
        var (lab, id) = await Imported("星空の航路 REMASTERED", [W("200", "星空の航路"), W("300", "全然別の作品")], R("200", "星空の航路 REMASTERED"));
        await using var _ = lab;
        await lab.Library.UpdateAsync("a", new() { Notes = "personal", Rating = 8 });
        Assert.True((await lab.Reviews.ApplyAutomaticAsync(id, default)).IsCommitted);
        var entry = Assert.Single(lab.Library.Snapshot.Entries);
        Assert.Equal("200", entry.Metadata?.Id); Assert.Equal("personal", entry.Notes); Assert.Equal(8, entry.PersonalRating);
    }

    [Fact]
    public async Task SameTitleWorksStayForReviewWithoutWriting()
    {
        var (lab, id) = await Imported("Hoshi no Kouro", [W("200", "Hoshi no Kouro"), W("201", "Hoshi no Kouro")]);
        await using var _ = lab;
        Assert.Null(AutomaticRecognitionPolicy.Select(lab.Queue.RequireCurrentResult(id)));
        Assert.False((await lab.Reviews.ApplyAutomaticAsync(id, default)).IsCommitted);
        Assert.Null(Assert.Single(lab.Library.Snapshot.Entries).Metadata); Assert.Single(lab.Reviews.Snapshot);
    }

    [Fact]
    public async Task UnconfirmedFanDiscStaysForReviewWithoutWriting()
    {
        var (lab, id) = await Imported("星空の航路 FD", [W("200", "星空の航路")], R("200", "星空の航路 ダウンロード版"));
        await using var _ = lab;
        var top = lab.Queue.RequireCurrentResult(id).Candidates[0];
        Assert.True(top.VersionNeedsConfirmation);
        Assert.False((await lab.Reviews.ApplyAutomaticAsync(id, default)).IsCommitted);
        Assert.Null(Assert.Single(lab.Library.Snapshot.Entries).Metadata);
    }

    [Fact]
    public void AlgorithmVersionChangedForV2() => Assert.Equal("recognition-v2-release-aware-8", TitleMatching.AlgorithmVersion);
}
