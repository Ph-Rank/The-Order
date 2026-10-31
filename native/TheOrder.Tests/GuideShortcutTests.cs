using System.Runtime.InteropServices;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

[Collection("Launch processes")]
public sealed class GuideShortcutTests
{
    [Fact]
    public void OccupiedShortcutIsRejectedWithoutReleasingItsOwnerAndCanBeRetried()
    {
        const int id = 0x6174;
        foreach (int key in new[] { 0x4a, (2 << 16) | 0x4b })
        {
            if (!RegisterHotKey(IntPtr.Zero, id, 0x4000 | (uint)WindowsGuideHotkeys.Modifiers(key), (uint)WindowsGuideHotkeys.Key(key)))
            { Assert.False(WindowsGuideHotkeys.Check(key).Available); continue; }
            try
            {
                var blocked = WindowsGuideHotkeys.Check(key);
                Assert.False(blocked.Available); Assert.Contains("占用", blocked.Message);
                Assert.Throws<GuideHotkeyUnavailableException>(() => WindowsGuideHotkeys.RequireAvailable(key));
            }
            finally { Assert.True(UnregisterHotKey(IntPtr.Zero, id)); }
            Assert.True(WindowsGuideHotkeys.Check(key).Available);
            // The successful probe releases its own registration too.
            Assert.True(WindowsGuideHotkeys.Check(key).Available);
        }
    }
    [Theory]
    [InlineData(0x4a, "J")]
    [InlineData(0x4b, "K")]
    [InlineData(0x77, "F8")]
    [InlineData(0x6004b, "Ctrl+Shift+K")]
    [InlineData(0x10039, "Alt+9")]
    [InlineData(0xbf, "/")]
    public void CustomShortcutHasConsistentDisplayAndEncoding(int shortcut, string label)
    {
        Assert.True(WindowsGuideHotkeys.IsSupported(shortcut)); Assert.Equal(label, WindowsGuideHotkeys.Label(shortcut));
        Assert.Equal(shortcut, WindowsGuideHotkeys.Key(shortcut) | (WindowsGuideHotkeys.Modifiers(shortcut) << 16));
    }
    [Theory] [InlineData(0)] [InlineData(0x10)] [InlineData(0x20)] [InlineData(0x7b)] [InlineData(0x10073)] [InlineData(0x8004b)] [InlineData(-1)]
    public void ReservedKeysCannotBeSavedOrProbed(int shortcut)
    { Assert.False(WindowsGuideHotkeys.IsSupported(shortcut)); Assert.Throws<ArgumentOutOfRangeException>(() => WindowsGuideHotkeys.Check(shortcut)); }
    [Theory] [InlineData(0xbf)] [InlineData(0x77)] [InlineData(0x79)]
    public void FormerFixedAlternativesMigrateToDefaultWithoutChangingNewCustomKeys(int legacy)
    { Assert.Equal(WindowsGuideHotkeys.DefaultKey, WindowsGuideHotkeys.FromLegacy(legacy)); Assert.True(WindowsGuideHotkeys.IsSupported(legacy)); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
