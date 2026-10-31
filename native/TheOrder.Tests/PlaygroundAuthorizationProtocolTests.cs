using System.Net;
using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Domain;
using TheOrder.Infrastructure.PlaygroundProviders;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundAuthorizationProtocolTests
{
    // The real service and native provider are used, but this handler has no
    // network transport. Every record, credential and response is synthetic.
    [Theory]
    [InlineData("planned", "未设置", "不表示用户计划游玩")]
    [InlineData("completed", "玩过", "不证明已通关、全路线或 CG 回收完成")]
    public async Task LibraryGrantWithoutDiscussionHistoryExplainsAndExecutesPlayHistoryOnNativeWire(
        string rawStatus, string expectedLabel, string expectedNotice)
    {
        using var handler = new LocalProtocol(); using var client = new HttpClient(handler);
        var store = new Store(); var credentials = new Credentials();
        var providers = new PlaygroundProviderRegistry(client, credentials).Providers;
        LibraryEntry entry = new("g", "合成作品", false, null, [], null, rawStatus, null,
            false, [], [], "", 3600, 1,
            [new("synthetic-session", "2026-10-29T00:00:00Z", "2026-10-29T01:00:00Z", 3600)],
            "2026-10-01T00:00:00Z", "2026-10-29T01:00:00Z", null, null, new(false, null));
        await using var service = new PlaygroundService(store, credentials, providers,
            () => new([entry], LibraryRecovery.Main));
        const string endpoint = "https://api.openai.com/v1";
        await service.InitializeAsync();
        await service.ConfigureAsync(new()
        {
            Enabled = true, Permissions = new(Library: true, History: false, Memory: false, AutoMemory: false),
            Connections = [new(PlaygroundProviderKind.OpenAI, endpoint, "gpt-6.1-sol", true)]
        });
        var id = await service.CreateConversationAsync();
        var options = service.Snapshot.Conversations.Single(c => c.Id == id).Options;
        await service.UpdateConversationAsync(id, options with
        {
            Search = PlaygroundSearchMode.Off, Spoiler = PlaygroundSpoilerMode.Full, UseSavedContext = true
        });
        await service.SendAsync(id, "请列出 2026 年 10 月 29 日这一天逐次游玩的起止时间和时长。");

        Assert.Equal(PlaygroundMessageStatus.Complete,
            Assert.Single(service.Snapshot.Messages, m => m.Role == "assistant").Status);
        Assert.Equal(2, handler.Bodies.Count);
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        string instructions = first.RootElement.GetProperty("instructions").GetString()!;
        Assert.Contains("\"Library\":true,\"History\":false", instructions);
        Assert.Contains("Library 指书架及游玩资料", instructions);
        Assert.Contains("play_history（逐次游玩历史）", instructions);
        Assert.Contains("History 仅指 Playground 历史讨论", instructions);
        Assert.Contains("History=false 不代表禁止读取 play_history", instructions);
        Assert.Contains("实际可用能力以本轮提供的工具列表为准", instructions);
        Assert.Contains("尚未调用不代表无权访问或无法读取", instructions);
        Assert.Contains("不要从游玩时长推断", instructions);
        Assert.Contains("先按工具提供的 status_label（权威界面名称）和 status_notice 理解", instructions);
        string[] tools = first.RootElement.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "recent_play", "rank_playtime", "search_library", "get_work", "play_history" }, tools);

        using var second = JsonDocument.Parse(handler.Bodies[1]);
        var result = Assert.Single(second.RootElement.GetProperty("input").EnumerateArray(),
            item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call_output");
        Assert.Equal("history-call", result.GetProperty("call_id").GetString());
        using var output = JsonDocument.Parse(result.GetProperty("output").GetString()!);
        Assert.Equal("ok", output.RootElement.GetProperty("status").GetString());
        var session = Assert.Single(output.RootElement.GetProperty("data").GetProperty("sessions").EnumerateArray());
        Assert.Equal("synthetic-session", session.GetProperty("session_id").GetString());
        Assert.Equal(3600, session.GetProperty("total_session_seconds").GetInt32());
        Assert.Equal(3600, session.GetProperty("seconds_within_range").GetInt32());
        var work = session.GetProperty("work");
        Assert.Equal(rawStatus, work.GetProperty("status").GetString());
        Assert.Equal(expectedLabel, work.GetProperty("status_label").GetString());
        Assert.Contains(expectedNotice, work.GetProperty("status_notice").GetString()!);
        Assert.Null(work.GetProperty("story_progress").GetString());
        Assert.Equal("未知", work.GetProperty("progress_source").GetString());
        Assert.Equal(rawStatus, entry.Status);
    }

    private sealed class LocalProtocol : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri("https://api.openai.com/v1/responses"), request.RequestUri);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            object[] output = Bodies.Count switch
            {
                1 => [new { type = "function_call", id = "history-item", call_id = "history-call", name = "play_history",
                    arguments = "{\"from\":\"2026-10-29T00:00:00Z\",\"to\":\"2026-10-30T00:00:00Z\",\"limit\":6}" }],
                2 => [new { type = "message", id = "answer-item", role = "assistant",
                    content = new[] { new { type = "output_text", text = "已读取合成游玩会话。" } } }],
                _ => throw new InvalidOperationException("Unexpected synthetic request.")
            };
            string data = JsonSerializer.Serialize(new { type = "response.completed", response = new { status = "completed", output } });
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("event: response.completed\ndata: " + data + "\n\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }
    private sealed class Store : IPlaygroundStore
    {
        public PlaygroundIdentity Identity { get; } = new(Guid.NewGuid(), Guid.NewGuid());
        private PlaygroundState? state;
        public Task<PlaygroundState?> LoadAsync(CancellationToken cancellation) => Task.FromResult(state);
        public Task SaveAsync(PlaygroundState value, long expectedRevision, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Assert.Equal(state?.Revision ?? 0, expectedRevision);
            state = value; return Task.CompletedTask;
        }
    }
    private sealed class Credentials : IPlaygroundCredentials
    {
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult(true);
        public Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.FromResult<string?>("synthetic-unused-secret");
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) => Task.CompletedTask;
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => Task.CompletedTask;
    }
}
