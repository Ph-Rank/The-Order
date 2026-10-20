using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class FatFilesystemIdentityTests
{
    private const string Root = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\";

    [Theory]
    [InlineData(87, "exFAT", 0u, true)][InlineData(1, "FAT32", 0u, true)][InlineData(50, "fat", 0u, true)]
    [InlineData(87, "NTFS", 0u, false)][InlineData(87, "ReFS", 0u, false)][InlineData(87, "unknown", 0u, false)]
    [InlineData(87, "exFAT", 0x80u, false)][InlineData(5, "exFAT", 0u, false)]
    [InlineData(32, "exFAT", 0u, false)][InlineData(3, "exFAT", 0u, false)]
    [InlineData(4392, "exFAT", 0u, false)][InlineData(0, "exFAT", 0u, false)]
    public void OnlyUnsupportedInformationOnProvenNonReparseFatCanFallBack(int error, string fs, uint flags, bool expected)
        => Assert.Equal(expected, FatFilesystemIdentity.AllowsFallback(error, fs, flags));

    [Fact]
    public void LegacyKeyKeepsFullIndexCreationAndVolumeIdentity()
    {
        var value = FatFilesystemIdentity.Create(Root + "游戏.exe", 0xABCD, 0x20, 0x123456789ABCDEF0, 0x1020304050607080)!;
        Assert.StartsWith("FAT:12345678-1234-1234-1234-123456789abc:0000ABCD", value.Volume);
        Assert.Equal("L64:123456789ABCDEF0:1020304050607080", value.FileId);
        Assert.Equal(value, FatFilesystemIdentity.Create(Root + "renamed.exe", 0xABCD, 0x20, 0x123456789ABCDEF0, 0x1020304050607080));
        Assert.NotEqual(value, FatFilesystemIdentity.Create(Root + "游戏.exe", 0xABCD, 0x20, 0x123456789ABCDEF0, 0x1020304050607081));
        Assert.NotEqual(value, FatFilesystemIdentity.Create(Root + "游戏.exe", 0xABCD, 0x20, 0x123456799ABCDEF0, 0x1020304050607080));
        Assert.NotEqual(value, FatFilesystemIdentity.Create(Root + "游戏.exe", 0xABCE, 0x20, 0x123456789ABCDEF0, 0x1020304050607080));
        Assert.NotEqual(value, FatFilesystemIdentity.Create(Root.Replace("123456789abc", "123456789abd") + "游戏.exe", 0xABCD, 0x20, 0x123456789ABCDEF0, 0x1020304050607080));
    }
    [Theory]
    [InlineData(@"E:\game", 0x10u, 1ul, 1ul)]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\game", 0x10u, 1ul, 1ul)]
    [InlineData(Root + "game", 0x410u, 1ul, 1ul)]
    [InlineData(Root + "game", 0x10u, 0ul, 1ul)]
    [InlineData(Root + "game", 0x10u, 1ul, 0ul)]
    [InlineData(Root, 0x20u, 0ul, 0ul)]
    public void MissingIdentityOrUnexpectedReparseCannotBecomePathBasedIdentity(string path, uint attrs, ulong index, ulong creation)
        => Assert.Null(FatFilesystemIdentity.Create(path, 1, attrs, index, creation));
    [Fact]
    public void OnlyActualVolumeRootMayHaveZeroFileIndex()
    {
        var value = FatFilesystemIdentity.Create(Root, 1, 0x10, 0, 0);
        Assert.NotNull(value); Assert.Equal("ROOT", value.FileId);
        Assert.Null(FatFilesystemIdentity.Create(Root + "nested\\", 1, 0x10, 0, 0));
    }
}
