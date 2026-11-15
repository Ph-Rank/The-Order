using Microsoft.Extensions.Logging;
using TheOrder.Platform.Windows;

namespace TheOrder.WinUI.Startup;

// Bounded lifecycle records only: never format arbitrary application data,
// credentials, paths, exception messages or stack traces into release logs.
internal sealed class NativeLifecycleLog(NativeProfileStore profile) : ILoggerProvider
{
    private readonly object gate = new();
    private readonly NativeLifecycleJournal journal = new(profile);
    private readonly Queue<string> recentWarnings = new(8);
    private bool disposed;
    public ILogger CreateLogger(string categoryName) => new LifecycleLogger(this);
    public void Dispose() { lock (gate) disposed = true; }
    internal void Write(LogLevel level, Exception? error)
    {
        lock (gate)
        {
            if (disposed) return;
            var errorType = error?.GetType().Name;
            journal.Write(level.ToString(), errorType);
            if (level < LogLevel.Warning) return;
            if (errorType is not { Length: > 0 and <= 96 } ||
                !errorType.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) errorType = "Lifecycle";
            if (recentWarnings.Count == 8) recentWarnings.Dequeue();
            recentWarnings.Enqueue(FormattableString.Invariant($"{DateTimeOffset.UtcNow:u} {level} {errorType}"));
        }
    }
    internal string RecentWarnings()
    {
        lock (gate) return string.Join('\n', recentWarnings);
    }
    private sealed class LifecycleLogger(NativeLifecycleLog owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(level)) owner.Write(level, error); }
    }
}
