using TheOrder.Application.Playground;
using System.Collections.Immutable;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    // Called only by the Test-profile command gate in MainWindow. These use the
    // same native view actions as the composer and session list.
    internal Task SelectForFixtureAsync(Guid id) => SelectAsync(id);
    internal Task CreateForFixtureAsync(bool temporary) => CreateAsync(temporary);
    internal Task SendForFixtureAsync(string text) { composer.Text = text; return SendAsync(); }
    internal void RefreshForFixture() => Refresh(forceConversations: true, forceOptions: true);
    internal async Task OptionsForFixtureAsync(PlaygroundConversationOptions options)
    {
        Draft = options; await ApplyOptionsAsync(); SyncOptions(); await RefreshCredentialAsync(); Refresh();
    }
    internal Task ModelsForFixtureAsync() => LoadModelsAsync();
    internal async Task ControlsForFixtureAsync(bool search, bool restrictSpoilers, string instructions, string reasoning)
    {
        if (instructions.Length > 8000 || reasoning is not ("low" or "medium" or "high")) throw new InvalidDataException();
        searchMode.IsOn = search; spoilerMode.IsOn = restrictSpoilers;
        effort.SelectedIndex = reasoning switch { "low" => 0, "medium" => 1, _ => 2 };
        Draft = Draft with { SystemInstructions = instructions };
        await ApplyOptionsAsync(); SyncOptions(); Refresh();
    }
    private object? sessionSwitchEvidence;
    internal async Task SessionOptionsForFixtureAsync(Guid first, Guid second)
    {
        if (service.IsGenerating || first == second || !service.Snapshot.Conversations.Any(c => c.Id == first)
            || !service.Snapshot.Conversations.Any(c => c.Id == second)) throw new InvalidDataException("Two idle synthetic conversations are required.");
        foreach (var kind in new[] { PlaygroundProviderKind.Gemini, PlaygroundProviderKind.OpenAI })
            if (!service.Capabilities(kind, Endpoint(kind), "fixture-" + kind.ToString().ToLowerInvariant()).Verified)
                throw new InvalidOperationException("Offline synthetic model capability required.");
        search.Text = ""; var steps = new List<object>();
        await SelectAsync(first);
        await OptionsForFixtureAsync(Draft with { Provider = PlaygroundProviderKind.Gemini, Endpoint = Endpoint(PlaygroundProviderKind.Gemini), Model = "fixture-gemini", ReasoningEffort = "low" });
        var oldOwner = CaptureOptionsOwner();
        Record(first, PlaygroundProviderKind.Gemini, "fixture-gemini", "low", 0);
        await SelectAsync(second);
        bool previousOwnerRejected = !IsCurrentOptionsOwner(oldOwner);
        await OptionsForFixtureAsync(Draft with { Provider = PlaygroundProviderKind.OpenAI, Endpoint = Endpoint(PlaygroundProviderKind.OpenAI), Model = "fixture-openai", ReasoningEffort = "high" });
        Record(second, PlaygroundProviderKind.OpenAI, "fixture-openai", "high", 2);
        await SelectAsync(first); await optionsSaveTail; Refresh(forceConversations: true, forceOptions: true);
        Record(first, PlaygroundProviderKind.Gemini, "fixture-gemini", "low", 0);
        await SelectAsync(second); await optionsSaveTail; Refresh(forceConversations: true, forceOptions: true);
        Record(second, PlaygroundProviderKind.OpenAI, "fixture-openai", "high", 2);
        await SelectAsync(first); await optionsSaveTail; Refresh(forceConversations: true, forceOptions: true);
        Record(first, PlaygroundProviderKind.Gemini, "fixture-gemini", "low", 0);
        if (!previousOwnerRejected) throw new InvalidOperationException("The previous conversation still owns asynchronous UI updates.");
        sessionSwitchEvidence = new { passed = true, previousOwnerRejected, steps = steps.ToArray(), synthetic = true, realApi = false };

        void Record(Guid expected, PlaygroundProviderKind provider, string model, string reasoning, int index)
        {
            var draft = OptionsFor(expected); var saved = service.Snapshot.Conversations.Single(c => c.Id == expected).Options;
            var models = modelCatalogues.GetValueOrDefault(OptionsBinding(draft))?.Models ?? [];
            string displayed = ModelDisplayName(model, models.FirstOrDefault(m => m.Id == model)?.Name);
            if (selected != expected || conversations.SelectedItem is not PlaygroundConversationItem row || row.Id != expected
                || draft.Provider != provider || saved.Provider != provider || draft.Model != model || saved.Model != model
                || draft.ReasoningEffort != reasoning || saved.ReasoningEffort != reasoning
                || currentModelProvider.Text != ProviderNames[(int)provider] || currentModelTitle.Text != displayed || effort.SelectedIndex != index)
                throw new InvalidOperationException("Saved, draft, list selection and displayed model/effort must identify the same conversation.");
            steps.Add(new { conversation = expected, draftProvider = draft.Provider.ToString(), draft.Model, draft.ReasoningEffort,
                savedProvider = saved.Provider.ToString(), savedModel = saved.Model, savedEffort = saved.ReasoningEffort,
                displayedProvider = currentModelProvider.Text, displayedModel = currentModelTitle.Text, displayedReasoningIndex = effort.SelectedIndex });
        }
    }
    private object? budgetBounds;
    internal void BoundsForFixture()
    {
        bool wasDirty = generationDirty; var sliders = new[] { generationOutput, generationContext, generationTimeout };
        var original = sliders.Select(s => s.Value).ToArray(); generationSync = true;
        try
        {
            foreach (var slider in sliders) slider.Value = slider.Minimum - 100;
            var minimum = sliders.Select(s => s.Value).ToArray();
            foreach (var slider in sliders) slider.Value = slider.Maximum + 1000000;
            var maximum = sliders.Select(s => s.Value).ToArray();
            budgetBounds = new { minimum, maximum, isSlider = sliders.All(s => s is Microsoft.UI.Xaml.Controls.Slider), editableTextFields = sliders.Any(s => Descendants(s).OfType<Microsoft.UI.Xaml.Controls.TextBox>().Any()) };
        }
        finally { for (int i = 0; i < sliders.Length; i++) sliders[i].Value = original[i]; generationSync = false; generationDirty = wasDirty; }
    }
    internal async Task GenerationForFixtureAsync(int output, int context, int timeout)
    {
        if (output is < 128 or > 65536 || context is < 2000 or > 1000000 || timeout is < 10 or > 1800 || service.IsGenerating) throw new InvalidDataException();
        generationOutput.Value = output; generationContext.Value = context; generationTimeout.Value = timeout;
        await SaveGenerationSettingsAsync();
    }
    internal Task AttachmentsForFixtureAsync(ImmutableArray<PlaygroundAttachment> snapshots) => EnqueueAttachmentsAsync(selected, snapshots);
    internal void PanelsForFixture(bool? left, bool? right)
    {
        if (left is { } sessionsOpen) SetPanelOpen(true, sessionsOpen);
        if (right is { } runSettingsOpen) SetPanelOpen(false, runSettingsOpen);
    }
    internal void SetPanelsForFixture(bool left, bool right) { SetPanelOpen(true, left); SetPanelOpen(false, right); }
    internal void SetReducedMotionForFixture(bool reduced) { fixtureReducedMotion = reduced; ArrangeWorkspace(); }
    internal Task SourceForFixtureAsync(string id)
    {
        var source = messageRows.SelectMany(x => x.Message.Sources).FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidDataException("Synthetic source not found.");
        return OpenSourceAsync(source);
    }
    internal object RenderingForFixture => new
    {
        renderedRows = Descendants(messages).OfType<PlaygroundMessageView>().Select(x => x.Evidence).ToArray(),
        advancedParameterVisible = effort.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
        controls = new { search = searchMode.IsOn, restrictSpoilers = spoilerMode.IsOn, reasoningIndex = effort.SelectedIndex, reasoningEnabled = effort.IsEnabled,
            instructionsLength = Draft.SystemInstructions.Length, savedInstructionsLength = Current?.Options.SystemInstructions.Length,
            savedSearch = Current?.Options.Search.ToString(), savedSpoiler = Current?.Options.Spoiler.ToString(), savedEffort = Current?.Options.ReasoningEffort,
            globalConsent = service.Snapshot.Settings.DiscussionConsent, rightConnectionButtons = Descendants(settings).OfType<Microsoft.UI.Xaml.Controls.Button>().Count(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "PlaygroundConfigure") },
        sessionOptions = new { selected, draftProvider = Draft.Provider.ToString(), draftModel = Draft.Model, draftEffort = Draft.ReasoningEffort,
            savedProvider = Current?.Options.Provider.ToString(), savedModel = Current?.Options.Model, savedEffort = Current?.Options.ReasoningEffort,
            displayedProvider = currentModelProvider.Text, displayedModel = currentModelTitle.Text, displayedReasoningIndex = effort.SelectedIndex,
            pendingSave = !optionsSaveTail.IsCompleted }, sessionSwitchEvidence,
        rightSidebarScroll = new { visibility = settingsScroll.VerticalScrollBarVisibility.ToString(), mode = settingsScroll.VerticalScrollMode.ToString(),
            offset = settingsScroll.VerticalOffset, extent = settingsScroll.ScrollableHeight, viewport = settingsScroll.ViewportHeight },
        budgetBounds,
        modelListVisible = modelPickerOpen,
        modelPicker = readModelPickerEvidence?.Invoke() ?? lastModelPickerEvidence,
        modelCount = CurrentModels.Length,
        modelIds = CurrentModels.Select(x => x.Id).ToArray(),
        generation = new { output = generationOutput.Value, context = generationContext.Value, timeout = generationTimeout.Value,
            savedOutput = service.Snapshot.Settings.MaximumOutputTokens, savedContext = service.Snapshot.Settings.MaximumContextCharacters,
            savedTimeout = service.Snapshot.Settings.TimeoutSeconds, generationDirty, inRightSidebar = Descendants(settings).Contains(generationOutput) },
        readable = settingsScroll.IsHitTestVisible && sessions.IsHitTestVisible && composer.IsEnabled,
        composerTextLength = composer.Text.Length,
        attachmentCount = DraftFiles.Length, imageCount = DraftFiles.Count(x => PlaygroundAttachmentPreview.IsImage(x.Snapshot)),
        attachmentNames = DraftFiles.Select(x => x.Snapshot.Name).ToArray(), sendWidth = send.ActualWidth, sendHeight = send.ActualHeight,
        addFileVisible = attach.Visibility == Microsoft.UI.Xaml.Visibility.Visible, promptPlaceholder = composer.PlaceholderText.Length
    };
}
