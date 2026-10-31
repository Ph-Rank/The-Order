using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Infrastructure;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundStorageTests
{
    private sealed class Rig : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "the-order-playground-" + Guid.NewGuid().ToString("N"));
        public NativeProfileStore Profile { get; }
        public WindowsPlaygroundStore Store { get; }
        public Rig() { Profile = NativeProfileStore.Open(new(NativeProfileKind.Test, Root), new NativeLibraryCodec()); Store = new(Profile.Configuration()); }
        public PlaygroundState State => new() { Identity = Store.Identity };
        public void Dispose() { Profile.Dispose(); Directory.Delete(Root, true); }
    }
    [Fact] public async Task LazyLoadDoesNotCreateAiFilesAndCommitIsEncryptedRevisionBound()
    {
        using var rig = new Rig(); Assert.Null(await rig.Store.LoadAsync(default)); Assert.False(Directory.Exists(Path.Combine(rig.Root, "playground")));
        var id = Guid.NewGuid(); var state = rig.State with { Revision = 1, Conversations = [new(id, "合成中文对话", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new())],
            Messages = [new(Guid.NewGuid(), id, "user", "唯一的合成隐私文本，不是真实用户资料", DateTimeOffset.UtcNow, PlaygroundMessageStatus.Complete)] };
        await rig.Store.SaveAsync(state, 0, default); var reopened = new WindowsPlaygroundStore(rig.Profile.Configuration()); var loaded = await reopened.LoadAsync(default);
        Assert.Equal(state.Messages.Select(m => (m.Id, m.Text)), loaded!.Messages.Select(m => (m.Id, m.Text))); Assert.Equal(1, loaded.Revision);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(rig.Root, "playground", "state-v1.dpapi")); Assert.DoesNotContain("唯一的合成", Encoding.UTF8.GetString(bytes));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Store.SaveAsync(state with { Revision = 2 }, 0, default));
        Assert.Equal(1, (await rig.Store.LoadAsync(default))!.Revision);
    }
    [Fact] public async Task LongCompletedAnswerPersistsWithoutLosingExplicitGenerationSettings()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        // A 64k-token English answer can exceed the former 200k-character disk limit.
        string text = string.Concat(Enumerable.Repeat("Long answer, preserved verbatim. ", 10000));
        var options = new PlaygroundConversationOptions { ReasoningEffort = "low" };
        var settings = new PlaygroundSettings { MaximumOutputTokens = 8192, MaximumContextCharacters = 48000, TimeoutSeconds = 120 };
        await rig.Store.SaveAsync(rig.State with { Revision = 1, Settings = settings, Conversations = [new(id, "合成长回复", now, now, options)],
            Messages = [new(Guid.NewGuid(), id, "assistant", text, now, PlaygroundMessageStatus.Complete)] }, 0, default);
        var loaded = (await new WindowsPlaygroundStore(rig.Profile.Configuration()).LoadAsync(default))!;
        Assert.Equal(text, Assert.Single(loaded.Messages).Text);
        Assert.Equal("low", Assert.Single(loaded.Conversations).Options.ReasoningEffort);
        Assert.Equal(8192, loaded.Settings.MaximumOutputTokens);
        Assert.Equal(48000, loaded.Settings.MaximumContextCharacters);
        Assert.Equal(120, loaded.Settings.TimeoutSeconds);
    }
    [Fact] public async Task TemporaryAndGoogleGroundedBodiesNeverEnterDurableState()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var permanent = Guid.NewGuid(); var temporary = Guid.NewGuid(); var user = Guid.NewGuid();
        var state = rig.State with { Revision = 1, Conversations = [new(permanent, "普通", now, now, new()), new(temporary, "临时", now, now, new(), true)],
            Messages = [new(user, temporary, "user", "TEMPORARY-PROBE", now, PlaygroundMessageStatus.Complete),
                new(Guid.NewGuid(), permanent, "research", "GOOGLE-RESTRICTED-PROBE", now, PlaygroundMessageStatus.Complete)
                { Retention = PlaygroundRetention.SessionOnly, SearchSuggestionsHtml = "SUGGESTIONS-PROBE", Sources = [new("g", PlaygroundSourceKind.Web, "Restricted source")] }],
            Memories = [new(Guid.NewGuid(), "TEMPORARY-MEMORY", temporary, user, now, now)] };
        await rig.Store.SaveAsync(state, 0, default); var loaded = (await rig.Store.LoadAsync(default))!;
        Assert.Single(loaded.Conversations); Assert.Empty(loaded.Memories); Assert.Single(loaded.Messages);
        Assert.DoesNotContain("GOOGLE-RESTRICTED", loaded.Messages[0].Text); Assert.Empty(loaded.Messages[0].Sources); Assert.Null(loaded.Messages[0].SearchSuggestionsHtml);
    }
    [Fact] public async Task CrossProfileCopiedStateAndCredentialsAreRejected()
    {
        using var a = new Rig(); using var b = new Rig(); var conversation = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await a.Store.SaveAsync(a.State with { Revision = 1, Conversations = [new(conversation, "附件隔离", now, now, new())],
            Messages = [new(Guid.NewGuid(), conversation, "user", "资料", now, PlaygroundMessageStatus.Complete)
                { Attachments = [new("fixture.txt", "CROSS-PROFILE-ATTACHMENT-CANARY")] }] }, 0, default);
        var keys = new WindowsPlaygroundCredentials(a.Profile.Configuration()); await keys.SaveAsync(PlaygroundProviderKind.OpenAI, PlaygroundEndpointPolicy.Default(PlaygroundProviderKind.OpenAI), "synthetic-not-a-real-api-key", default);
        var destination = Path.Combine(b.Root, "playground"); Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(Path.Combine(a.Root, "playground"))) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        await Assert.ThrowsAsync<InvalidDataException>(() => b.Store.LoadAsync(default));
        var other = new WindowsPlaygroundCredentials(b.Profile.Configuration());
        await Assert.ThrowsAsync<InvalidDataException>(() => other.HasAsync(PlaygroundProviderKind.OpenAI, PlaygroundEndpointPolicy.Default(PlaygroundProviderKind.OpenAI), default));
    }
    [Fact] public async Task CredentialsUseProviderEndpointSlotsAndDoNotTouchCatalogKey()
    {
        using var rig = new Rig(); var keys = new WindowsPlaygroundCredentials(rig.Profile.Configuration());
        var before = rig.Profile.ReadComponent("native-online-key.dpapi");
        foreach (var provider in Enum.GetValues<PlaygroundProviderKind>()) await keys.SaveAsync(provider, PlaygroundEndpointPolicy.Default(provider), "synthetic-" + provider, default);
        foreach (var provider in Enum.GetValues<PlaygroundProviderKind>()) Assert.Equal("synthetic-" + provider, await keys.ReadAsync(provider, PlaygroundEndpointPolicy.Default(provider) + "/", default));
        await keys.ClearAsync(PlaygroundProviderKind.OpenAI, PlaygroundEndpointPolicy.Default(PlaygroundProviderKind.OpenAI), default);
        Assert.False(await keys.HasAsync(PlaygroundProviderKind.OpenAI, PlaygroundEndpointPolicy.Default(PlaygroundProviderKind.OpenAI), default));
        Assert.True(await keys.HasAsync(PlaygroundProviderKind.Gemini, PlaygroundEndpointPolicy.Default(PlaygroundProviderKind.Gemini), default));
        Assert.Equal(before, rig.Profile.ReadComponent("native-online-key.dpapi"));
    }
    [Fact] public async Task CorruptionFailsClosedAndExplicitRestorePreservesDamagedBytes()
    {
        using var rig = new Rig(); await rig.Store.SaveAsync(rig.State with { Revision = 1 }, 0, default); await rig.Store.SaveAsync(rig.State with { Revision = 2 }, 1, default);
        string path = Path.Combine(rig.Root, "playground", "state-v1.dpapi"); await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Store.LoadAsync(default)); await rig.Store.RestoreLastGoodAsync(default);
        Assert.Equal(1, (await rig.Store.LoadAsync(default))!.Revision); Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "state-preserved-*.dpapi"));
    }
    [Theory] [InlineData("https://api.openai.com.evil.invalid/v1")] [InlineData("http://api.openai.com/v1")]
    [InlineData("https://api.openai.com:444/v1")] [InlineData("https://user@api.openai.com/v1")]
    [InlineData("https://api.openai.com/v1?key=synthetic")]
    public void CredentialEndpointsCannotBeRedirected(string endpoint) => Assert.Throws<ArgumentException>(() => PlaygroundEndpointPolicy.Normalize(PlaygroundProviderKind.OpenAI, endpoint));
    [Theory] [InlineData("http://127.0.0.1/")] [InlineData("http://10.0.0.1/")] [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")] [InlineData("http://metadata.internal/")] [InlineData("file:///tmp/x")]
    public void PublicLinksRejectLocalAndDangerousDestinations(string url) => Assert.False(PlaygroundEndpointPolicy.SafePublicUrl(url));
    [Fact] public async Task HidingEntryDoesNotInitializeChatOrCredentials()
    {
        using var rig = new Rig(); var navigation = new WindowsPlaygroundNavigation(rig.Profile.Configuration());
        Assert.True(await navigation.ReadVisibleAsync()); Assert.False(Directory.Exists(Path.Combine(rig.Root, "playground")));
        await navigation.SetVisibleAsync(false); Assert.False(await navigation.ReadVisibleAsync());
        Assert.Equal("navigation-v1.json", Path.GetFileName(Assert.Single(Directory.GetFiles(Path.Combine(rig.Root, "playground")))));
        Assert.Null(await rig.Store.LoadAsync(default));
    }
    [Fact] public async Task ExplicitRecoveryNeverDowngradesFutureState()
    {
        using var rig = new Rig();
        await rig.Store.SaveAsync(rig.State with { Revision = 1 }, 0, default);
        await rig.Store.SaveAsync(rig.State with { Revision = 2 }, 1, default);
        string path = Path.Combine(rig.Root, "playground", "state-v1.dpapi");
        byte[] future = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(rig.State with { Revision = 3, Version = 2 }), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, future);
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Store.LoadAsync(default));
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Store.RestoreLastGoodAsync(default));
        Assert.Equal(future, await File.ReadAllBytesAsync(path));
    }
    [Fact] public async Task DisplayOnlyIsAlsoExcludedAtTheStorageBoundary()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        await rig.Store.SaveAsync(rig.State with { Revision = 1, Conversations = [new(id, "合成来源", now, now, new())],
            Messages = [new(Guid.NewGuid(), id, "assistant", "DISPLAY-ONLY-SHOULD-NOT-PERSIST", now, PlaygroundMessageStatus.Complete)
                { Retention = PlaygroundRetention.DisplayOnly, Sources = [new("private", PlaygroundSourceKind.Web, "受限来源")], SearchSuggestionsHtml = "restricted" }] }, 0, default);
        var message = Assert.Single((await rig.Store.LoadAsync(default))!.Messages);
        Assert.DoesNotContain("SHOULD-NOT-PERSIST", message.Text); Assert.Empty(message.Sources); Assert.Null(message.SearchSuggestionsHtml);
    }
    [Fact] public async Task RecoveryCannotRestoreForgottenMemoryDeletedConversationOrRevokedGrants()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var keep = Guid.NewGuid(); var deleted = Guid.NewGuid(); var user = Guid.NewGuid(); var memory = Guid.NewGuid();
        var first = rig.State with { Revision = 1, Settings = new() { Enabled = true, DiscussionConsent = true, Permissions = new(true, true, true, true) },
            Conversations = [new(keep, "保留对话", now, now, new() { Spoiler = PlaygroundSpoilerMode.Full, SystemInstructions = "旧的系统提示" }), new(deleted, "将删除", now, now, new())],
            Messages = [new(user, keep, "user", "合成旧观点", now, PlaygroundMessageStatus.Complete), new(Guid.NewGuid(), deleted, "user", "DELETED-CONTENT", now, PlaygroundMessageStatus.Complete)],
            Memories = [new(memory, "合成旧观点", keep, user, now, now) { OriginalQuote = "合成旧观点" }] };
        await rig.Store.SaveAsync(first, 0, default);
        var current = first with { Revision = 2, Settings = new() { DiscussionConsent = false }, Conversations = [first.Conversations[0] with { Options = new() { SystemInstructions = "当前系统提示" } }], Messages = [first.Messages[0]], Memories = [],
            Suppressions = [new(Guid.NewGuid(), memory, "sha256:synthetic", [user], now)] };
        await rig.Store.SaveAsync(current, 1, default);
        await File.WriteAllBytesAsync(Path.Combine(rig.Root, "playground", "state-v1.dpapi"), [1, 2, 3]);
        await rig.Store.RestoreLastGoodAsync(default); var recovered = (await rig.Store.LoadAsync(default))!;
        Assert.False(recovered.Settings.Enabled); Assert.Equal(new PlaygroundPermissions(), recovered.Settings.Permissions);
        Assert.Empty(recovered.Memories); Assert.Single(recovered.Suppressions); Assert.Single(recovered.Conversations); Assert.Single(recovered.Messages);
        Assert.Equal(PlaygroundSpoilerMode.LibraryStatus, recovered.Conversations[0].Options.Spoiler);
        Assert.False(recovered.Settings.DiscussionConsent);
        Assert.Equal("当前系统提示", recovered.Conversations[0].Options.SystemInstructions);
    }
    [Fact] public async Task RecoveryCannotTurnARejectedLateReplyBackIntoCompletedEvidence()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var reply = Guid.NewGuid();
        var first = rig.State with { Revision = 1, Conversations = [new(id, "合成停止", now, now, new())],
            Messages = [new(reply, id, "assistant", "LATE-REJECTED", now, PlaygroundMessageStatus.Complete)] };
        await rig.Store.SaveAsync(first, 0, default);
        await rig.Store.SaveAsync(first with { Revision = 2, Messages = [first.Messages[0] with { Text = "", Status = PlaygroundMessageStatus.Cancelled, Error = "已撤权" }] }, 1, default);
        await rig.Store.RestoreLastGoodAsync(default); var recovered = Assert.Single((await rig.Store.LoadAsync(default))!.Messages);
        Assert.Equal(PlaygroundMessageStatus.Cancelled, recovered.Status); Assert.Empty(recovered.Text);
    }
    [Fact] public async Task TextAndImageSnapshotsRoundTripWhileTemporaryAttachmentsNeverBecomeDurable()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var normal = Guid.NewGuid(); var temporary = Guid.NewGuid();
        var image = new PlaygroundAttachment("pixel.png", "") { MediaType = "image/png", ImageWidth = 1, ImageHeight = 1,
            ImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/f6kAAAAASUVORK5CYII=" };
        ImmutableArray<PlaygroundAttachment> attachments = [new("guide.md", "DURABLE-FILE-SNAPSHOT-CANARY"), image];
        await rig.Store.SaveAsync(rig.State with { Revision = 1, Conversations = [new(normal, "文件快照", now, now, new()), new(temporary, "临时", now, now, new(), true)],
            Messages = [new(Guid.NewGuid(), normal, "user", "", now, PlaygroundMessageStatus.Complete) { Attachments = attachments },
                new(Guid.NewGuid(), temporary, "user", "", now, PlaygroundMessageStatus.Complete)
                    { Attachments = [new("TEMP-FILE-CANARY.txt", "TEMP-ATTACHMENT-BODY-CANARY"), image with { Name = "TEMP-IMAGE-CANARY.png" }] }] }, 0, default);
        var loaded = (await new WindowsPlaygroundStore(rig.Profile.Configuration()).LoadAsync(default))!;
        var message = Assert.Single(loaded.Messages); Assert.Empty(message.Text); Assert.Equal(attachments.ToArray(), message.Attachments.ToArray());
        Assert.DoesNotContain("TEMP-", JsonSerializer.Serialize(loaded));
        string encrypted = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(rig.Root, "playground", "state-v1.dpapi")));
        Assert.DoesNotContain("DURABLE-FILE-SNAPSHOT-CANARY", encrypted); Assert.DoesNotContain(image.ImageBase64!, encrypted);
    }
    [Fact] public async Task RecoveryDoesNotBringBackAnAttachmentRemovedFromTheCurrentSnapshot()
    {
        using var rig = new Rig(); var now = DateTimeOffset.UtcNow; var conversation = Guid.NewGuid(); var messageId = Guid.NewGuid();
        var first = rig.State with { Revision = 1, Conversations = [new(conversation, "文件快照", now, now, new())],
            Messages = [new(messageId, conversation, "user", "资料", now, PlaygroundMessageStatus.Complete) { Attachments = [new("old.txt", "REMOVED-ATTACHMENT-CANARY")] }] };
        await rig.Store.SaveAsync(first, 0, default);
        await rig.Store.SaveAsync(first with { Revision = 2, Messages = [first.Messages[0] with { Attachments = [] }] }, 1, default);
        await rig.Store.RestoreLastGoodAsync(default); Assert.Empty(Assert.Single((await rig.Store.LoadAsync(default))!.Messages).Attachments);
    }
}
