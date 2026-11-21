using System.Collections.Immutable;
using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundModelSelectionTests
{
    [Fact]
    public void OpenAiSelectsTheLatestVisibleModelInEachFamily()
    {
        var models = Catalog("gpt-5-luna", "gpt-6-luna", "gpt-6.1-terra-2026-10-20", "gpt-6.1-terra",
            "gpt-6.9-sol", "gpt-6.10-sol-preview", "gpt-6-astra", "gpt-99-astra-audio", "gpt-99-sol-codex", "gpt-5.5");
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, models);
        Assert.Equal(new[] { "gpt-6-luna", "gpt-6.1-terra", "gpt-6.10-sol-preview", "gpt-6-astra" }, selected.Select(x => x.Id));
        Assert.All(selected, model => Assert.Contains(model, models));
        Assert.All(selected, model => Assert.False(model.Capabilities.Verified));
        Assert.Equal(selected.Select(x => x.Id), PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, models.Reverse()).Select(x => x.Id));
    }

    [Theory]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-6-bel", "gpt-6.2-bel", "gpt-6.2-bel-2026-11-14")]
    [InlineData(PlaygroundProviderKind.OpenAI, "gpt-bel", "gpt-bel-7", "gpt-bel-7-20261114")]
    [InlineData(PlaygroundProviderKind.OpenAI, "bel-1", "bel-2", "bel-2-20261114")]
    [InlineData(PlaygroundProviderKind.Gemini, "gemini-5-helium-preview", "gemini-6-helium", "gemini-6-helium-001")]
    [InlineData(PlaygroundProviderKind.Claude, "claude-new-line-5-6", "claude-new-line-5-10", "claude-new-line-5-10-20261114")]
    [InlineData(PlaygroundProviderKind.DeepSeek, "deepseek-v4-new-line", "deepseek-v4.2-new-line", "deepseek-v4.2-new-line-20261114")]
    [InlineData(PlaygroundProviderKind.Grok, "grok-4-new-line", "grok-4.1-new-line", "grok-4.1-new-line-20261114")]
    public void ANewCatalogFamilyDoesNotRequireANameAllowlist(PlaygroundProviderKind provider, string older, string newer, string snapshot)
    {
        var models = Catalog(older, newer, snapshot, newer + "-realtime", newer + "-image", newer + "-computer-use");
        if (provider == PlaygroundProviderKind.Gemini || !newer.StartsWith("gpt-", StringComparison.Ordinal) && provider == PlaygroundProviderKind.OpenAI)
            models = [.. models.Select(model => model with { CatalogTextOutput = true })];
        var winner = Assert.Single(PlaygroundModelSelection.SelectLatest(provider, models));
        Assert.Equal(newer, winner.Id);
        Assert.Equal(winner, Assert.Single(PlaygroundModelSelection.SelectLatest(provider, models.Reverse())));
        Assert.False(winner.Capabilities.Verified);
    }

    [Fact]
    public void DirectoryMetadataCanKeepAnUnfamiliarFamilyOrExcludeNonText()
    {
        var candidates = new[] {
            Model("claude-new-name-release-1") with { CatalogFamily = "new-line", Version = "1.0" },
            Model("claude-another-name-release-2") with { CatalogFamily = "new-line", Version = "2.0" },
            Model("claude-video") with { CatalogTextOutput = false },
            Model("claude-mystery") with { CatalogTextOutput = false },
            Model("claude-new-line-99") with { Lifecycle = "deprecated" }
        };
        Assert.Equal(candidates[1], Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Claude, candidates)));
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
        Assert.Equal(new[] { "gemini-4-flash-preview", "gemini-4-argon" }, selected.Select(x => x.Id));
    }

    [Fact]
    public void GoogleKeepsTheLatestVisibleGeneralTextFamilyAfterRenames()
    {
        static PlaygroundModel TextModel(string id) => Model(id) with
        { GenerationMethodsReported = true, GenerationMethods = ["createInteraction"] };
        var models = Catalog("gemini-3.8-flash", "gemini-3.1-pro-preview", "gemini-4-argon", "gemini-3.5-flash-lite")
            .AddRange(new[] { TextModel("gemini-4-hydrogen"), TextModel("gemini-4-carbon"),
                TextModel("gemini-5-nitrogen-preview"), TextModel("gemini-6-nitrogen"),
                TextModel("gemini-9-carbon-cyber-permissive"), TextModel("gemini-9-hydrogen-live"),
                TextModel("gemini-9-image"), TextModel("gemini-9-vision") with { CatalogTextOutput = false }, Model("gemini-9-unknown") });
        var selected = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, models);
        Assert.Equal(new[] { "gemini-6-nitrogen" }, selected.Select(model => model.Id));
        Assert.Equal(selected.Select(model => model.Id),
            PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, models.Reverse()).Select(model => model.Id));
        Assert.Contains(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, models.Take(2)), model => model.Id == "gemini-3.8-flash");
        Assert.False(selected.Last().Capabilities.Verified);
    }

    [Fact]
    public void BothOfficialMenusFilterTheOldAndSpecializedModelsFromTheReportedLists()
    {
        var gpt = Catalog("gpt-6-luna", "gpt-6.1-terra", "gpt-6.1-sol", "gpt-6-astra", "gpt-4o", "gpt-4o-mini",
            "chat-latest", "gpt-5.5", "gpt-5.4-mini", "gpt-5.4-nano", "o3", "gpt-5.5-pro", "gpt-image-2", "gpt-6-audio",
            "gpt-6-sol-20261104", "gpt-7-bel-preview", "gpt-bel");
        Assert.Equal(new[] { "gpt-6-luna", "gpt-6.1-terra", "gpt-6.1-sol", "gpt-6-astra", "gpt-7-bel-preview" },
            PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, gpt).Select(x => x.Id));
        Assert.Equal("gpt-bel", Assert.Single(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI,
            gpt.Where(x => x.Id is "chat-latest" or "gpt-bel"))).Id);

        var google = Catalog("gemini-3.5-flash-lite", "gemini-4-hydrogen", "gemini-4-carbon", "gemini-4-argon",
            "antigravity-agent-preview", "antigravity-agent-preview-latest", "gemma-4-26b-a4b-it", "gemma-4-31b-it", "lyria-3.5",
            "lyria-3-clip-preview", "lyria-3-pro-preview", "nano-banana-2.1", "gemini-omni-1.2-flash", "gemini-omni-flash-preview",
            "gemini-4-hydrogen-latest", "gemini-4-carbon-audio", "gemini-9-unknown");
        google = [.. google.Select(x => x.Id == "gemini-9-unknown" ? x : x with
        { GenerationMethods = ["generateContent"], GenerationMethodsReported = true })];
        Assert.Equal(new[] { "gemini-4-hydrogen", "gemini-4-carbon", "gemini-4-argon" },
            PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.Gemini, google).Select(x => x.Id));
    }

    [Fact]
    public void IndependentlyVersionedTextProductsDoNotUseGptGenerationNumbers()
    {
        var fresh = PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI,
            Catalog("gpt-6.1-sol", "gpt-5.4-mini").Add(Model("bel-2") with { CatalogTextOutput = true })
                .Add(Model("movie-gen-40") with { CatalogTextOutput = false }));
        Assert.Equal(new[] { "gpt-6.1-sol", "bel-2" }, fresh.Select(model => model.Id));
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
        Assert.Empty(PlaygroundModelSelection.SelectLatest(PlaygroundProviderKind.OpenAI, Catalog("gpt-image-99", "gpt-4-instruct", "gemini-4-argon")));
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
