using TheOrder.Domain;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class LocalReadingPickerFolderTests
{
    private static LibraryEntry Entry() => new("g", "g", null, null,
        [new("first", @"C:\synthetic\first", null, []), new("preferred", @"C:\synthetic\preferred", null, []), new("running", @"C:\synthetic\running", null, [])],
        "preferred", "未开始", null, false, [], [], "", 0, 0, [], "2026-10-30", null, null, null, new(false, null));
    [Fact] public void RunningInstallationWinsOverPreferredAndLibraryOrder()
    { Assert.Equal(@"C:\synthetic\running", LocalReadingPickerFolder.Select(Entry(), "running", _ => true)); }
    [Fact] public void PreferredInstallationIsUsedOutsideAnActiveGame()
    { Assert.Equal(@"C:\synthetic\preferred", LocalReadingPickerFolder.Select(Entry(), null, _ => true)); }
    [Fact] public void MissingFoldersFallBackToAnotherInstallationWithoutScanningFiles()
    { Assert.Equal(@"C:\synthetic\first", LocalReadingPickerFolder.Select(Entry(), "running", p => p.EndsWith("first", StringComparison.Ordinal))); }
    [Fact] public void WishlistOrUnavailableGameLeavesThePickerAtItsNormalFallback()
    { Assert.Null(LocalReadingPickerFolder.Select(Entry() with { Installations = [] }, null, _ => throw new InvalidOperationException())); Assert.Null(LocalReadingPickerFolder.Select(Entry(), null, _ => false)); }
}
