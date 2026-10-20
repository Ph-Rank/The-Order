using System.Collections.Immutable;
using TheOrder.Domain;
using TheOrder.Presentation;
using Xunit;
using static TheOrder.Tests.RecognitionBoundaryTests;

namespace TheOrder.Tests;

public sealed class CandidateDetailLinkTests
{
    [Fact]
    public void LinksUseActualWorkUrlsAndDeduplicateSources()
    {
        var work = Work("123") with { WebUrl = "https://www.kungal.com/galgame/123", References = [new("vndb", "v1", "https://vndb.org/v1"),
            new("bangumi", "123", "https://bgm.tv/subject/123"), new("vndb", "v1", "https://vndb.org/v1")] };
        var links = CandidateDetailLinks.From(work);
        Assert.Equal(3, links.Count); Assert.Equal("KunGal", links[0].Title); Assert.Equal(work.WebUrl, links[0].Url);
        Assert.Contains(links, l => l.Title == "VNDB"); Assert.Contains(links, l => l.Title == "Bangumi");
    }
    [Theory]
    [InlineData("http://vndb.org/v1")][InlineData("file:///C:/private")][InlineData("javascript:alert(1)")]
    [InlineData("https://secret@vndb.org/v1")][InlineData("https://vndb.org:444/v1")]
    [InlineData("https://vndb.org.attacker.invalid/v1")][InlineData("https://127.0.0.1/")]
    public void UnsafeOrUnrelatedTargetsAreNotLaunchable(string value) => Assert.False(CandidateDetailLinks.Allowed(value));
    [Fact]
    public void MissingForeignLinksKeepOnlyCatalogKunGalRoute() { var link = Assert.Single(CandidateDetailLinks.From(Work())); Assert.Equal("KunGal", link.Title); Assert.Equal("https://www.kungal.com/galgame/" + Work().Id, link.Url); }
    [Theory]
    [InlineData("RJ297925")][InlineData("VJ01001079")][InlineData("BJ123456")]
    public void DlsiteCatalogProductIdsAppearAsAnOrdinarySource(string id)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { @object="work", id="123", display_name="Synthetic", refs=new[]{new{source="dlsite",external_id=id}} }));
        var work = new TheOrder.Infrastructure.Online.RemoteWorkDto(doc.RootElement).Map(false, DateTimeOffset.UtcNow);
        var link = Assert.Single(CandidateDetailLinks.From(work), x=>x.Title=="DLsite");
        Assert.Equal("https://www.dlsite.com/home/work/=/product_id/"+id+".html",link.Url);
        Assert.Equal(id,Assert.Single(work.References).Id);
    }
    [Theory]
    [InlineData("../RJ297925")][InlineData("RJ297925?affiliate=x")][InlineData("123456")][InlineData("rj297925")]
    public void InvalidDlsiteIdsNeverGeneratePurchaseLinks(string id)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new { @object="work", id="123", display_name="Synthetic", refs=new[]{new{source="dlsite",external_id=id}} }));
        var work = new TheOrder.Infrastructure.Online.RemoteWorkDto(doc.RootElement).Map(false, DateTimeOffset.UtcNow);
        Assert.DoesNotContain(CandidateDetailLinks.From(work),x=>x.Title=="DLsite");
    }
    [Theory]
    [InlineData("https://www.dlsite.com/")][InlineData("https://www.dlsite.com/home/fsr/=/keyword/test")]
    public void DlsiteHomeAndSearchPagesAreNotProductSources(string url) => Assert.DoesNotContain(CandidateDetailLinks.From(Work() with { References=[new("dlsite","x",url)] }),x=>x.Title=="DLsite");
}
