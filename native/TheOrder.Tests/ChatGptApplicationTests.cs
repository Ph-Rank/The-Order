using TheOrder.Application.Playground;
using Xunit;

namespace TheOrder.Tests;

public sealed partial class PlaygroundCoreTests
{
    [Fact] public async Task ChatGptConversationWithoutAccountIsRejectedBeforeProviderAndMessageCommit()
    {
        await using var rig = new Rig(new Provider(PlaygroundProviderKind.ChatGPT)); var id = await rig.Ready();
        var error = await Assert.ThrowsAsync<PlaygroundProviderException>(() => rig.Service.SendAsync(id, "synthetic question"));
        Assert.Equal(PlaygroundFailureKind.Authentication, error.Failure.Kind);
        Assert.Empty(rig.Provider.Requests); Assert.Empty(rig.Service.Snapshot.Messages);
    }
    [Fact] public async Task ChatGptTurnCapturesExplicitAccountInProviderRequestAndContinuationBinding()
    {
        await using var rig = new Rig(new Provider(PlaygroundProviderKind.ChatGPT)); var id = await rig.Ready();
        var options = rig.Service.Snapshot.Conversations.Single(c => c.Id == id).Options;
        await rig.Service.UpdateConversationAsync(id, options with { ChatGptAccountId = "oaiapp_synthetic" });
        await rig.Service.SendAsync(id, "synthetic question");
        var sent = Assert.Single(rig.Provider.Requests);
        Assert.Equal("oaiapp_synthetic", sent.ChatGptAccountId); Assert.Contains("oaiapp_synthetic", sent.Binding);
        Assert.Equal("oaiapp_synthetic", rig.Service.Snapshot.Conversations.Single().Options.ChatGptAccountId);
    }
}
