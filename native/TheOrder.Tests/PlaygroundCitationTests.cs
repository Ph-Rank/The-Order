using TheOrder.Application.Playground;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundCitationTests
{
    private static PlaygroundSource Source(string id) => new(id, PlaygroundSourceKind.LocalRecord, "来源 " + id);

    [Fact]
    public void NumbersFollowRenderedAppearanceNotContextOrder()
    {
        var citations = new PlaygroundCitations(Enumerable.Range(1, 11).Select(i => Source("s" + i)));
        var parts = citations.Literal("第一项[[s2]]，第二项[[s6]]，第三项[[s4]]");
        Assert.Equal(new int?[] { 1, 2, 3 }, parts.Where(p => p.Source is not null).Select(p => p.Number));
        Assert.Equal(new[] { "s2", "s6", "s4" }, citations.Entries.Where(e => e.Number is not null).Select(e => e.Source.Id));
        Assert.Null(citations.Entries.Single(e => e.Source.Id == "s1").Number);
    }

    [Theory]
    [InlineData(", ")]
    [InlineData("，")]
    [InlineData("、 ")]
    [InlineData("; ")]
    [InlineData("")]
    public void AdjacentReferencesFormOneCompactNonbreakingGroup(string separator)
    {
        var citations = new PlaygroundCitations([Source("b"), Source("a")]);
        var parts = citations.Literal("结论。 [[a]]" + separator + "[[b]]");
        Assert.Equal("结论。\u2060¹\u2060\u2009\u2060\u2060²", string.Concat(parts.Select(p => p.Text)));
        Assert.Equal(new[] { "a", "b" }, parts.Where(p => p.Source is not null).Select(p => p.Source!.Id));
    }

    [Theory]
    [InlineData("，下一句")]
    [InlineData("\n")]
    [InlineData("；而且")]
    public void ProsePunctuationAndExplicitLineBreaksArePreserved(string text)
    {
        var parts = new PlaygroundCitations([Source("a"), Source("b")]).Literal("[[a]]" + text + "[[b]]");
        Assert.Equal(text, parts[1].Text);
    }

    [Fact]
    public void RepeatedSourcesKeepTheirNumberAcrossStreamingBlocksAndPanelReads()
    {
        var citations = new PlaygroundCitations([Source("a"), Source("b"), Source("c")]);
        Assert.Equal(1, citations.Literal("[[b]]").Single().Number);
        _ = citations.Entries.ToArray(); // Opening sources must not number unused context.
        Assert.Equal(new int?[] { 1, 2, 3 }, citations.Literal("[[c]], [[b]], [[a]]").Where(p => p.Source is not null).Select(p => p.Number));
        Assert.Equal(new[] { "b", "c", "a" }, citations.Entries.Select(e => e.Source.Id));
    }

    [Fact]
    public void ReusedReferenceGroupsSortAndDeduplicateWithoutChangingSourceIdentity()
    {
        var citations = new PlaygroundCitations([Source("a"), Source("b"), Source("c"), Source("d")]);
        citations.Literal("[[a]]第一句[[b]]第二句[[c]]第三句[[d]]");
        var group = citations.Literal("结论[[d]]，[[b]], [[a]], [[d]]");
        Assert.Equal(new int?[] { 1, 2, 4 }, group.Where(p => p.Source is not null).Select(p => p.Number));
        Assert.Equal(new[] { "a", "b", "d" }, group.Where(p => p.Source is not null).Select(p => p.Source!.Id));
        Assert.Equal("结论\u2060¹\u2060\u2009\u2060\u2060²\u2060\u2009\u2060\u2060⁴", string.Concat(group.Select(p => p.Text)));
    }

    [Fact]
    public void DeduplicationDoesNotRemoveAReferenceFromAnotherSentence()
    {
        var citations = new PlaygroundCitations([Source("a"), Source("b")]);
        var parts = citations.Literal("第一句[[a]], [[b]]；第二句[[a]]。");
        Assert.Equal(new int?[] { 1, 2, 1 }, parts.Where(p => p.Source is not null).Select(p => p.Number));
        Assert.Contains(parts, p => p.Text == "；第二句");
    }

    [Fact]
    public void UnknownAndDuplicateSourcesDoNotConsumeNumbersOrMakeUnverifiedLinks()
    {
        var citations = new PlaygroundCitations([Source("a"), Source("a"), Source("b")]);
        var parts = citations.Literal("[[missing]], [[b]], [[a]]");
        Assert.Equal("[未核验引用]", parts[0].Text);
        Assert.Null(parts[0].Source);
        Assert.Equal(",", parts[1].Text.Trim());
        Assert.Equal(new int?[] { 1, 2 }, parts.Where(p => p.Source is not null).Select(p => p.Number));
        Assert.Equal(2, citations.Entries.Count());
    }

    [Fact]
    public void MultiDigitNumbersRemainCompleteAndSourceLimitStillApplies()
    {
        var citations = new PlaygroundCitations(Enumerable.Range(1, 65).Select(i => Source("s" + i)));
        var parts = citations.Literal(string.Join(", ", Enumerable.Range(1, 65).Select(i => "[[s" + i + "]]")));
        var verified = parts.Where(p => p.Source is not null).ToArray();
        Assert.Equal(64, verified.Length);
        Assert.Equal("\u2060¹\u2060⁰", verified[9].Text);
        Assert.Equal(64, verified[^1].Number);
        Assert.Equal("[未核验引用]", parts[^1].Text);
    }
}
