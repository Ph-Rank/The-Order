using System.ComponentModel;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed class WindowsFilesystemIdentity : IFilesystemIdentity
{
    private const uint DirectoryFlag = 0x02000000, OpenReparse = 0x00200000, ReparseAttribute = 0x400;
    private const uint Symlink = 0xA000000C, MountPoint = 0xA0000003;
    public PathPreflight Inspect(string storedPath, bool requireDirectory = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(storedPath) || !Path.IsPathFullyQualified(storedPath) || storedPath.StartsWith(@"\\.\", StringComparison.Ordinal)
                || storedPath.StartsWith(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase))
                return Failure(PreflightStatus.OtherIOError, UiText.Get("请选择完整的本地或 UNC 路径。"));
            var display = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storedPath));
            // Inspect every lexical ancestor without following its final reparse point. Unknown filters fail closed.
            bool alias = false; uint tag = 0;
            var reparsePaths = ImmutableArray.CreateBuilder<string>();
            var ancestors = new Stack<string>();
            for (string? part = display; part is not null; part = Path.GetDirectoryName(part))
            {
                if (ancestors.Count >= 512) return Failure(PreflightStatus.OtherIOError, UiText.Get("目录层级过多。"));
                ancestors.Push(part);
            }
            while (ancestors.TryPop(out var part))
            {
                using var entry = Open(part, true);
                if (entry.IsInvalid) return NativeFailure(Marshal.GetLastWin32Error(), alias);
                if (!TryAttributes(entry, out var info, out var error))
                    return AttributeFailure(error, alias);
                if ((info.Attributes & ReparseAttribute) == 0) continue;
                alias = true; tag = info.Tag;
                reparsePaths.Add(part);
                if (tag is not (Symlink or MountPoint)) return Failure(PreflightStatus.UnsupportedReparseCase, UiText.Get("此重解析类型暂不支持，请选择普通目录、junction 或目录符号链接。"));
            }
            using var handle = Open(display, false, requireDirectory);
            if (handle.IsInvalid) return NativeFailure(Marshal.GetLastWin32Error(), alias);
            if (!TryAttributes(handle, out var attributes, out var attributeError))
                return AttributeFailure(attributeError, alias);
            bool directory = (attributes.Attributes & 0x10) != 0;
            if ((attributes.Attributes & ReparseAttribute) != 0 && attributes.Tag is not (Symlink or MountPoint))
                return Failure(PreflightStatus.UnsupportedReparseCase, UiText.Get("最终目标属于不支持的重解析类型。"));
            if (requireDirectory && !directory) return Failure(PreflightStatus.FileInsteadOfDirectory, UiText.Get("所选路径是文件，请选择文件夹。"));
            var key = ReadIdentity(handle, out var identityError);
            if (key is null)
                return Failure(PreflightStatus.IdentityUnavailable, UiText.Get("此文件系统不能提供可靠的文件身份，请选择其他目录。"), identityError);
            var final = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
            if (length == 0 || length >= final.Capacity) return Failure(PreflightStatus.IdentityUnavailable, UiText.Get("无法取得句柄的最终路径。"), Marshal.GetLastWin32Error());
            return new(PreflightStatus.Valid, new(storedPath, display, final.ToString(), key, directory, alias, tag) { ReparsePaths = reparsePaths.ToImmutable() }, UiText.Get("可用"));
        }
        catch (UnauthorizedAccessException) { return Failure(PreflightStatus.AccessDenied, UiText.Get("无权访问此目录，请选择可读取的目录。")); }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        { return Failure(PreflightStatus.OtherIOError, UiText.Get("路径不可用或无法读取。")); }
    }
    private static bool TryAttributes(SafeFileHandle handle, out AttributeTag attributes, out int error)
    {
        if (GetFileInformationByHandleEx(handle, 9, out attributes, (uint)Marshal.SizeOf<AttributeTag>())) { error = 0; return true; }
        error = Marshal.GetLastWin32Error();
        // FAT does not implement FileAttributeTagInfo/FileIdInfo. Only a volume which the same
        // handle proves is FAT and incapable of reparse points may use the legacy attributes.
        // Access errors, unknown drivers, NTFS/ReFS and unexpected reparse attributes stay closed.
        if (!TryFatInformation(handle, error, out var legacy, out _)) return false;
        attributes = new() { Attributes = legacy.Attributes }; error = 0; return true;
    }
    private static FilesystemIdentity? ReadIdentity(SafeFileHandle handle, out int error)
    {
        if (GetFileInformationByHandleEx(handle, 18, out FileIdInfo identity, (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            // Preserve the existing full 128-bit identity, especially on ReFS.
            error = 0; return new(identity.Volume.ToString("X16"), $"{identity.Low:X16}{identity.High:X16}");
        }
        error = Marshal.GetLastWin32Error();
        if (!TryFatInformation(handle, error, out var legacy, out var serial)) return null;
        var volumePath = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, volumePath, (uint)volumePath.Capacity, 1); // VOLUME_NAME_GUID
        if (length == 0 || length >= volumePath.Capacity) { error = Marshal.GetLastWin32Error(); return null; }
        return FatFilesystemIdentity.Create(volumePath.ToString(), serial, legacy.Attributes,
            ((ulong)legacy.IndexHigh << 32) | legacy.IndexLow, ((ulong)legacy.CreationHigh << 32) | legacy.CreationLow);
    }
    private static bool TryFatInformation(SafeFileHandle handle, int error, out LegacyInformation legacy, out uint serial)
    {
        legacy = default; serial = 0;
        if (error is not (1 or 50 or 87)) return false;
        var fileSystem = new StringBuilder(64);
        return GetVolumeInformationByHandle(handle, IntPtr.Zero, 0, out serial, out _, out var flags, fileSystem, (uint)fileSystem.Capacity)
            && FatFilesystemIdentity.AllowsFallback(error, fileSystem.ToString(), flags)
            && GetFileInformationByHandle(handle, out legacy) && legacy.Serial == serial && (legacy.Attributes & ReparseAttribute) == 0;
    }
    private static PathPreflight AttributeFailure(int error, bool alias) => error is 1 or 50 or 87
        ? Failure(PreflightStatus.IdentityUnavailable, UiText.Get("无法检查目录的重解析信息。"), error) : NativeFailure(error, alias);
    private static SafeFileHandle Open(string path, bool noFollow, bool listDirectory = false) => CreateFile(path, listDirectory ? 0x81u : 0x80u, 7, IntPtr.Zero, 3, DirectoryFlag | (noFollow ? OpenReparse : 0), IntPtr.Zero);
    private static PathPreflight Failure(PreflightStatus status, string message, int error = 0) => new(status, null, message, error);
    private static PathPreflight NativeFailure(int error, bool alias) => error switch
    {
        2 or 3 => Failure(alias ? PreflightStatus.BrokenLink : PreflightStatus.NotFound, alias ? UiText.Get("链接目标不存在。") : UiText.Get("目录不存在。"), error),
        5 => Failure(PreflightStatus.AccessDenied, UiText.Get("无权访问此目录，请选择可读取的目录。"), error),
        1921 or 4392 or 4393 or 4394 => Failure(PreflightStatus.UnsupportedReparseCase, UiText.Get("链接循环或重解析目标不可用。"), error),
        _ => Failure(PreflightStatus.OtherIOError, UiText.Get("无法读取路径：") + new Win32Exception(error).Message, error)
    };
    [StructLayout(LayoutKind.Sequential)] private struct FileIdInfo { public ulong Volume, Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public uint Attributes, Tag; }
    [StructLayout(LayoutKind.Sequential)] private struct LegacyInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Serial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out FileIdInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out LegacyInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandle(SafeFileHandle handle, IntPtr name, uint nameCapacity, out uint serial, out uint maximumComponentLength, out uint flags, StringBuilder fileSystem, uint capacity);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
}
