using System.Collections.Immutable;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Infrastructure.Legacy;

namespace TheOrder.Tests;

internal sealed class SessionTestRig : IAsyncDisposable
{
    public const string Date = "2026-10-05T00:00:00.000Z";
    public static NativeLibraryCodec Codec => new();
    public static LibraryEntry Entry(string id = "g", long total = 1000, long count = 5) => new(id, "Synthetic Session", false, null,
        [new("i", @"C:\session-fixture", @"C:\session-fixture\game.exe", [])], "i", "planned", null, false, [], [], "", total, count, [], Date, null, null, null, new(false, null));
    public readonly SessionMemoryDisk Disk;
    public readonly SessionFakeFilesystem Files = new();
    public readonly SessionFakeClock Clock = new();
    public readonly SessionFakeTokens Tokens = new();
    public readonly SessionFakePlatform Platform = new();
    public readonly SessionFakePositions Positions = new();
    public TransactionalLibrary Library { get; private set; }
    public SessionEngine Engine { get; private set; }
    private readonly bool requireRecordingMinimum;
    public SessionTestRig(byte[]? document = null, bool requireRecordingMinimum = false)
    {
        this.requireRecordingMinimum = requireRecordingMinimum;
        Disk = new() { Main = document ?? Codec.Encode([Entry()], Date) };
        Library = Open(); Engine = Create();
    }
    public TransactionalLibrary Open() => TransactionalLibrary.Open(Codec, new SessionMemoryPersistence(Disk));
    private SessionEngine Create() => new(Library, Files, Platform, Positions, Clock, Tokens, autoPoll: false,
        launchOptions: new() { RunningPollInterval = TimeSpan.FromDays(1), PollInterval = TimeSpan.FromDays(1) },
        requireRecordingMinimum: requireRecordingMinimum);
    public async Task<LaunchStatus> Start()
    {
        await Engine.InitializeAsync();
        var preview = await Engine.PrepareAsync("g");
        await Engine.ConfirmPositionAsync(preview.Snapshot!);
        var started = await Engine.LaunchAsync("g");
        // The live 5A monitor may publish its first attach after LaunchAsync returns.
        for (int i = 0; i < 100 && Engine.LaunchStatus.Tracking == ProcessTrackingState.Attaching; i++) await Task.Delay(5);
        await Engine.DrainAsync();
        return started;
    }
    public async Task Reopen()
    {
        await Engine.DisposeAsync(); await Library.DisposeAsync();
        Library = Open(); Engine = Create(); await Engine.InitializeAsync();
    }
    public PlaySession Stored => Library.Snapshot.Entries[0].Sessions.Single();
    public NativeSession Native => Stored.Native!;
    public ProcessEvent Fact(ProcessEventKind kind, ProcessIdentity? current = null, ProcessIdentity? previous = null, DateTimeOffset? exit = null)
        => new(kind, Platform.Snapshot!.OperationId, previous, current ?? Engine.Snapshot.Current, Clock.Utc, kind == ProcessEventKind.ProcessExited ? exit : null, exit);
    public async Task Advance(double seconds)
    { Clock.Advance(seconds); await Engine.SampleAsync(); }
    public async ValueTask DisposeAsync() { await Engine.DisposeAsync(); await Library.DisposeAsync(); }
}
internal sealed class SessionMemoryDisk
{
    public byte[]? Main, Backup;
    public bool Fail;
    public Func<Task>? BeforeSave;
    public int Writes;
}
internal sealed class SessionMemoryPersistence(SessionMemoryDisk disk) : ILibraryPersistence
{
    public byte[]? ReadMain() => disk.Main?.ToArray();
    public byte[]? ReadBackup() => disk.Backup?.ToArray();
    public async Task<PersistenceReceipt> SaveAsync(ReadOnlyMemory<byte> previous, ReadOnlyMemory<byte> candidate)
    {
        if (disk.BeforeSave is { } before) await before();
        if (disk.Fail) throw new IOException("synthetic pre-commit failure");
        disk.Backup = previous.ToArray(); disk.Main = candidate.ToArray(); disk.Writes++;
        return new();
    }
    public void Dispose() { }
}
internal sealed class SessionFakeClock : ISessionClock
{
    public long Awake, Epoch;
    public DateTimeOffset Utc = DateTimeOffset.Parse(SessionTestRig.Date);
    public SessionClockStamp Read() => new(Awake, Epoch, Utc);
    public void Discontinuity() => Epoch++;
    public void Advance(double seconds) { Awake += (long)(seconds * 10_000_000); Utc = Utc.AddSeconds(seconds); }
}
internal sealed class SessionFakeFilesystem : IFilesystemIdentity
{
    public bool Invalid;
    public PathPreflight Inspect(string path, bool requireDirectory = true) => Invalid ? new(PreflightStatus.NotFound, null, "missing")
        : new(PreflightStatus.Valid, new(path, path, path, new("volume", path), requireDirectory, false, 0), "ok");
}
internal sealed class SessionFakeTokens : ISessionRecoveryTokens
{
    public uint? BootId { get; set; } = 7;
    public FilesystemIdentity ProfileIdentity { get; set; } = new("profile", "id");
    public bool Reject;
    public string Seal(SessionRecoveryIdentity value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));
    public SessionRecoveryIdentity? Open(string value)
    { try { return Reject ? null : JsonSerializer.Deserialize<SessionRecoveryIdentity>(Convert.FromBase64String(value)); } catch { return null; } }
}
internal sealed class SessionFakePositions : ILaunchPositionStore
{
    private ImmutableArray<LaunchPosition> values = [];
    public Task<ImmutableArray<LaunchPosition>> ReadAsync(CancellationToken cancellation) => Task.FromResult(values);
    public Task WriteAsync(ImmutableArray<LaunchPosition> positions, CancellationToken cancellation) { values = positions; return Task.CompletedTask; }
}
internal sealed class SessionProcessState(ProcessIdentity identity)
{
    public ProcessIdentity Identity = identity;
    public ProcessTrackingState State = ProcessTrackingState.RunningConfirmed;
    public bool UnknownIdentity;
}
internal sealed class SessionFakePlatform : IGameProcessPlatform
{
    public LaunchSnapshot? Snapshot;
    public readonly Dictionary<int, SessionProcessState> Processes = [];
    public int Starts, Handles, Attachments;
    public bool ThrowScope, UnknownStart, Deny, External;
    public ProcessIdentity Root => Processes[71].Identity;
    public SessionProcessState Add(int pid) => Processes[pid] = new(new(pid, DateTimeOffset.Parse(SessionTestRig.Date).ToFileTime() + pid,
        @"C:\session-fixture\game.exe", new("volume", @"C:\session-fixture\game.exe")));
    public IObservedProcess Start(LaunchSnapshot snapshot, CancellationToken cancellation)
    {
        if (Deny) throw new UnauthorizedAccessException();
        Starts++; Snapshot = snapshot; var root = Add(71); root.UnknownIdentity = UnknownStart;
        return new Observation(this, root, true);
    }
    public IObservedProcess? Attach(ProcessIdentity identity)
    {
        Attachments++;
        return Processes.TryGetValue(identity.Pid, out var state) && state.Identity == identity ? new Observation(this, state, false) : null;
    }
    public ImmutableArray<ProcessIdentity> FindExisting(PathObservation executable, int maximumProcesses) => External ? [Add(72).Identity] : [];
    public ProcessPresence RevalidateExternal(ProcessIdentity identity) => !Processes.TryGetValue(identity.Pid, out var state) || state.Identity != identity || state.State == ProcessTrackingState.Exited
        ? ProcessPresence.Inactive : state.State == ProcessTrackingState.Unknown ? ProcessPresence.Unknown : ProcessPresence.Active;
    public ProcessTreeObservation Descendants(ImmutableArray<ProcessObservation> ancestors, int maximumProcesses, int maximumCandidates) => new([], true);
    private sealed class Observation : IObservedProcess
    {
        private readonly SessionFakePlatform owner; private readonly SessionProcessState state; private readonly bool original; private bool disposed;
        public Observation(SessionFakePlatform owner, SessionProcessState state, bool original) { this.owner = owner; this.state = state; this.original = original; Interlocked.Increment(ref owner.Handles); }
        public int Pid => state.Identity.Pid;
        public ProcessObservation Query() => new(state.UnknownIdentity ? null : state.Identity, state.State,
            state.State == ProcessTrackingState.Exited ? state.Identity.CreationFileTime + 100000 : null, true);
        public IProcessLifetimeScope? RetainLifetimeScope() => original && owner.ThrowScope ? throw new IOException("scope lease") : new Scope(owner);
        public void Dispose() { if (!disposed) { disposed = true; Interlocked.Decrement(ref owner.Handles); } }
    }
    private sealed class Scope(SessionFakePlatform owner) : IProcessLifetimeScope
    {
        public ProcessLifetimeObservation Query()
        {
            var pids = owner.Processes.Values.Where(p => p.State != ProcessTrackingState.Exited).Select(p => p.Identity.Pid).ToImmutableArray();
            return new(true, (uint)pids.Length, (uint)owner.Processes.Count) { ActivePids = pids };
        }
        public void Dispose() { }
    }
}
