using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private readonly record struct OptionsOwner(Guid? ConversationId, int Navigation);
    private Task optionsSaveTail = Task.CompletedTask;
    private OptionsOwner CaptureOptionsOwner() => new(selected, navigationGeneration);
    private bool IsCurrentOptionsOwner(OptionsOwner owner) => !disposed && owner.ConversationId == selected
        && owner.Navigation == navigationGeneration
        && (owner.ConversationId is null || service.Snapshot.Conversations.Any(c => c.Id == owner.ConversationId));

    private PlaygroundConversationOptions OptionsFor(Guid? conversation)
    {
        if (conversation is not { } id) return emptyDraft;
        if (drafts.TryGetValue(id, out var value)) return value;
        var saved = service.Snapshot.Conversations.FirstOrDefault(c => c.Id == id);
        if (saved is null) return emptyDraft;
        return drafts[id] = saved.Options;
    }
    private void StoreOptions(Guid? conversation, PlaygroundConversationOptions options)
    {
        if (conversation is not { } id) emptyDraft = options;
        else if (service.Snapshot.Conversations.Any(c => c.Id == id)) drafts[id] = options;
    }

    private Task SaveOptionsAsync(Guid? conversation, PlaygroundConversationOptions options)
    {
        if (conversation is not { } id || disposed) return Task.CompletedTask;
        var previous = optionsSaveTail;
        return optionsSaveTail = SaveAfterAsync();

        async Task SaveAfterAsync()
        {
            // The owner and value are captured before any await. A slow save
            // cannot read the next selected conversation or overtake a newer edit.
            try { await previous; } catch { /* Its caller retains and reports that draft. */ }
            if (disposed) return;
            var saved = service.Snapshot.Conversations.FirstOrDefault(c => c.Id == id);
            if (saved is not null && saved.Options != options)
                await service.UpdateConversationAsync(id, options, lifetime.Token);
        }
    }
    private async Task ApplyOptionsAsync()
    {
        var owner = CaptureOptionsOwner(); var options = OptionsFor(owner.ConversationId);
        await SaveOptionsAsync(owner.ConversationId, options);
        if (IsCurrentOptionsOwner(owner)) UpdateConnectionStatus();
    }
    private void SaveOutgoingOptions()
    {
        if (Current is { } current && current.Options != OptionsFor(current.Id))
            _ = SaveOutgoingOptionsAsync(current.Id, OptionsFor(current.Id));
    }
    private async Task SaveOutgoingOptionsAsync(Guid id, PlaygroundConversationOptions options)
    {
        try { await SaveOptionsAsync(id, options); }
        catch (OperationCanceledException) { }
        catch
        {
            // The target conversation is already displayed. Keep the outgoing
            // draft in its own slot instead of leaving a new row with old controls.
            if (!disposed) ShowError("会话设置暂未保存，草稿已保留；可以稍后重试。");
        }
    }
    private void InvalidateOptionsNavigation()
    {
        CancelModelLoading(); DismissModelPicker();
        credentialGeneration++; credentialAvailable = false; credentialBinding = "";
    }
    private void DismissModelPicker()
    {
        dismissModelPicker?.Invoke(); modelPickerOptions = null;
        modelPickerCredentialAvailable = modelPickerCheckingCredential = false; modelPickerCredentialBinding = null;
    }
}
