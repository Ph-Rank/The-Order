using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application.Playground;

namespace TheOrder.WinUI.Views;

public sealed partial class PlaygroundView
{
    private sealed record AttachmentDraft(Guid Id, PlaygroundAttachment Snapshot);
    private readonly Dictionary<Guid, ImmutableArray<AttachmentDraft>> attachmentDrafts = [];
    private ImmutableArray<AttachmentDraft> unnamedAttachments = [];
    private ImmutableArray<AttachmentDraft> renderedAttachments;
    private readonly TagFlowPanel draftFiles = new() { HorizontalSpacing = 6, VerticalSpacing = 6, Visibility = Visibility.Collapsed };
    private readonly Button attach = new() { Content = PlaygroundAppearance.Icon("\uE710", 18), Width = 32, Height = 32, MinWidth = 32, MinHeight = 32,
        Padding = new Thickness(0), CornerRadius = new CornerRadius(16), Visibility = Visibility.Collapsed };
    private Func<CancellationToken, Task<ImmutableArray<PlaygroundAttachment>>>? pickAttachments;
    private bool pickingAttachments;
    public Func<CancellationToken, Task<ImmutableArray<PlaygroundAttachment>>>? PickAttachments
    {
        get => pickAttachments;
        set { pickAttachments = value; attach.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible; UpdateActions(); }
    }
    private ImmutableArray<AttachmentDraft> DraftFiles => FilesFor(selected);
    private ImmutableArray<AttachmentDraft> FilesFor(Guid? id) => id is { } conversation ? attachmentDrafts.GetValueOrDefault(conversation, []) : unnamedAttachments;
    private void StoreFiles(Guid? id, ImmutableArray<AttachmentDraft> files)
    {
        if (id is { } conversation) { if (files.IsEmpty) attachmentDrafts.Remove(conversation); else attachmentDrafts[conversation] = files; }
        else unnamedAttachments = files;
        if (selected == id) { RenderDraftFiles(); UpdateActions(); }
    }
    private async Task PickFilesAsync()
    {
        if (pickingAttachments || pickAttachments is not { } picker || !initialized) return;
        var origin = selected; pickingAttachments = true; UpdateActions();
        try
        {
            var files = await picker(lifetime.Token);
            if (disposed || files.IsDefaultOrEmpty) return;
            await EnqueueAttachmentsAsync(origin, files);
        }
        finally { pickingAttachments = false; if (!disposed) UpdateActions(); }
    }
    private async Task EnqueueAttachmentsAsync(Guid? origin, ImmutableArray<PlaygroundAttachment> files)
    {
        if (disposed || files.IsDefaultOrEmpty) return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (origin is { } id && !service.Snapshot.Conversations.Any(x => x.Id == id)) return;
            var current = FilesFor(origin);
            if (files.Length > 4 || current.Length + files.Length > 4) { ShowError("最多添加 4 个附件。本次未添加，原有草稿已保留。"); return; }
            var snapshots = current.Select(x => x.Snapshot).Concat(files).ToImmutableArray();
            try { await Task.Run(() => PlaygroundAttachmentRules.Validate(snapshots), lifetime.Token); }
            catch (ArgumentException error) { if (!disposed) ShowError(error.Message + " 原有草稿已保留。"); return; }
            if (disposed || origin is { } target && !service.Snapshot.Conversations.Any(x => x.Id == target)) return;
            if (FilesFor(origin) != current) continue;
            StoreFiles(origin, current.AddRange(files.Select(x => new AttachmentDraft(Guid.NewGuid(), x)))); return;
        }
        ShowError("附件草稿刚有变化，请重新添加；原有内容保持不变。");
    }
    private void RenderDraftFiles()
    {
        var files = DraftFiles; if (files == renderedAttachments) return; renderedAttachments = files;
        draftFiles.Children.Clear(); draftFiles.Visibility = files.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        draftFiles.Margin = files.IsEmpty ? new Thickness(0) : new Thickness(0, 0, 0, 8);
        foreach (var file in files)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            var preview = AttachmentPreviewButton(file.Snapshot); preview.Click += async (_, _) => await RunAsync(() => PlaygroundAttachmentPreview.ShowAsync(XamlRoot, PaletteTheme, file.Snapshot));
            var remove = new Button { Content = PlaygroundAppearance.Icon("\uE711", 12), Width = 26, Height = 28, MinWidth = 26, MinHeight = 28, Padding = new Thickness(0) };
            PlaygroundAppearance.Apply(remove, "PGIconButton"); PlaygroundAppearance.Tip(remove, "移除 " + file.Snapshot.Name);
            remove.Click += (_, _) => StoreFiles(selected, DraftFiles.Where(x => x.Id != file.Id).ToImmutableArray());
            row.Children.Add(preview); row.Children.Add(remove); draftFiles.Children.Add(row);
        }
    }
    internal static Button AttachmentPreviewButton(PlaygroundAttachment file)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (PlaygroundAttachmentPreview.IsImage(file)) content.Children.Add(PlaygroundAttachmentPreview.Thumbnail(file));
        else content.Children.Add(PlaygroundAppearance.Icon("\uE8A5", 14));
        content.Children.Add(new TextBlock { Text = file.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Padding = new Thickness(7, 3, 7, 3), MinHeight = 28 };
        PlaygroundAppearance.Apply(button); PlaygroundAppearance.Tip(button, "查看 " + file.Name); return button;
    }
}
