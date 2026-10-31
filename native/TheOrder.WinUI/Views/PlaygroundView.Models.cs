using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private sealed record ModelCatalogue(ImmutableArray<PlaygroundModel> Models, int RawCount);
    private readonly Dictionary<string, ModelCatalogue> modelCatalogues = [];
    private readonly HashSet<string> modelLoadAttempts = [];
    private readonly Dictionary<string, string> modelLoadErrors = [];
    private CancellationTokenSource? modelLoadCancellation;
    private string? pendingModelBinding;
    private int modelLoadGeneration;
    private int modelCredentialEpoch, handledModelCredentialEpoch;
    private PlaygroundConversationOptions? modelPickerOptions;
    private bool modelPickerCredentialAvailable, modelPickerCheckingCredential;
    private string? modelPickerCredentialBinding;
    private Action? dismissModelPicker;
    private static string OptionsBinding(PlaygroundConversationOptions options) => options.Provider + "|" + options.Endpoint;
    private PlaygroundConversationOptions CatalogueOptions => modelPickerOptions ?? Draft;
    private string ModelBinding => OptionsBinding(CatalogueOptions);
    private bool CatalogueCredentialAvailable => modelPickerOptions is not null
        ? modelPickerCredentialAvailable && modelPickerCredentialBinding == ModelBinding
        : credentialAvailable && credentialBinding == ModelBinding;
    private bool CanLoadModels => initialized && CatalogueCredentialAvailable && PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings);
    private ImmutableArray<PlaygroundModel> CurrentModels => modelCatalogues.GetValueOrDefault(ModelBinding)?.Models ?? [];
    private Action? refreshModelPicker;
    private string ModelCatalogueStatus => modelLoadErrors.GetValueOrDefault(ModelBinding) ?? (modelsLoading ? "正在读取模型目录…"
        : modelPickerOptions is not null && modelPickerCheckingCredential ? "正在读取连接状态…"
        : !PlaygroundAuthorizationPolicy.CanSend(service.Snapshot.Settings) ? "尚未授予全局发送授权。"
        : !CatalogueCredentialAvailable ? "此连接尚未保存 API Key。"
        : modelCatalogues.GetValueOrDefault(ModelBinding) is not { } catalogue ? "尚未读取此连接的模型目录。"
        : catalogue.Models.IsEmpty ? catalogue.RawCount == 0 ? "接口返回的模型目录为空。" : "目录中没有符合系列筛选的对话型号。" : "");

    private void CancelModelLoading()
    {
        if (modelsLoading && pendingModelBinding is { } pending) modelLoadAttempts.Remove(pending);
        pendingModelBinding = null;
        modelLoadGeneration++;
        modelLoadCancellation?.Cancel(); modelLoadCancellation = null; modelsLoading = false;
    }
    private void InvalidateModelCatalogues()
    {
        handledModelCredentialEpoch = Volatile.Read(ref modelCredentialEpoch);
        CancelModelLoading(); modelCatalogues.Clear(); modelLoadAttempts.Clear(); modelLoadErrors.Clear(); SyncModelCatalogue();
    }
    private void SyncModelCatalogue()
    {
        currentModelProvider.Text = ProviderNames[(int)Draft.Provider];
        var selectedModels = modelCatalogues.GetValueOrDefault(OptionsBinding(Draft))?.Models ?? [];
        currentModelTitle.Text = string.IsNullOrWhiteSpace(Draft.Model) ? "选择模型" : ModelDisplayName(Draft.Model, selectedModels.FirstOrDefault(x => x.Id == Draft.Model)?.Name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(currentModel, "选择模型：" + currentModelTitle.Text);
        refreshModelPicker?.Invoke();
    }
    private static string ModelDisplayName(string id, string? name)
    {
        bool sample = name?.StartsWith("[示例] ", StringComparison.Ordinal) == true;
        string supplied = sample ? name![5..] : name ?? id;
        if (supplied != id) return name ?? id;
        var gpt = System.Text.RegularExpressions.Regex.Match(id, @"^gpt-(\d+(?:\.\d+)?)-(luna|terra|sol|astra)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        string label = gpt.Success ? "GPT-" + gpt.Groups[1].Value + " " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(gpt.Groups[2].Value)
            : id is "deepseek-flash" ? "DeepSeek Flash" : id is "deepseek-v4-pro" ? "DeepSeek V4 Pro" : id;
        return sample ? "[示例] " + label : label;
    }
    private async Task LoadModelsAsync()
    {
        if (modelsLoading || !CanLoadModels) return;
        var owner = CaptureOptionsOwner(); var options = CatalogueOptions; bool pickerRequest = modelPickerOptions is not null;
        string binding = ModelBinding; int request = ++modelLoadGeneration;
        int credentialEpoch = Volatile.Read(ref modelCredentialEpoch);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        modelLoadCancellation = cancellation; modelsLoading = true; pendingModelBinding = binding;
        // A metadata refresh must not blank a still-authorized cached list.
        // Credential changes already invalidate every catalogue separately.
        modelLoadAttempts.Add(binding); modelLoadErrors.Remove(binding);
        SyncModelCatalogue(); UpdateConnectionStatus();
        bool CurrentRequest() => IsCurrentOptionsOwner(owner) && request == modelLoadGeneration
            && credentialEpoch == Volatile.Read(ref modelCredentialEpoch) && binding == ModelBinding && CanLoadModels
            && pickerRequest == (modelPickerOptions is not null);
        try
        {
            var raw = await service.ListModelsAsync(options.Provider, options.Endpoint, cancellation.Token);
            if (!CurrentRequest()) return;
            var latest = PlaygroundModelSelection.SelectLatest(options.Provider, raw);
            modelCatalogues[binding] = new(latest, raw.Length);
            if (!pickerRequest && string.IsNullOrWhiteSpace(Draft.Model) && !latest.IsEmpty)
            {
                var preferred = latest.FirstOrDefault(m => options.Provider == PlaygroundProviderKind.OpenAI && m.Id.Contains("sol", StringComparison.OrdinalIgnoreCase)
                    || options.Provider == PlaygroundProviderKind.Gemini && m.Id.Contains("argon", StringComparison.OrdinalIgnoreCase)) ?? latest[0];
                Draft = Draft with { Model = preferred.Id, ReasoningEffort = DefaultEffort(Draft.Provider, Draft.Endpoint, preferred.Id) };
                UpdateCapabilities(); if (owner.ConversationId is { } conversation) _ = SaveOutgoingOptionsAsync(conversation, Draft);
            }
        }
        catch (OperationCanceledException) { if (CurrentRequest() && !lifetime.IsCancellationRequested) modelLoadErrors[binding] = "模型目录读取超时，可点击刷新重试。"; }
        catch (PlaygroundProviderException error) { if (CurrentRequest()) modelLoadErrors[binding] = error.Failure.Message + " 可点击刷新重试。"; }
        catch { if (CurrentRequest()) modelLoadErrors[binding] = "模型目录未能读取；可刷新重试或手动填写 ID。"; }
        finally
        {
            if (request == modelLoadGeneration)
            {
                modelLoadCancellation = null; modelsLoading = false; pendingModelBinding = null;
                if (!disposed) { SyncModelCatalogue(); UpdateConnectionStatus(); UpdateActions(); }
            }
        }
    }
}
