using System.Collections.Immutable;
using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundModelSelectionTests
{
    [Fact]
    public void OpenAiSelectsOnlyTheLatestVisibleModelInEachRequestedFamily()
    {
        var models = Catalog("gpt-5-luna", "gpt-6-luna", "gpt-6.1-terra-2026-10-20", "gpt-6.1-terra",
            "gpt-6.9-sol", "gpt-6.10-sol-preview", "gpt-6-astra", "gpt-99-astra-audio", "gpt-99-sol-codex", "gpt-5.5");
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, models);
        Assert.Equal(new[] { "gpt-6-luna", "gpt-6.1-terra", "gpt-6.10-sol-preview", "gpt-6-astra" }, selected.Select(x => x.Id));
        Assert.All(selected, model => Assert.Contains(model, models));
        Assert.All(selected, model => Assert.False(model.Capabilities.Verified));
        Assert.Equal(selected.Select(x => x.Id), PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, models.Reverse()).Select(x => x.Id));
    }

    [Fact]
    public void DatedSnapshotsCollapseToNewestWhenNoOrdinaryAliasIsVisible()
    {
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI,
            Catalog("gpt-6.1-sol-2026-09-29", "gpt-6.1-sol-2026-10-02", "gpt-6.1-sol-2026-10-01"));
        Assert.Equal("gpt-6.1-sol-2026-10-02", Assert.Single(selected).Id);
    }

    [Fact]
    public void GoogleLiteIsSeparateAndReportedNonGenerationModelsDoNotWin()
    {
        var models = Catalog("gemini-3.5-flash-lite", "gemini-3.1-flash-lite-image", "gemini-3.8-flash", "gemini-3.1-pro-preview", "gemini-4-argon")
            .Add(Model("gemini-99-flash") with { GenerationMethodsReported = true, GenerationMethods = ["embedContent"] })
            .Add(Model("gemini-100-flash") with { GenerationMethodsReported = true })
            .Add(Model("gemini-4-flash-preview") with { GenerationMethodsReported = true, GenerationMethods = ["generate_content"], Version = "001" });
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, models);
        Assert.Equal(new[] { "gemini-3.5-flash-lite", "gemini-4-flash-preview", "gemini-3.1-pro-preview", "gemini-4-argon" }, selected.Select(x => x.Id));
    }

    [Fact]
    public void GooglePatchCountersAndPreviewMonthDoNotBecomeSemanticMajorVersions()
    {
        var models = Catalog("gemini-2.5-flash-preview-09-2025", "gemini-2.5-flash-preview-02-2026")
            .Add(Model("gemini-2.5-flash") with { Version = "001" });
        Assert.Equal("gemini-2.5-flash", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, models)).Id);
        Assert.Equal("gemini-2.5-flash-preview-02-2026", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, models.Take(2))).Id);
    }

    [Theory]
    [InlineData("customtools")]
    [InlineData("customtool")]
    [InlineData("custom-tools")]
    [InlineData("custom-tool")]
    [InlineData("computer-use")]
    public void DedicatedVariantsCannotDisplaceTheOrdinaryPreviewInTheSameGeneration(string variant)
    {
        const string ordinary = "gemini-3.1-pro-preview";
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, Catalog(ordinary, ordinary + "-" + variant));
        Assert.Equal(ordinary, Assert.Single(selected).Id);
        Assert.Empty(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, Catalog(ordinary + "-" + variant)));
    }

    [Fact]
    public void DedicatedVariantExclusionStillRetainsOrdinaryLatestAndDatedModels()
    {
        Assert.Equal("gemini-pro-latest", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini,
            Catalog("gemini-pro-latest", "gemini-pro-latest-customtools"))).Id);
        Assert.Equal("claude-sonnet-4-6-20261001", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Claude,
            Catalog("claude-sonnet-4-6-20261001", "claude-sonnet-99-computer-use"))).Id);
    }

    [Fact]
    public void ClaudeSupportsBothHistoricalAndCurrentVersionNamingAndNewFableFamily()
    {
        var models = Catalog("claude-3-5-haiku-20241022", "claude-3-7-sonnet-20250219", "claude-sonnet-4-6", "claude-opus-5-5",
            "claude-opus-5-10", "claude-fable-1-2", "claude-fable-1-1", "claude-opus-99-audio");
        Assert.Equal(new[] { "claude-3-5-haiku-20241022", "claude-sonnet-4-6", "claude-opus-5-10", "claude-fable-1-2" },
            PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Claude, models).Select(x => x.Id));
    }

    [Fact]
    public void ClaudeDatedAliasesDeduplicateWithoutUsingTheDateAsTheVersion()
    {
        var models = Catalog("claude-sonnet-4-5-20250929", "claude-sonnet-4-6-20260801", "claude-sonnet-4-6-20261001");
        Assert.Equal("claude-sonnet-4-6-20261001", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Claude, models)).Id);
    }

    [Fact]
    public void DeepSeekAndGrokKeepTheirDistinctVisibleChatFamiliesAndExcludeSpecializedModels()
    {
        Assert.Equal(new[] { "deepseek-v4.2-flash", "deepseek-v4-pro", "deepseek-chat", "deepseek-reasoner" },
            PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.DeepSeek,
                Catalog("deepseek-v4.1-flash-1017", "deepseek-v4.2-flash", "deepseek-v4-pro", "deepseek-chat", "deepseek-reasoner", "deepseek-coder-v99")).Select(x => x.Id));
        Assert.Equal(new[] { "grok-4.10", "grok-4-1-fast-reasoning", "grok-3-mini" },
            PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Grok,
                Catalog("grok-4.9", "grok-4.10", "grok-4-fast", "grok-4-1-fast-reasoning", "grok-3-mini", "grok-code-fast-99", "grok-99-image", "grok-99-multi-agent-beta")).Select(x => x.Id));
    }

    [Fact]
    public void EmptyOrUnmatchedCatalogNeverInventsPermissionForDefaultModels()
    {
        Assert.Empty(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, []));
        Assert.Empty(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, Catalog("gpt-5.5", "gpt-image-99", "gemini-4-argon")));
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini,
            [Model("gemini-4-argon"), Model("gemini-99-argon") with { Lifecycle = "retired" }]);
        Assert.Equal("gemini-4-argon", Assert.Single(selected).Id);
    }

    [Fact]
    public void ProviderCreationMetadataBreaksOtherwiseEquivalentRevisionTies()
    {
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Grok,
            [Model("grok-4.7-reasoning") with { CreatedAt = DateTimeOffset.Parse("2026-10-20T00:00:00Z") },
                Model("grok-4.7-non-reasoning") with { CreatedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z") }]);
        Assert.Equal("grok-4.7-reasoning", Assert.Single(selected).Id);
    }

    private static PlaygroundModel Model(string id) => new(id, id, new());
    private static ImmutableArray<PlaygroundModel> Catalog(params string[] ids) => ids.Select(Model).ToImmutableArray();
}
