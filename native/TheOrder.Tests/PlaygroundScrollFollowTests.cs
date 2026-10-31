using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class PlaygroundScrollFollowTests
{
    [Fact]
    public void AcceptedOwnTurnRestoresLatestFromReadingHistory()
    {
        var follow = Ready(); follow.Suspend();
        var intent = follow.CaptureIntent()!.Value;
        Assert.False(follow.TryCapture(out _));
        Assert.True(follow.AcceptOwnTurn(intent));
        Assert.True(follow.TryCapture(out var ticket)); Assert.True(follow.IsCurrent(ticket));
    }

    [Fact]
    public void UserScrollAfterSendIntentWinsOverLateAdmission()
    {
        var follow = Ready(); var intent = follow.CaptureIntent()!.Value;
        follow.Suspend();
        Assert.False(follow.AcceptOwnTurn(intent)); Assert.False(follow.Following);
    }

    [Fact]
    public void SwitchingAwayAndBackRejectsTheOldAdmissionAndDeferredScroll()
    {
        var follow = Ready(); var conversation = follow.ConversationId;
        var intent = follow.CaptureIntent()!.Value; Assert.True(follow.TryCapture(out var ticket));
        follow.Select(Guid.NewGuid()); follow.Select(conversation); follow.SetLatest(ticket.LastMessageId);
        Assert.False(follow.AcceptOwnTurn(intent)); Assert.False(follow.IsCurrent(ticket));
    }

    [Fact]
    public void ANewLastMessageInvalidatesTheOldVirtualizedTarget()
    {
        var follow = Ready(); Assert.True(follow.TryCapture(out var previous));
        follow.SetLatest(Guid.NewGuid());
        Assert.False(follow.IsCurrent(previous)); Assert.True(follow.TryCapture(out var latest));
        Assert.True(follow.IsCurrent(latest));
    }

    [Fact]
    public void ExplicitHistoryJumpCancelsAlreadyQueuedFollow()
    {
        var follow = Ready(); Assert.True(follow.TryCapture(out var ticket));
        follow.Suspend();
        Assert.False(follow.IsCurrent(ticket)); Assert.False(follow.ObserveLayout(12000, 600));
    }

    [Fact]
    public void StreamingAndFinalLayoutGrowthKeepFollowingWithoutAnOffsetHeuristic()
    {
        var follow = Ready();
        Assert.True(follow.ObserveLayout(50, 600));
        Assert.True(follow.ObserveLayout(5000, 600)); // Stream grows beyond a viewport.
        Assert.True(follow.ObserveLayout(18000, 600)); // Final Markdown measures taller.
        Assert.False(follow.ObserveLayout(18000, 600));
        Assert.True(follow.TryCapture(out var ticket)); Assert.True(follow.IsCurrent(ticket));
    }

    [Fact]
    public void LayoutGrowthDoesNotResumeAnIntentionallyPausedReader()
    {
        var follow = Ready(); follow.ObserveLayout(500, 600); follow.Suspend();
        Assert.False(follow.ObserveLayout(12000, 600));
        Assert.False(follow.ObserveLayout(24000, 500)); Assert.False(follow.TryCapture(out _));
    }

    [Fact]
    public void ExplicitReturnToEndResumesButUnloadInvalidatesOldRequests()
    {
        var follow = Ready(); Assert.True(follow.TryCapture(out var previous));
        follow.Suspend(); follow.ResumeAtEnd();
        Assert.False(follow.IsCurrent(previous)); Assert.True(follow.TryCapture(out var resumed));
        var intent = follow.CaptureIntent()!.Value; follow.Invalidate();
        Assert.False(follow.IsCurrent(resumed)); Assert.False(follow.AcceptOwnTurn(intent));
        Assert.True(follow.Following);
    }

    private static PlaygroundScrollFollow Ready()
    {
        var follow = new PlaygroundScrollFollow(); follow.Select(Guid.NewGuid()); follow.SetLatest(Guid.NewGuid()); return follow;
    }
}
