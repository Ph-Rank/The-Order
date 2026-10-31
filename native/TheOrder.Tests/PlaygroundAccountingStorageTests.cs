using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundAccountingStorageTests
{
    [Fact]
    public async Task LiteralLegacyClaudeCacheReadLargerThanUncachedInputLoadsAndResavesWithoutMigration()
    {
        using var rig = new Rig(); await rig.Store.SaveAsync(rig.State, 0, default);
        var document = LegacyClaudeDocument(rig); byte[] original = Protect(document);
        await File.WriteAllBytesAsync(rig.Path, original);
        var loaded = (await new WindowsPlaygroundStore(rig.Profile.Configuration()).LoadAsync(default))!;
        var message = Assert.Single(loaded.Messages);
        Assert.Equal("LEGACY-CLAUDE-ORIGINAL-TEXT", message.Text);
        Assert.Equal(100, message.Usage!.InputTokens); Assert.Equal(200, message.Usage.CachedInputTokens);
        Assert.Null(message.Usage.CacheWriteInputTokens); Assert.Null(message.Usage.BillingModel); Assert.Null(message.Cost);
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path)); // Read does not rewrite or normalize old counts.
        var estimate = PlaygroundPricing.ForMessage(message);
        Assert.False(estimate.IsActual); Assert.False(estimate.IsComplete);
        await rig.Store.SaveAsync(loaded with { Revision = 2 }, 1, default);
        var reopened = Assert.Single((await new WindowsPlaygroundStore(rig.Profile.Configuration()).LoadAsync(default))!.Messages);
        Assert.Equal(message.Text, reopened.Text); Assert.Equal(message.Usage, reopened.Usage); Assert.Null(reopened.Cost);
        Assert.False(PlaygroundPricing.ForMessage(reopened).IsActual);
    }

    [Theory]
    [InlineData("BillingModel")]
    [InlineData("ServiceTier")]
    [InlineData("ReportedCostUsd")]
    [InlineData("CacheWriteInputTokens")]
    [InlineData("CacheWrite5mInputTokens")]
    [InlineData("CacheWrite1hInputTokens")]
    [InlineData("CacheWrite30mInputTokens")]
    [InlineData("CacheWrite12hInputTokens")]
    [InlineData("Cost")]
    public async Task NewAccountingMarkerCannotUseTheLegacyClaudeSubsetException(string marker)
    {
        using var rig = new Rig(); await rig.Store.SaveAsync(rig.State, 0, default);
        var document = LegacyClaudeDocument(rig); var message = document["Messages"]![0]!;
        if (marker == "Cost") message["Cost"] = JsonSerializer.SerializeToNode(new PlaygroundCostEstimate("USD", null, null, false, false, "Synthetic unknown cost"));
        else message["Usage"]![marker] = marker switch
        {
            "BillingModel" => JsonValue.Create("claude-opus-5-5"),
            "ServiceTier" => JsonValue.Create("standard"),
            _ => JsonValue.Create(0)
        };
        byte[] original = Protect(document); await File.WriteAllBytesAsync(rig.Path, original);
        await Assert.ThrowsAsync<InvalidDataException>(() => new WindowsPlaygroundStore(rig.Profile.Configuration()).LoadAsync(default));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path));
    }

    private static JsonObject LegacyClaudeDocument(Rig rig)
    {
        var document = JsonSerializer.SerializeToNode(rig.State)!.AsObject(); var message = document["Messages"]![0]!.AsObject();
        message["Provider"] = (int)PlaygroundProviderKind.Claude; message["Model"] = "claude-opus-5-5";
        message["Text"] = "LEGACY-CLAUDE-ORIGINAL-TEXT"; message.Remove("Cost");
        // Exact six-field old usage wire shape, with no new accounting fields.
        message["Usage"] = JsonNode.Parse("""{"InputTokens":100,"OutputTokens":5,"ReasoningTokens":null,"CachedInputTokens":200,"Searches":null,"Fetches":null}""");
        return document;
    }
    private static byte[] Protect(JsonObject document) => ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(document), null, DataProtectionScope.CurrentUser);

    [Fact]
    public async Task OldNullAccountingAndReportedNativeCostRemainReadableAfterRestart()
    {
        using var rig = new Rig(); var state = rig.State;
        state = state with { Messages = [state.Messages[0] with { Usage = null, Cost = null }, state.Messages[0] with { Id = Guid.NewGuid() }] };
        await rig.Store.SaveAsync(state, 0, default);
        var loaded = (await new WindowsPlaygroundStore(rig.Profile.Configuration()).LoadAsync(default))!;
        Assert.Null(loaded.Messages[0].Usage); Assert.Null(loaded.Messages[0].Cost);
        Assert.Equal(state.Messages[1].Usage, loaded.Messages[1].Usage);
        Assert.Equal(state.Messages[1].Cost, loaded.Messages[1].Cost);
        Assert.True(loaded.Messages[1].Cost!.IsActual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidUsageNeverReplacesPriorValidEncryptedState(int scenario)
    {
        using var rig = new Rig(); var state = rig.State;
        await rig.Store.SaveAsync(state, 0, default); byte[] original = await File.ReadAllBytesAsync(rig.Path);
        var usage = scenario switch
        {
            0 => new PlaygroundUsage(InputTokens: -1),
            1 => new PlaygroundUsage(InputTokens: 10, CachedInputTokens: 9) { CacheWriteInputTokens = 2 },
            2 => new PlaygroundUsage(InputTokens: 20) { CacheWriteInputTokens = 5, CacheWrite1hInputTokens = 6 },
            3 => new PlaygroundUsage(OutputTokens: 3, ReasoningTokens: 4),
            4 => new PlaygroundUsage(InputTokens: 1000000000001),
            _ => new PlaygroundUsage(InputTokens: 1) { BillingModel = "unsafe\nmodel" }
        };
        var invalid = state with { Revision = 2, Messages = [state.Messages[0] with { Usage = usage, Cost = null }] };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Store.SaveAsync(invalid, 1, default));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path));
        Assert.Equal(1, (await rig.Store.LoadAsync(default))!.Revision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task SavedCostMustBeValidAndReportedActualMustMatchNativeGrokUsage(int scenario)
    {
        using var rig = new Rig(); var state = rig.State; var original = state.Messages[0];
        var invalid = scenario switch
        {
            0 => original with { Cost = original.Cost! with { Minimum = -1m } },
            1 => original with { Cost = original.Cost! with { Minimum = 1m, Maximum = .5m, IsActual = false } },
            2 => original with { Cost = original.Cost! with { IsComplete = false } },
            3 => original with { Cost = original.Cost! with { Currency = "USD\nFake" } },
            4 => original with { Provider = PlaygroundProviderKind.OpenAI },
            5 => original with { Usage = null },
            _ => original with { Cost = original.Cost! with { Minimum = .02m, Maximum = .02m } }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Store.SaveAsync(state with { Messages = [invalid] }, 0, default));
        Assert.False(File.Exists(rig.Path));
    }

    [Fact]
    public async Task ReadBoundaryRejectsForgedActualAmountInsteadOfPromotingItToAnApiBill()
    {
        using var rig = new Rig(); var state = rig.State;
        await rig.Store.SaveAsync(state, 0, default);
        var changed = state with { Messages = [state.Messages[0] with { Cost = new("USD", 1000m, 1000m, true, true, "fabricated amount") }] };
        byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(changed), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(rig.Path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Store.LoadAsync(default));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path));
    }

    [Fact]
    public async Task RecoveryKeepsLatestAccountingRatherThanRollingBackConsumedCost()
    {
        using var rig = new Rig(); var state = rig.State;
        await rig.Store.SaveAsync(state, 0, default);
        var message = state.Messages[0] with { Usage = state.Messages[0].Usage! with { ReportedCostUsd = .02m },
            Cost = state.Messages[0].Cost! with { Minimum = .02m, Maximum = .02m } };
        await rig.Store.SaveAsync(state with { Revision = 2, Messages = [message] }, 1, default);
        await rig.Store.RestoreLastGoodAsync(default);
        var recovered = Assert.Single((await rig.Store.LoadAsync(default))!.Messages);
        Assert.Equal(.02m, recovered.Usage!.ReportedCostUsd); Assert.Equal(.02m, recovered.Cost!.Minimum);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Guid conversation = Guid.NewGuid();
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "the-order-accounting-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "playground", "state-v1.dpapi");
        public NativeProfileStore Profile { get; }
        public WindowsPlaygroundStore Store { get; }
        public Rig()
        { Profile = NativeProfileStore.Open(new(NativeProfileKind.Test, Root), new NativeLibraryCodec()); Store = new(Profile.Configuration()); }
        public PlaygroundState State => new()
        {
            Identity = Store.Identity, Revision = 1,
            Conversations = [new(conversation, "合成费用记录", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new())],
            Messages = [new(Guid.NewGuid(), conversation, "assistant", "Synthetic reply", DateTimeOffset.UtcNow, PlaygroundMessageStatus.Complete)
            { Provider = PlaygroundProviderKind.Grok, Model = "grok-4.7", Usage = new(100, 10, 2, 0, 0, 0)
                { BillingModel = "grok-4.7", ReportedCostUsd = .01m }, Cost = new("USD", .01m, .01m, true, true, "Synthetic native usage") }]
        };
        public void Dispose() { Profile.Dispose(); Directory.Delete(Root, true); }
    }
}
