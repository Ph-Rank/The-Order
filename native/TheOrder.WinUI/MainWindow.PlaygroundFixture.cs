using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TheOrder.Application;
using TheOrder.Application.Playground;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private bool playgroundFixtureActive;
    private Task playgroundFixturePending = Task.CompletedTask;
    private static readonly Guid PlaygroundFixtureConversation = Guid.Parse("120ea6df-27f1-4a60-8041-555000000001");
    private static readonly Guid PlaygroundFixtureHistory = Guid.Parse("120ea6df-27f1-4a60-8041-555000000002");
    private static readonly Guid PlaygroundFixtureUser = Guid.Parse("120ea6df-27f1-4a60-8041-555000000003");
    private static readonly Guid PlaygroundFixtureMemory = Guid.Parse("120ea6df-27f1-4a60-8041-555000000004");

    private PlaygroundService? CreatePlaygroundFixture(NativeConfiguration config, OfflineLibraryClient library)
    {
        if (!config.Diagnostics || config.Profile is not NativeProfileStore profile || profile.Manifest.ProfileKind != NativeProfileKind.Test
            || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PLAYGROUND_FIXTURE") != "1") return null;
        if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(profile.Root)), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Playground synthetic profile must stay on D drive.");
        playgroundFixtureActive = true;
        var identity = new PlaygroundIdentity(profile.Manifest.ProfileId, profile.Manifest.StoreId);
        var now = new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
        var providers = Enum.GetValues<PlaygroundProviderKind>().Select(kind => new PlaygroundFixtureProvider(kind)).ToArray();
        var settings = new PlaygroundSettings { Enabled = true, Permissions = new(true, true, true, false),
            Connections = providers.Select(p => new PlaygroundConnection(p.Kind, p.DefaultEndpoint, p.Model, true)).ToImmutableArray() };
        var options = new PlaygroundConversationOptions { Model = providers[0].Model, Search = PlaygroundSearchMode.Off, Spoiler = PlaygroundSpoilerMode.Full };
        var original = new PlaygroundMessage(PlaygroundFixtureUser, PlaygroundFixtureHistory, "user", "聊《星潮留声机》时，我更在意安静段落里的环境音，而不是对白说了多少。", now.AddMinutes(-12), PlaygroundMessageStatus.Complete);
        var sources = ImmutableArray.CreateBuilder<PlaygroundSource>();
        sources.Add(new("fixture_unused", PlaygroundSourceKind.Memory, "未在回答中引用的合成上下文")
            { TargetId = PlaygroundFixtureMemory.ToString(), At = now });
        sources.Add(new("fixture_history", PlaygroundSourceKind.ChatMessage, "关于《星潮留声机》的那次讨论")
            { TargetId = PlaygroundFixtureHistory.ToString(), MessageId = original.Id.ToString(), At = original.CreatedAt, Quote = original.Text });
        sources.Add(new("fixture_memory", PlaygroundSourceKind.Memory, "示例偏好：声音与叙事节奏")
            { TargetId = PlaygroundFixtureMemory.ToString(), MessageId = original.Id.ToString(), At = now.AddMinutes(-10), Quote = "讨论作品时，可以先从声音与叙事节奏聊起。" });
        sources.Add(new("fixture_web", PlaygroundSourceKind.Web, "演示资料条目（未联网核验）")
            { Url = "https://example.com/playground-fixture", At = now.AddMinutes(-1), Quote = "离线演示中的资料条目，不是真实搜索结果。" });
        if (library.CurrentSnapshot?.Entries.FirstOrDefault() is { } entry)
            sources.Add(new("fixture_record", PlaygroundSourceKind.LocalRecord, "书架中的示例条目")
                { TargetId = entry.Id, WorkKey = PlaygroundService.WorkKeyFor(entry), At = now });
        // These two works and the player remarks are invented for the offline
        // demonstration. They do not describe a real title or a real player.
        var markdown = "## 先从声音留下的印象聊起\n\n可以。《暮色邮局》里那些对白之间的停顿，很适合从声音而不是情节开始聊。\n\n"
            + "你在聊《星潮留声机》时，也提到过自己会留意安静段落里的**环境音**。[[fixture_history]], [[fixture_memory]] 这次可以沿着这个角度继续，而不急着转向后面的剧情。[[fixture_web]], [[fixture_history]], [[fixture_web]]\n\n"
            + "### 两种不同的“安静”\n\n"
            + "- **声音仍在，话暂时停下。** 雨声、钟摆或远处的列车，会让一个场景保留自己的时间。\n"
            + "- **音乐先替人物开口。** 同一段旋律换了进入的时机，留下的情绪可能完全不同。\n"
            + "- **让阅读有一点余地。** 留白并不一定要解释成某个伏笔，也可以只是让一段日常慢下来。\n\n"
            + "> 有时让人记住的，不是音乐最响的地方，而是它停下以后，场景还留下了什么。\n\n"
            + "| 想聊的部分 | 可以留意什么 |\n| --- | --- |\n| 配乐 | 旋律何时进入，又在何时退后 |\n| 环境音 | 没有对白时，场景是否仍在流动 |\n| 节奏 | 停顿是在等情绪，还是在催促下一句 |\n\n"
            + "如果只选一个片段，你更想聊《暮色邮局》开场的雨声，还是《星潮留声机》中音乐突然停下的那一刻？";
        var state = new PlaygroundState { Identity = identity, Settings = settings,
            Conversations = [new(PlaygroundFixtureConversation, "[示例] 暮色邮局：配乐与叙事节奏", now, now, options),
                new(PlaygroundFixtureHistory, "[示例] 星潮留声机：那些对白之外的声音，为什么会留在记忆里", now.AddMinutes(-15), now.AddMinutes(-10), options)],
            Messages = [original,
                new(Guid.Parse("120ea6df-27f1-4a60-8041-555000000005"), PlaygroundFixtureHistory, "assistant", "那就先从声音聊起。对白停下来时，你会更容易注意到雨声、钟摆，或远处还在继续的日常。这和单纯的沉默，是两种不同的感受。", now.AddMinutes(-11), PlaygroundMessageStatus.Complete) { Model = providers[0].Model, Provider = PlaygroundProviderKind.OpenAI },
                new(Guid.Parse("120ea6df-27f1-4a60-8041-555000000006"), PlaygroundFixtureConversation, "user", "聊《暮色邮局》时，我总觉得很多情绪藏在对白的停顿里。可以从声音和节奏聊起吗？", now.AddMinutes(-1), PlaygroundMessageStatus.Complete),
                new(Guid.Parse("120ea6df-27f1-4a60-8041-555000000007"), PlaygroundFixtureConversation, "assistant", markdown, now, PlaygroundMessageStatus.Complete)
                    { Model = "gpt-6.1-sol", Provider = PlaygroundProviderKind.OpenAI, Sources = sources.ToImmutable(),
                        Usage = new(240, 375, CachedInputTokens: 0, Searches: 0) { BillingModel = "gpt-6.1-sol" },
                        // Offline arithmetic/display fixture, never a real charge.
                        Cost = PlaygroundPricing.Calculate(PlaygroundProviderKind.OpenAI, "gpt-6.1-sol", new(240, 375, CachedInputTokens: 0, Searches: 0), now) }],
            Memories = [new(PlaygroundFixtureMemory, "[示例偏好] 讨论作品时，可以先从声音与叙事节奏聊起。", PlaygroundFixtureHistory, original.Id, now.AddMinutes(-10), now.AddMinutes(-10)) { OriginalQuote = original.Text }] };
        return new(new PlaygroundFixtureStore(state), new PlaygroundFixtureCredentials(providers), providers, () => library.CurrentSnapshot);
    }
    private Task InitializePlaygroundFixtureAsync(PlaygroundService service) => playgroundFixtureActive ? service.InitializeAsync() : Task.CompletedTask;

    private async Task PlaygroundFixtureAsync(JsonElement request)
    {
        if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test || playgroundConfiguration?.Diagnostics != true
            || visualFixtureTimer is null || fixtureDirectory is null || Environment.GetEnvironmentVariable("THE_ORDER_NATIVE_PLAYGROUND_FIXTURE") != "1"
            || !string.Equals(Path.GetPathRoot(Path.GetFullPath(fixtureDirectory)), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Explicit isolated Playground Test capability on D drive required.");
        if (!await startupFinished.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new InvalidOperationException("Test startup incomplete.");
        var view = EnsurePlaygroundView(); await playgroundOpening;
        if (!playgroundFixtureActive || playground is null) throw new InvalidOperationException("Offline synthetic provider required.");
        var action = request.GetProperty("value").GetString();
        switch (action)
        {
            case "open": await Navigate(WorkspacePage.Playground); break;
            case "select":
                await view.SelectForFixtureAsync(request.TryGetProperty("id", out var id) ? Guid.Parse(id.GetString() ?? "") : PlaygroundFixtureConversation); break;
            case "history": await view.SelectForFixtureAsync(PlaygroundFixtureHistory); break;
            case "new": await view.CreateForFixtureAsync(request.TryGetProperty("temporary", out var temporary) && temporary.GetBoolean()); break;
            case "options":
                var current = playground.Snapshot.Conversations.FirstOrDefault(x => x.Id == view.SelectedConversationId) ?? throw new InvalidOperationException();
                var provider = request.TryGetProperty("provider", out var providerName) ? Enum.Parse<PlaygroundProviderKind>(providerName.GetString() ?? "", false) : current.Options.Provider;
                if (!Enum.IsDefined(provider)) throw new InvalidDataException();
                var connection = playground.Snapshot.Settings.Connections.First(x => x.Provider == provider);
                var spoiler = request.TryGetProperty("spoiler", out var spoilerName) ? Enum.Parse<PlaygroundSpoilerMode>(spoilerName.GetString() ?? "", false) : current.Options.Spoiler;
                var mode = request.TryGetProperty("search", out var searchName) ? Enum.Parse<PlaygroundSearchMode>(searchName.GetString() ?? "", false) : current.Options.Search;
                await view.OptionsForFixtureAsync(current.Options with { Provider = provider, Endpoint = connection.Endpoint, Model = connection.Model, Spoiler = spoiler, Search = mode }); break;
            case "models": await view.ModelsForFixtureAsync(); break;
            case "session-options": await view.SessionOptionsForFixtureAsync(PlaygroundFixtureConversation, PlaygroundFixtureHistory); break;
            case "generation":
                await view.GenerationForFixtureAsync(request.GetProperty("output").GetInt32(), request.GetProperty("context").GetInt32(), request.GetProperty("timeout").GetInt32()); break;
            case "controls":
                await view.ControlsForFixtureAsync(request.GetProperty("network").GetBoolean(), request.GetProperty("restrictSpoilers").GetBoolean(),
                    request.GetProperty("instructions").GetString() ?? "", request.GetProperty("reasoning").GetString() ?? "high"); break;
            case "budget-bounds": view.BoundsForFixture(); break;
            case "panels":
                view.SetPanelsForFixture(request.GetProperty("left").GetBoolean(), request.GetProperty("right").GetBoolean()); break;
            case "motion": view.SetReducedMotionForFixture(request.GetProperty("reduced").GetBoolean()); break;
            case "attach":
                string fixtureAttachment = request.TryGetProperty("format", out var format) && format.GetString() == "image"
                    ? "fixture-attachment.png" : "fixture-attachment.md";
                var reader = new WindowsPlaygroundAttachments(new WindowsFilesystemIdentity(), new TheOrder.Infrastructure.LocalDocumentParser());
                await view.AttachmentsForFixtureAsync(await reader.ReadAsync([Path.Combine(fixtureDirectory, fixtureAttachment)])); break;
            case "send":
            case "start":
                if (!playgroundFixturePending.IsCompleted) throw new InvalidOperationException("A synthetic turn is still running.");
                string prompt = request.TryGetProperty("prompt", out var promptValue) ? promptValue.GetString() ?? "short" : "short";
                string input = prompt switch { "short" => "《暮色邮局》给人的节奏很慢，你觉得配乐在其中起了什么作用？", "long" => "可以用《暮色邮局》和《星潮留声机》为例，展开聊聊音乐与叙事节奏吗？",
                    "slow" => "关于《暮色邮局》的声音设计，我想慢慢聊一会儿。", "fail" => "这轮请演示连接暂时中断时，讨论会如何保留。",
                    "search" => "《暮色邮局》的发售时间有公开来源吗？", _ => throw new InvalidDataException("Bounded synthetic prompt required.") };
                playgroundFixturePending = view.SendForFixtureAsync(input); if (action == "send") await playgroundFixturePending; break;
            case "stop": playground.Stop(); await playgroundFixturePending.WaitAsync(TimeSpan.FromSeconds(10)); break;
            case "wait": await playgroundFixturePending.WaitAsync(TimeSpan.FromSeconds(30)); break;
            case "retry":
                if (view.SelectedConversationId is not { } retryId) throw new InvalidOperationException();
                await playground.RetryAsync(retryId); break;
            case "source":
                string sourceId = request.GetProperty("id").GetString() ?? "";
                if (sourceId is not ("fixture_history" or "fixture_memory" or "fixture_record")) throw new InvalidDataException("Internal synthetic sources only; no browser is opened by this controller.");
                await view.SourceForFixtureAsync(sourceId); break;
            case "rename":
                if (view.SelectedConversationId is not { } renameId) throw new InvalidOperationException();
                await playground.RenameConversationAsync(renameId, "[示例] 声音、留白与阅读的节奏"); break;
            case "forget-memory":
                var memory = playground.Snapshot.Memories.FirstOrDefault() ?? throw new InvalidOperationException(); await playground.ForgetMemoryAsync(memory.Id); break;
            case "delete":
                if (view.SelectedConversationId is not { } deleteId) throw new InvalidOperationException(); await playground.DeleteConversationAsync(deleteId); break;
            case "theme": view.RequestedTheme = request.GetProperty("theme").GetString() switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => throw new InvalidDataException() }; break;
            case "resize":
                int width = request.GetProperty("width").GetInt32(), height = request.TryGetProperty("height", out var h) ? h.GetInt32() : 880;
                if (width is < 640 or > 2000 || height is < 600 or > 1600) throw new InvalidDataException();
                double scale = view.XamlRoot?.RasterizationScale ?? 1; AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale))); break;
            case "capture":
                string name = request.GetProperty("name").GetString() ?? "";
                if (name.Length is < 1 or > 60 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException();
                var capture = new RenderTargetBitmap(); await capture.RenderAsync(view);
                var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await capture.GetPixelsAsync());
                var path = Path.Combine(fixtureDirectory, name + ".png"); if (File.Exists(path)) throw new InvalidOperationException("Evidence overwrite forbidden.");
                await File.WriteAllBytesAsync(path, []);
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                {
                    var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                    encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)capture.PixelWidth, (uint)capture.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
                }
                break;
            case "state": break;
            default: throw new InvalidDataException("Unknown Playground Test action.");
        }
        view.RefreshForFixture(); await Task.Delay(160); view.UpdateLayout();
        var evidence = new { at = DateTimeOffset.UtcNow, action, synthetic = true, productionAccess = false, realCredentials = false, realApi = false,
            view = view.Evidence, rendering = view.RenderingForFixture, panelLayout = view.PanelLayoutEvidence, scale = view.XamlRoot?.RasterizationScale,
            conversationIds = playground.Snapshot.Conversations.Select(x => new { x.Id, x.Temporary, x.Title }).ToArray(),
            memoryCount = playground.Snapshot.Memories.Length, state = playground.Snapshot.Revision };
        await File.AppendAllTextAsync(Path.Combine(fixtureDirectory, "playground-evidence.jsonl"), JsonSerializer.Serialize(evidence) + "\n");
    }

    private sealed class PlaygroundFixtureStore(PlaygroundState initial) : IPlaygroundStore
    {
        private PlaygroundState state = initial;
        public PlaygroundIdentity Identity => state.Identity;
        public Task<PlaygroundState?> LoadAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult<PlaygroundState?>(state); }
        public Task SaveAsync(PlaygroundState value, long expectedRevision, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); if (state.Revision != expectedRevision || value.Identity != Identity) throw new InvalidDataException("Synthetic CAS failed.");
            state = value; return Task.CompletedTask;
        }
    }
    private sealed class PlaygroundFixtureCredentials(IEnumerable<PlaygroundFixtureProvider> providers) : IPlaygroundCredentials
    {
        private readonly HashSet<string> available = providers.Select(p => Key(p.Kind, p.DefaultEndpoint)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        private static string Key(PlaygroundProviderKind provider, string endpoint) => provider + "|" + endpoint.TrimEnd('/');
        public Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(available.Contains(Key(provider, endpoint))); }
        public async Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) => await HasAsync(provider, endpoint, cancellation) ? "offline-synthetic-placeholder" : null;
        public Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); available.Add(Key(provider, endpoint)); return Task.CompletedTask; }
        public Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); available.Remove(Key(provider, endpoint)); return Task.CompletedTask; }
    }
    private sealed class PlaygroundFixtureProvider(PlaygroundProviderKind kind) : IPlaygroundProvider
    {
        public PlaygroundProviderKind Kind => kind;
        public string Model => "fixture-" + kind.ToString().ToLowerInvariant();
        public string DefaultEndpoint => PlaygroundEndpointPolicy.Default(kind);
        public PlaygroundCapabilities Capabilities(string model, string endpoint) => model == Model
            ? new(true, true, false, true) { ImageInput = true, ReasoningEfforts = ["low", "medium", "high"], Notice = "离线演示模型，未连接服务商；示例对话不代表真实模型答复。" }
            : new() { Notice = "合成环境中的未知模型。" };
        public async Task<ImmutableArray<PlaygroundModel>> ListModelsAsync(string endpoint, CancellationToken cancellation)
        {
            await Task.Delay(180, cancellation); // Exercise the cold catalogue transition without a real request.
            string[] ids = kind switch
            {
                PlaygroundProviderKind.OpenAI => ["gpt-6.1-sol", "gpt-6-sol", "gpt-6.1-sol-20261001", "gpt-6.1-terra", "gpt-6-luna", "gpt-6-astra", "gpt-audio-1.5"],
                PlaygroundProviderKind.Gemini => ["gemini-4-argon", "gemini-3.8-flash", "gemini-3.8-flash-lite", "gemini-3.5-pro", "gemini-2.5-flash", "gemini-embedding-001"],
                PlaygroundProviderKind.Claude => ["claude-haiku-4-5", "claude-sonnet-4-6", "claude-opus-4-8", "claude-fable-5", "claude-sonnet-4-5"],
                PlaygroundProviderKind.Grok => ["grok-4", "grok-4-fast", "grok-3-mini", "grok-2-image"],
                _ => ["deepseek-flash", "deepseek-pro"]
            };
            return ids.Select(id => new PlaygroundModel(id, "[示例] " + id, Capabilities(id, endpoint))).ToImmutableArray();
        }
        public async IAsyncEnumerable<PlaygroundProviderEvent> SendAsync(PlaygroundProviderRequest request, [EnumeratorCancellation] CancellationToken cancellation)
        {
            string question = request.Messages.LastOrDefault(x => x.Role == "user")?.Text ?? "";
            bool hasImage = request.Messages.Any(x => !x.Attachments.IsDefaultOrEmpty);
            bool slow = question.Contains("慢慢聊", StringComparison.Ordinal), fail = question.Contains("连接暂时中断", StringComparison.Ordinal), longer = question.Contains("展开聊聊", StringComparison.Ordinal);
            string text = "## 音乐给日常留出的时间\n\n把《暮色邮局》和《星潮留声机》放在一起聊，声音有时比对白更能说明一个场景的呼吸。\n\n"
                + "- **音乐的进入**可以改变一句普通对白留下的情绪。\n- **环境音的延续**让停顿仍然属于这个场景。\n- **阅读的节奏**不必总向前走，也可以停在一段日常里。\n\n"
                + "```text\n声音 → 场景的情绪\n停顿 → 阅读的呼吸\n```\n\n";
            if (hasImage) text = "[离线图片演示]\n\n图片快照已随本轮请求进入图片输入字段。演示服务不执行真实视觉识别；真实识别效果需要配置支持图片的模型后验证。\n\n";
            if (longer || slow) text += string.Join("\n\n", Enumerable.Range(1, longer ? 30 : 15).Select(i => $"### 观察 {i}：让场景自己说话\n\n可以先回想一段对白暂停的地方。音乐是否还在继续，背景里有没有风声或脚步声？不急着解释情节，先描述这些声音带来的距离感，再比较两部作品留下的不同余韵。"));
            for (int at = 0; at < text.Length; at += 24)
            {
                await Task.Delay(slow ? 90 : 8, cancellation);
                if (fail && at >= 120) { yield return new(PlaygroundProviderEventKind.Error) { Failure = new(PlaygroundFailureKind.Network, "[合成故障] 流式连接中断，未自动重新发送。", true) }; yield break; }
                yield return new(PlaygroundProviderEventKind.Text) { Text = text.Substring(at, Math.Min(24, text.Length - at)) };
            }
            yield return new(PlaygroundProviderEventKind.Usage) { Usage = new(125, text.Length / 2, Searches: 0) };
            yield return new(PlaygroundProviderEventKind.Completed) { StopReason = "stop" };
        }
        public Task<PlaygroundResearchResult> SearchAsync(PlaygroundPublicSearchRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var retention = kind == PlaygroundProviderKind.Gemini ? PlaygroundRetention.SessionOnly : PlaygroundRetention.Portable;
            return Task.FromResult(new PlaygroundResearchResult("[合成公开检索答复] 这是离线测试内容，不是真实 Google 或其他服务的搜索结果。 [[fixture_search]]",
                [new("fixture_search", PlaygroundSourceKind.Web, "合成公开资料") { Url = "https://example.com/playground-fixture", Retention = retention }], new(30, 60, Searches: 1))
                { Retention = retention, SearchSuggestionsHtml = kind == PlaygroundProviderKind.Gemini ? "<div style='padding:12px;font:14px sans-serif;color:#58697b;background:#edf3fa;border-radius:8px'>离线合成搜索建议 · <a href='https://example.com/playground-fixture'>示例来源</a></div>" : null });
        }
    }
}
