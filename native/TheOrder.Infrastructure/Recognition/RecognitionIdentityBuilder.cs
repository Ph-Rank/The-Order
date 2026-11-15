using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Infrastructure.Recognition;

public static class RecognitionIdentityBuilder
{
    public static ImmutableArray<string> Names(IEnumerable<string> values) => [.. values.Select(TitleMatching.Prepared)
        .Where(s => TitleMatching.IndexKey(s).Length > 0).DistinctBy(TitleMatching.IndexKey, StringComparer.Ordinal).Take(8)];
    public static ImmutableArray<string> References(IEnumerable<string> values)
    {
        var unique = values.Select(LocalEvidenceParser.NormalizeReference).OfType<string>().Distinct(StringComparer.Ordinal).Take(128).ToArray();
        return [.. new[] { "vndb:", "bangumi:", "steam:" }.SelectMany(source => unique.Where(r => r.StartsWith(source, StringComparison.Ordinal)).Take(2)).Concat(unique).Distinct(StringComparer.Ordinal).Take(8)];
    }
    public static async Task<RecognitionIdentity> BuildAsync(LibraryEntry entry, RecognitionEvidence evidence, ITitleIndex index, CancellationToken cancellation)
    {
        var readme = await index.FindReadmeTitlesAsync([.. evidence.Titles.Where(t => t.Source == "readme-heading").Select(t => t.Value)], cancellation).ConfigureAwait(false);
        // Keep the source of each local observation when expanding title spellings.
        var local = evidence.Titles.Where(t => t.Source != "readme-heading")
            .Select(t => t.Source == "sidecar" ? t : t with { Value = LocalTitleCleaner.Clean(t.Value, t.Source) }).ToArray();
        var namedEditions = local.Where(t => ReleaseAwareRanking.IdentitySource(t.Source) && t.Source is not ("custom-name" or "readme-title"))
            .Select(t => TitleMatching.Markers(t.Value)).Where(m => m.Base.Length >= 2 && !ReleaseAwareRanking.Identity(m).IsEmpty).ToArray();
        // Readme headings are substring matches. A launcher name in the text can mention the
        // base work; a more specific physical title keeps that inferred name from taking over.
        bool LessSpecificReadme(string title)
        {
            var readmeTitle = TitleMatching.Markers(title);
            return namedEditions.Any(m => m.Base == readmeTitle.Base &&
                ReleaseAwareRanking.Identity(m).IsProperSupersetOf(ReleaseAwareRanking.Identity(readmeTitle)));
        }
        evidence = evidence with { Titles = [.. local.Concat(readme.Select(LocalTitleCleaner.Clean).Where(t => !LessSpecificReadme(t))
            .Select(t => new LocalTitleEvidence(t, "readme-title"))).Distinct().Take(40)] };
        var names = Names(evidence.Titles.Where(t => t.Source != "sidecar").Select(t => t.Value));
        // Script variants broaden lookup, not the actual evidence. Exact row proofs below still
        // require the original spelling; inferred references retain their title-index provenance.
        var lookupNames = names.Concat(names.SelectMany(LocalQueryVariants.Chinese)).DistinctBy(TitleMatching.IndexKey, StringComparer.Ordinal).Take(24).ToImmutableArray();
        var matches = await index.FindAsync(lookupNames, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        static string IdentityOf(IndexCandidate m) => References(m.Record.References).FirstOrDefault() ?? "unknown:" + m.Row;
        var identities = matches.Select(IdentityOf).Distinct(StringComparer.Ordinal).ToArray();
        // A saturated lookup is incomplete: an unseen row can carry a conflicting identity.
        var unambiguous = identities.Length == 1 && matches.Length < 128 ? matches : [];
        // An index row found through a less specific local title (a base title, when another local
        // identity title names an edition of it) identifies the base work, not this installation.
        // Its ids must not act as trusted local identifiers against the edition.
        bool Superseded(IndexCandidate m) => evidence.Titles.Any(t => ReleaseAwareRanking.IdentitySource(t.Source)
            && TitleMatching.Markers(t.Value).Base is { Length: >= 2 } b && b == TitleMatching.Markers(m.ObservedTitle).Base
            && !ReleaseAwareRanking.Identity(TitleMatching.Markers(t.Value)).Except(ReleaseAwareRanking.Identity(TitleMatching.Markers(m.ObservedTitle))).IsEmpty
            && !m.Record.Names.Any(n => TitleMatching.Compare(t.Value, n) is { Exact: true }));
        if (unambiguous.Any(Superseded)) unambiguous = [];
        // One index identity is one piece of evidence: at most one reference per source, and none
        // for a source the local files already identify. Several ids of the same work (base game,
        // DLC, regional store pages) must not read as conflicting local identifiers.
        var localSources = evidence.References.Where(r => r.Source != "sidecar").Select(r => r.Value.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        var indexedRefs = References(unambiguous.SelectMany(m => m.Record.References)).GroupBy(r => r.Split(':')[0], StringComparer.Ordinal)
            .Where(g => !localSources.Contains(g.Key)).Select(g => g.First()).ToImmutableArray();
        var proofs = unambiguous.SelectMany(m => evidence.Titles.Where(t => t.Source is not ("sidecar" or "readme-title") && m.Record.Names.Any(n => TitleMatching.IndexKey(n) == TitleMatching.IndexKey(t.Value)))
            .Select(t => new TitleIndexProof(t.Value, t.Source, [.. m.Record.Names.Take(30)], References(m.Record.References)))).Take(20).ToImmutableArray();
        // Exact local titles that the index assigns to more than one identity. A catalog that only
        // returns one of them must not treat the shared title as unique proof.
        var homonyms = evidence.Titles.Where(t => t.Source != "sidecar").Select(t =>
        {
            var spellings = new[] { t.Value }.Concat(LocalQueryVariants.Chinese(t.Value)).Select(TitleMatching.IndexKey).ToHashSet(StringComparer.Ordinal);
            var ids = matches.Where(m => spellings.Contains(TitleMatching.IndexKey(m.ObservedTitle))).Select(IdentityOf)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8).ToImmutableArray();
            return new TitleHomonym(t.Value, t.Source, ids);
        }).Where(h => h.Identities.Length > 1).Take(20).ToImmutableArray();
        var combined = evidence with { References = [.. evidence.References.Concat(indexedRefs.Select(r => new ReferenceEvidence(r, "title-index"))).Take(40)], IndexProofs = proofs,
            Warnings = matches.Length >= 128 ? evidence.Warnings.Add("本地同名候选达到上限，无法判断唯一性，请手动核对。") : evidence.Warnings, Homonyms = homonyms };
        var primary = Names(evidence.Titles.Where(t => t.Source is "manual-title" or "config-title").Select(t => t.Value)
            .Concat(evidence.Titles.Where(t => t.Source is "product-name" or "file-description").Select(t => t.Value)).Append(LocalTitleCleaner.Clean(entry.Name))
            .Concat(evidence.Titles.Where(t => t.Source is "folder" or "readme-title").Select(t => t.Value))
            .Concat(evidence.Titles.Where(t => t.Source is not ("sidecar" or "custom-name")).Select(t => t.Value))).Where(v => TitleMatching.Compare(v, v).Score > 0).ToArray();
        // Spend the bounded query budget on distinct titles first, then their edition-free bases and
        // index names, and only then on spelling variants of the same title.
        var bases = primary.Take(3).Select(TitleMatching.BaseTitle).OfType<string>();
        var queries = primary.Take(3).Concat(bases).Concat(unambiguous.SelectMany(m => m.Record.Names).Take(2))
            .Concat(primary.Take(3).SelectMany(LocalQueryVariants.Retrieval)).Concat(primary.Skip(3))
            .Concat(primary.SelectMany(TitleMatching.Variants)).Concat(unambiguous.SelectMany(m => m.Record.Names))
            .Where(v => TitleMatching.Compare(v, v).Score > 0).DistinctBy(v => TitleMatching.Compact(TitleMatching.Normalize(v)), StringComparer.Ordinal).Take(6).ToImmutableArray();
        // Near-title rows broaden candidate retrieval only after all exact proof/homonym work is
        // finished. They never contribute trusted identifiers, exact bridges or observed titles.
        var similar = matches.Length >= 128 ? [] : await index.FindSimilarAsync(primary.Take(3).ToImmutableArray(), cancellation).ConfigureAwait(false);
        var localCandidates = matches.Concat(similar.Where(s => !matches.Any(m => m.Row == s.Row))).Take(128).ToImmutableArray();
        return new(combined, localCandidates, queries, References(combined.References.Select(r => r.Value)));
    }
}
