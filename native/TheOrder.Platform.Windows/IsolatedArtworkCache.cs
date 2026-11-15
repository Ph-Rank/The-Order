using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TheOrder.Application;
namespace TheOrder.Platform.Windows;

// Public downloaded artwork, independent from personal photos/library backups.
// No TTL/LRU deletion: browsing must not evict already prepared local-game art.
// A full cache refuses new writes; all reads and enumeration remain bounded.
public sealed partial class IsolatedArtworkCache(NativeConfiguration configuration) : IRemoteArtworkCache
{
    private const string DirectoryName = "native-artwork-v1";
    private const int Magic=0x31414F54, MaximumFileBytes=20*1024*1024, MaximumFiles=20000;
    private const long MaximumBytes=2L*1024*1024*1024;
    private readonly SemaphoreSlim serial=new(1);

    internal Task<ImageCacheUsage> ManageDiskAsync(bool clear) =>
        ImageCacheStorage.WithDirectoryAsync(configuration, DirectoryName,
            name => name.EndsWith(".art", StringComparison.Ordinal) && KeyPattern().IsMatch(name[..^4]),
            MaximumFiles + 16, serial, clear);

    private static byte[]? ReadFile(string path)
    {
        try
        {
            using var file=IsolatedLibrarySource.OpenChecked(path);
            if(file.Length is <40 or >MaximumFileBytes+40) return null;
            using var reader=new BinaryReader(file);if(reader.ReadInt32()!=Magic) return null;
            int size=reader.ReadInt32();if(size is <4 or >MaximumFileBytes||file.Length!=40L+size)return null;
            var hash=reader.ReadBytes(32);var bytes=reader.ReadBytes(size);
            return CryptographicOperations.FixedTimeEquals(hash,SHA256.HashData(bytes))?bytes:null;
        }
        catch(FileNotFoundException){return null;}
    }
    public Task<byte[]?> ReadAsync(string key,CancellationToken cancellation)=>Run<byte[]?>(root=>ReadFile(Path.Combine(root,key+".art")),key,cancellation);
    public Task<bool> WriteAsync(string key,byte[] encoded,CancellationToken cancellation)=>Run(root=>
    {
        if(encoded.Length is <4 or >MaximumFileBytes) return false;
        var target=Path.Combine(root,key+".art");
        if(ReadFile(target) is { } prior&&prior.AsSpan().SequenceEqual(encoded))return true;
        bool replacing=File.Exists(target);
        var files=new DirectoryInfo(root).EnumerateFiles("*.art").Take(MaximumFiles+1).ToArray();
        if(files.Length>MaximumFiles||!replacing&&files.Length>=MaximumFiles||files.Sum(f=>f.Length)+encoded.Length+40>MaximumBytes)return false;
        cancellation.ThrowIfCancellationRequested();var temporary=Path.Combine(root,Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(file)){writer.Write(Magic);writer.Write(encoded.Length);writer.Write(SHA256.HashData(encoded));writer.Write(encoded);file.Flush(true);}
            cancellation.ThrowIfCancellationRequested();File.Move(temporary,target,true);return true;
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    },key,cancellation);
    private async Task<T> Run<T>(Func<string,T> action,string key,CancellationToken cancellation)
    {
        if(!KeyPattern().IsMatch(key))throw new InvalidDataException("Invalid public artwork key");
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try {return await Task.Run(()=>
        {
            var profile=NativeProfileAccess.Validate(configuration);var held=IsolatedLibraryPersistence.PinDirectoryChain(profile);
            try
            {
                NativeProfileAccess.Validate(configuration);var root=Path.Combine(profile,DirectoryName);Directory.CreateDirectory(root);
                var cacheHeld=IsolatedLibraryPersistence.PinDirectoryChain(root);
                try {cancellation.ThrowIfCancellationRequested();return action(root);}
                finally {foreach(var h in cacheHeld)h.Dispose();}
            }
            finally {foreach(var h in held)h.Dispose();}
        },cancellation).ConfigureAwait(false);}
        finally {serial.Release();}
    }
    [GeneratedRegex("^[A-F0-9]{64}$",RegexOptions.CultureInvariant)]private static partial Regex KeyPattern();
}
