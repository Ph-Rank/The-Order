using System.Collections.Immutable;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;
using TheOrder.Application;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

// One launch-scoped projection of the SAME chat service and profile. Native
// renderers get text, public model labels and transient pixels, never API keys.
internal sealed class WindowsGameChat : IDisposable
{
    private readonly MemoryMappedFile mapping;
    internal readonly MemoryMappedViewAccessor View;
    private readonly Func<CancellationToken, Task<PlaygroundService>> factory;
    private readonly Func<bool> current;
    private readonly CancellationTokenSource lifetime;
    private readonly object gate = new();
    private readonly string key, title;
    private PlaygroundService? service;
    private Guid conversation;
    private sealed record ModelChoice(PlaygroundProviderKind Provider, string Endpoint, string Id, string Label);
    private readonly List<ModelChoice> models = [];
    private Task pending = Task.CompletedTask;
    private Task discovery = Task.CompletedTask;
    private CancellationTokenSource? turn;
    private bool wasBusy;
    private bool catalogDirty;
    private int catalogGeneration;
    private bool disposed, dirty = true, loading;
    private int sequence, request;
    private string status = "在主程序中配置连接后，即可在这里对话。";
    private string? awaitingInput;
    private long published;
    public WindowsGameChat(string name, string key, string title, Func<CancellationToken, Task<PlaygroundService>> factory,
        Func<bool> current, CancellationToken cancellation)
    {
        this.key = key; this.title = title; this.factory = factory; this.current = current;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        mapping = MemoryMappedFile.CreateNew(name + ".chat", GameChatWire.MaximumBytes, MemoryMappedFileAccess.ReadWrite);
        View = mapping.CreateViewAccessor(); View.Write(0, GameChatWire.Magic); View.Write(4, GameChatWire.Version);
        View.Write(112, Environment.ProcessId); View.Write(44, 1); Publish();
    }
    public void Bind(int pid, long created) { lock (gate) { if (disposed) return; View.Write(96, pid); View.Write(104, created); View.Write(40, 0); View.Write(48, 0); View.Write(52, 0); } }
    public void Tick(bool visible)
    {
        lock (gate)
        {
            if (disposed) return;
            if (catalogDirty && service is not null && discovery.IsCompleted)
            { catalogDirty = false; discovery = DiscoverAsync(service); }
            if (visible && !loading && service is null) { loading = true; pending = OpenAsync(); }
            int next = View.ReadInt32(16);
            if (next != request)
            {
                int command = View.ReadInt32(24), argument = View.ReadInt32(28), length = View.ReadInt32(32);
                string text = "";
                if (length is >= 0 and < GameChatWire.InputBytes)
                { byte[] bytes = new byte[length]; View.ReadArray(GameChatWire.Input, bytes, 0, length); try { text = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { command = 0; } }
                else command = 0;
                if (View.ReadInt32(16) != next) return;
                request = next; View.Write(20, next);
                if (command == 2) { turn?.Cancel(); if (service?.ActiveConversationId == conversation) service.Stop(); dirty = true; }
                else if (command == 1 && pending.IsCompleted && text.Trim().Length > 0) pending = SendAsync(text.Trim());
                else if (command is >= 3 and <= 6 && pending.IsCompleted) pending = ChangeAsync(command, argument);
            }
            bool busy = !pending.IsCompleted || service?.ActiveConversationId == conversation && service.IsGenerating;
            if (busy != wasBusy) { dirty = true; wasBusy = busy; }
            if (dirty && Environment.TickCount64 - published > 90) Publish();
        }
    }
    private async Task OpenAsync()
    {
        try
        {
            var chat = await factory(lifetime.Token).ConfigureAwait(false);
            await chat.InitializeAsync(lifetime.Token).ConfigureAwait(false); Check();
            service = chat; chat.Changed += Changed;
            var existing = chat.Snapshot.Conversations.FirstOrDefault(c => c.Options.GameOverlayKey == key);
            if (existing is null)
            {
                conversation = await chat.CreateConversationAsync(cancellation: lifetime.Token).ConfigureAwait(false);
                var options = chat.Snapshot.Conversations.First(c => c.Id == conversation).Options;
                await chat.UpdateConversationAsync(conversation, options with { GameOverlayKey = key }, lifetime.Token).ConfigureAwait(false);
                string caption = "游戏中 · " + string.Concat(title.Where(c => !char.IsControl(c)));
                await chat.RenameConversationAsync(conversation, caption[..Math.Min(120, caption.Length)], lifetime.Token).ConfigureAwait(false);
            }
            else conversation = existing.Id;
            var selected = chat.Snapshot.Conversations.First(c => c.Id == conversation).Options;
            if (existing is null || string.IsNullOrWhiteSpace(selected.Model))
            {
                var previous = chat.Snapshot.Conversations.Where(c => c.Id != conversation && !c.Temporary && !string.IsNullOrWhiteSpace(c.Options.Model)).OrderByDescending(c => c.UpdatedAt).FirstOrDefault();
                if (previous is not null) { selected = previous.Options with { GameOverlayKey = key }; await chat.UpdateConversationAsync(conversation, selected, lifetime.Token).ConfigureAwait(false); }
            }
            if (!string.IsNullOrWhiteSpace(selected.Model)) AddModel(selected.Provider, selected.Endpoint, selected.Model, selected.Model);
            foreach (var connection in chat.Snapshot.Settings.Connections)
                if (!string.IsNullOrWhiteSpace(connection.Model)) AddModel(connection.Provider, connection.Endpoint, connection.Model, connection.Model);
            Check(); status = "";
            discovery = DiscoverAsync(chat);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { status = "对话未就绪，请先在主程序中配置连接与授权。"; }
        finally { lock (gate) { loading = false; dirty = true; } }
    }
    private async Task DiscoverAsync(PlaygroundService chat)
    {
        int generation; lock (gate) generation = catalogGeneration;
        try
        {
            foreach (var connection in chat.Snapshot.Settings.Connections)
            {
                Check();
                try
                {
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); budget.CancelAfter(TimeSpan.FromSeconds(5));
                    var latest = PlaygroundModelSelection.SelectLatest(connection.Provider,
                        await chat.ListModelsAsync(connection.Provider, connection.Endpoint, budget.Token).ConfigureAwait(false));
                    lock (gate)
                    {
                        foreach (var model in latest)
                            AddModel(connection.Provider, connection.Endpoint, model.Id, model.Name, generation);
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException && !lifetime.IsCancellationRequested) { /* Configured model remains selectable. */ }
            }
        }
        catch (OperationCanceledException) { }
    }
    private void AddModel(PlaygroundProviderKind provider, string endpoint, string id, string label, int? generation = null)
    {
        lock (gate)
        {
            if (disposed || generation is { } expected && expected != catalogGeneration) return;
            int index = models.FindIndex(m => m.Provider == provider && m.Endpoint == endpoint && m.Id == id);
            if (index >= 0) models[index] = models[index] with { Label = label };
            else if (models.Count < GameChatWire.MaxModels) models.Add(new(provider, endpoint, id, label));
            dirty = true;
        }
    }
    private async Task SendAsync(string text)
    {
        turn?.Dispose(); turn = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = turn.Token;
        try
        {
            Check(); if (service is null || conversation == Guid.Empty) throw new InvalidOperationException();
            if (service.IsGenerating) { status = "另一段对话正在回复，请稍候。"; return; }
            var options = service.Snapshot.Conversations.FirstOrDefault(c => c.Id == conversation)?.Options ?? throw new InvalidOperationException();
            if (string.IsNullOrWhiteSpace(options.Model)) { status = "请先在主程序连接模型，然后在这里选择型号。"; return; }
            bool image = View.ReadInt32(44) != 0;
            ImmutableArray<PlaygroundAttachment> attachments = [];
            if (image)
            {
                if (!service.Capabilities(options.Provider, options.Endpoint, options.Model).ImageInput)
                { status = "当前型号不支持图片，请更换模型或关闭读取画面。"; return; }
                status = "正在读取当前游戏画面…"; Mark();
                attachments = [await CaptureAsync(token).ConfigureAwait(false)];
            }
            Check(); status = ""; Mark();
            token.ThrowIfCancellationRequested();
            awaitingInput = text;
            await service.SendAsync(conversation, text, attachments, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { status = "已停止。"; }
        catch (PlaygroundProviderException e) { status = e.Failure.Message; }
        catch (Exception e) when (e is not OutOfMemoryException) { status = e is GameFrameUnavailableException ? "未能取得游戏画面；本轮尚未发送。可关闭读取画面后发送文字。" : "未能发送，请检查主程序中的连接、模型与授权。"; }
        finally { awaitingInput = null; Mark(); }
    }
    private async Task ChangeAsync(int command, int argument)
    {
        try
        {
            Check(); if (service is null || conversation == Guid.Empty || service.IsGenerating) return;
            var options = service.Snapshot.Conversations.FirstOrDefault(c => c.Id == conversation)?.Options ?? throw new InvalidOperationException();
            if (command == 3)
            {
                ModelChoice model;
                lock (gate) { if (argument < 0 || argument >= models.Count) return; model = models[argument]; }
                var efforts = service.Capabilities(model.Provider, model.Endpoint, model.Id).ReasoningEfforts;
                var effort = options.ReasoningEffort is { } previous && efforts.Contains(previous)
                    ? previous : efforts.Contains("high") ? "high" : efforts.FirstOrDefault();
                options = options with { Provider = model.Provider, Endpoint = model.Endpoint, Model = model.Id, ReasoningEffort = effort };
            }
            else if (command == 4) options = options with { ReasoningEffort = argument switch { 0 => "low", 1 => "medium", _ => "high" } };
            else if (command == 5) options = options with { Search = argument == 0 ? PlaygroundSearchMode.Off : PlaygroundSearchMode.Smart };
            else if (command == 6) options = options with { Spoiler = argument == 0 ? PlaygroundSpoilerMode.Full : PlaygroundSpoilerMode.LibraryStatus };
            await service.UpdateConversationAsync(conversation, options, lifetime.Token).ConfigureAwait(false); status = "";
        }
        catch (Exception e) when (e is not OutOfMemoryException) { status = "设置未能保存。"; }
        finally { Mark(); }
    }
    private async Task<PlaygroundAttachment> CaptureAsync(CancellationToken token)
    {
        int requested, pid; long created; long began = Environment.TickCount64;
        lock (gate) { Check(); pid = View.ReadInt32(96); created = View.ReadInt64(104); requested = View.ReadInt32(48) + 1; View.Write(56, 0); View.Write(48, requested); }
        try
        {
        while (Environment.TickCount64 - began < 3500)
        {
            await Task.Delay(35, token).ConfigureAwait(false); Check();
            byte[]? pixels = null; int width = 0, height = 0;
            lock (gate)
            {
                if (View.ReadInt32(96) != pid || View.ReadInt64(104) != created) throw new OperationCanceledException();
                if (View.ReadInt32(52) != requested) continue;
                width = View.ReadInt32(60); height = View.ReadInt32(64); int length = View.ReadInt32(68);
                if (View.ReadInt32(56) != 1 || width is < 1 or > 1600 || height is < 1 or > 1600 || length != (long)width * height * 4 || length > GameChatWire.PixelBytes || View.ReadInt64(88) < began) throw new GameFrameUnavailableException();
                pixels = new byte[length]; View.ReadArray(GameChatWire.Pixels, pixels, 0, length);
                View.WriteArray(GameChatWire.Pixels, new byte[length], 0, length); View.Write(68, 0);
            }
            try { return await WindowsGameFrameEncoder.EncodeAsync(pixels, width, height, token).ConfigureAwait(false); }
            finally { Array.Clear(pixels); }
        }
        throw new GameFrameUnavailableException();
        }
        finally
        {
            lock (gate)
            {
                if (View.ReadInt32(48) == requested)
                {
                    View.Write(48, requested + 1); Thread.MemoryBarrier(); View.Write(52, requested + 1);
                    SpinWait.SpinUntil(() => View.ReadInt32(128) == 0, TimeSpan.FromMilliseconds(100));
                    View.Write(52, requested + 1);
                    int length = View.ReadInt32(68); if (length is > 0 and <= GameChatWire.PixelBytes) View.WriteArray(GameChatWire.Pixels, new byte[length], 0, length);
                    View.Write(68, 0);
                }
            }
        }
    }
    private void Check() { lifetime.Token.ThrowIfCancellationRequested(); if (!current()) throw new OperationCanceledException(); }
    private void Changed(PlaygroundChange change)
    {
        lock (gate)
        {
            if (disposed) return;
            if (change.Kind is "settings" or "credentials") { catalogGeneration++; models.Clear(); catalogDirty = true; }
            if (change.ConversationId == conversation && change.Kind == "started" && awaitingInput is { } sent)
            {
                int length = View.ReadInt32(84);
                if (length is >= 0 and < GameChatWire.InputBytes)
                {
                    byte[] bytes = new byte[length]; View.ReadArray(GameChatWire.Draft, bytes, 0, length);
                    if (Encoding.UTF8.GetString(bytes).Trim() == sent) { View.Write(80, View.ReadInt32(80) + 1); View.Write(84, 0); Thread.MemoryBarrier(); View.Write(80, View.ReadInt32(80) + 1); }
                }
                awaitingInput = null;
            }
            dirty = true;
        }
    }
    private void Mark() { lock (gate) dirty = true; }
    private void Publish()
    {
        var snapshot = service?.Snapshot; var options = snapshot?.Conversations.FirstOrDefault(c => c.Id == conversation)?.Options;
        var lines = snapshot?.Messages.Where(m => m.ConversationId == conversation).TakeLast(32).Select(m =>
        {
            string text = m.Text;
            var used = new List<PlaygroundSource>();
            text = Regex.Replace(text, @"\[\[([A-Za-z0-9_-]+)\]\]", match => { var source = m.Sources.FirstOrDefault(s => s.Id == match.Groups[1].Value); if (source is null) return ""; int index = used.FindIndex(s => s.Id == source.Id); if (index < 0) { index = used.Count; used.Add(source); } return "[" + (index + 1) + "]"; });
            if (used.Count > 0) text += "\n\n" + string.Join("\n", used.Select((s, i) => (i + 1) + ". " + s.Title + (s.Kind == PlaygroundSourceKind.Web ? "  " + s.Url : "")));
            return new GameChatLine(m.Role, text, m.Error ?? (m.Status == PlaygroundMessageStatus.Generating ? "正在回复…" : ""));
        }).ToArray() ?? [];
        bool busy = !pending.IsCompleted || service?.ActiveConversationId == conversation && service.IsGenerating;
        var state = new GameChatState(status, options?.Model ?? "选择模型", options?.ReasoningEffort ?? "high", busy,
            options is not null && service!.Capabilities(options.Provider, options.Endpoint, options.Model).ImageInput,
            options?.Search != PlaygroundSearchMode.Off, options?.Spoiler != PlaygroundSpoilerMode.Full,
            models.Select(m => m.Provider + " · " + m.Label).ToArray(), lines);
        var bytes = GameChatWire.Encode(state);
        View.Write(8, ++sequence); Thread.MemoryBarrier(); View.WriteArray(GameChatWire.State, bytes, 0, bytes.Length); View.Write(12, bytes.Length); Thread.MemoryBarrier(); View.Write(8, ++sequence);
        published = Environment.TickCount64; dirty = false;
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; lifetime.Cancel(); if (service is not null) { service.Changed -= Changed; if (service.ActiveConversationId == conversation) service.Stop(); } View.Write(0, 0); }
        _ = ReleaseAsync();
    }
    private async Task ReleaseAsync() { try { await Task.WhenAll(pending, discovery).ConfigureAwait(false); } catch { } lock (gate) { turn?.Dispose(); View.Dispose(); mapping.Dispose(); lifetime.Dispose(); } }
    private sealed class GameFrameUnavailableException : Exception;
}
