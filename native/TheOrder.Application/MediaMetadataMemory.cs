namespace TheOrder.Application;

// AniList and MusicBrainz arbitrate their own requests against this same gate.
// MusicBrainz supplies a snapshot function to isolate its mutable arrays.
internal sealed class MediaMetadataMemory<T>(Func<T, string> keyOf, Func<T, T> snapshot) where T : class
{
    public object Gate { get; } = new();
    private readonly Dictionary<string, T> values = new();
    private long revision;

    public void Commit(T value)
    {
        // The provider's request check and the memory update own the same gate.
        System.Diagnostics.Debug.Assert(Monitor.IsEntered(Gate));
        Remember(value);
        revision++;
    }

    public async Task<T?> ReadAsync(string key, Func<T?, bool> valid,
        Func<string, CancellationToken, Task<T?>>? readDisk, CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            long observedRevision;
            lock (Gate)
            {
                if (values.TryGetValue(key, out var current))
                {
                    if (!valid(current)) return null;
                    Remember(current);
                    return snapshot(current);
                }
                observedRevision = revision;
            }

            if (readDisk is null) return null;
            T? value = null;
            try { value = await readDisk(key, cancellation).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

            cancellation.ThrowIfCancellationRequested();
            lock (Gate)
            {
                // A newer commit may already have been evicted while this read was pending.
                if (revision != observedRevision) continue;
                if (!valid(value)) return null;
                var owned = snapshot(value!);
                Remember(owned);
                return snapshot(owned);
            }
        }
    }

    private void Remember(T value)
    {
        string key = keyOf(value);
        values.Remove(key);
        values[key] = value;
        while (values.Count > 64) values.Remove(values.Keys.First());
    }
}
