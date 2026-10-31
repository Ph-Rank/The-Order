using System.ComponentModel;
using System.Diagnostics;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

[Collection("Launch processes")]
public sealed class GuideHelperProcessTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BackgroundHelperTurnsOffItsOwnStartupFeedbackAndPreservesArguments(bool background)
    {
        await using var fixture = new LaunchFixture();
        string[] extra = ["日本語 攻略", "quote\"value", @"trailing\", ""];
        string[] arguments = [fixture.Reports, "empty", "helper", .. extra];
        if (background)
        {
            using var process = WindowsGuideHelperProcess.Start(fixture.Exe, arguments);
            Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
        }
        else
        {
            var start = new ProcessStartInfo(fixture.Exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
        }
        var report = await fixture.Report("helper");
        uint flags = report.GetProperty("startupFlags").GetUInt32();
        Assert.Equal(background, (flags & 0x80) != 0); Assert.Equal(0u, flags & 0x40);
        Assert.Equal(extra, report.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()));
    }
    [Fact]
    public async Task OwnedHelperRetainsTimeoutAndTerminationBehavior()
    {
        await using var fixture = new LaunchFixture();
        using var process = WindowsGuideHelperProcess.Start(fixture.Exe, [fixture.Reports, "game", "helper"]);
        await fixture.Report("helper"); Assert.False(process.WaitForExit(0));
        process.Kill(); Assert.True(process.WaitForExit(10000)); Assert.Equal(1, process.ExitCode);
    }
    [Fact]
    public async Task MissingHelperFailsWithoutStartingAnotherProgram()
    {
        await using var fixture = new LaunchFixture();
        Assert.Throws<Win32Exception>(() => WindowsGuideHelperProcess.Start(Path.Combine(fixture.Game, "missing-helper.exe"), []));
        Assert.Empty(Directory.GetFiles(fixture.Reports, "*.json"));
    }
}
