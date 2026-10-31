namespace TheOrder.Presentation;

public readonly record struct PlaygroundScrollIntent(Guid ConversationId, long Generation);
public readonly record struct PlaygroundScrollTicket(Guid ConversationId, Guid LastMessageId, long Generation);

// A reading decision must outlive changing/estimated item heights. Distance
// from the bottom is only used by the view after explicit user navigation.
public sealed class PlaygroundScrollFollow
{
    public Guid? ConversationId { get; private set; }
    public Guid? LastMessageId { get; private set; }
    public bool Following { get; private set; } = true;
    public long Generation { get; private set; }
    private double lastExtent = double.NaN, lastViewport = double.NaN;

    public void Select(Guid? conversation)
    {
        if (ConversationId == conversation) return;
        ConversationId = conversation; LastMessageId = null; Following = true; Generation++;
        lastExtent = lastViewport = double.NaN;
    }

    public PlaygroundScrollIntent? CaptureIntent() => ConversationId is { } conversation ? new PlaygroundScrollIntent(conversation, Generation) : null;

    public bool AcceptOwnTurn(PlaygroundScrollIntent intent)
    {
        if (intent.ConversationId != ConversationId || intent.Generation != Generation) return false;
        Following = true; Generation++; return true;
    }

    public bool SetLatest(Guid? message)
    {
        if (LastMessageId == message) return false;
        LastMessageId = message; return true;
    }

    public void Suspend() { Following = false; Generation++; }
    public void Invalidate() => Generation++;
    public void ResumeAtEnd()
    {
        if (Following) return;
        Following = true; Generation++;
    }

    public bool ObserveLayout(double extent, double viewport)
    {
        if (!double.IsFinite(extent) || !double.IsFinite(viewport) || extent < 0 || viewport <= 0) return false;
        bool changed = double.IsNaN(lastExtent) || Math.Abs(extent - lastExtent) > .5 || Math.Abs(viewport - lastViewport) > .5;
        lastExtent = extent; lastViewport = viewport;
        return changed && Following && LastMessageId is not null;
    }

    public bool TryCapture(out PlaygroundScrollTicket ticket)
    {
        if (Following && ConversationId is { } conversation && LastMessageId is { } message)
        { ticket = new(conversation, message, Generation); return true; }
        ticket = default; return false;
    }

    public bool IsCurrent(PlaygroundScrollTicket ticket) => Following && ticket.ConversationId == ConversationId
        && ticket.LastMessageId == LastMessageId && ticket.Generation == Generation;
}
