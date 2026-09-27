using System.Collections.Concurrent;
using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using DbgX;
using DbgX.Interfaces.Services;
using DbgX.Interfaces.Events;
using DbgX.Interfaces.Structs;
using DbgX.Requests;
using DbgX.Requests.Initialization;

namespace RocketIDE.Debugger;

public sealed class DbgXCommandTransport : IDebuggerCommandTransport
{
    private readonly DebuggerSynchronizationContext _context;
    private readonly DebugEngine _engine;
    private readonly DebuggerOwnedProcesses.EngineHostLease _engineHosts = new(Environment.ProcessId);
    private bool _disposed;

    private DbgXCommandTransport(DebuggerSynchronizationContext context)
    {
        _context = context;
        _engine = new DebugEngine();
        _engineHosts.Capture();
        _engine.DmlOutput += Engine_DmlOutput;
    }

    public event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;

    public static Task<DbgXCommandTransport> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new DebuggerSynchronizationContext();
        var creation = InvokeAsync(context, () => Task.FromResult(new DbgXCommandTransport(context)));
        return DebuggerCreation.AwaitAsync(creation, TimeSpan.FromSeconds(5), context.Dispose, cancellationToken);
    }

    public Task CreateProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        executablePath = Path.GetFullPath(executablePath);
        workingDirectory = Path.GetFullPath(workingDirectory);
        var argumentText = WindowsArgumentQuoter.Join(arguments);
        return InvokeAsync(async () =>
        {
            var options = CreateLaunchOptions(executablePath, workingDirectory);
            try { await _engine.SendRequestAsync(new CreateProcessRequest(executablePath, argumentText, options), cancellationToken); }
            finally { _engineHosts.Capture(); }
            await _engine.SendRequestAsync(new ExecuteRequest(".prefer_dml 0"));
            await _engine.SendRequestAsync(new ExecuteRequest(".noshell"));
            return true;
        }).WaitAsync(cancellationToken);
    }

    public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return InvokeAsync(async () => await _engine.SendRequestAsync(new ExecuteToStringRequest(command), cancellationToken))
            .WaitAsync(cancellationToken);
    }

    public Task ExecuteRunAsync(string command, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return InvokeAsync(async () =>
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sawRunning = false;
            void StateChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName != nameof(DebuggingState.RunningState)) return;
                if (_engine.DebuggingState.RunningState == RunningState.Running) sawRunning = true;
                else if (sawRunning) stopped.TrySetResult();
            }
            void Disconnected(object? sender, UnexpectedEngineDisconnectionEventArgs e) =>
                stopped.TrySetException(new InvalidOperationException("The native debugger engine disconnected."));
            _engine.DebuggingState.PropertyChanged += StateChanged;
            _engine.UnexpectedEngineDisconnection += Disconnected;
            try
            {
                await _engine.SendRequestAsync(new ExecuteToStringRequest(command), cancellationToken);
                if (sawRunning || _engine.DebuggingState.RunningState == RunningState.Running)
                    await stopped.Task.WaitAsync(cancellationToken);
                return true;
            }
            finally
            {
                _engine.DebuggingState.PropertyChanged -= StateChanged;
                _engine.UnexpectedEngineDisconnection -= Disconnected;
            }
        }).WaitAsync(cancellationToken);
    }

    public Task BreakAsync(int processId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.GetProcessById(processId);
        if (!NativeMethods.DebugBreakProcess(process.Handle))
        {
            throw new InvalidOperationException($"DebugBreakProcess failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return InvokeAsync(async () => await _engine.StopDebuggingAsync()).WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await InvokeAsync(async () =>
            {
                _engine.DmlOutput -= Engine_DmlOutput;
                // Dispose alone leaves DbgX's automatic engine recovery active.
                // Shutdown first so a forced host termination cannot leave a
                // replacement host behind after this transport has been retired.
                try { await _engine.ShutdownAsync(250); }
                finally { _engine.Dispose(); }
                return true;
            }).WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ForceTerminateOwnedProcesses();
        }
        finally
        {
            _context.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private Task<T> InvokeAsync<T>(Func<Task<T>> action) => InvokeAsync(_context, action);

    private static Task<T> InvokeAsync<T>(SynchronizationContext context, Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            try
            {
                action().ContinueWith(task =>
                {
                    if (task.IsCanceled) completion.TrySetCanceled();
                    else if (task.IsFaulted) completion.TrySetException(task.Exception!.InnerExceptions);
                    else completion.TrySetResult(task.Result);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, null);
        return completion.Task;
    }

    internal static EngineOptions CreateLaunchOptions(string executablePath, string workingDirectory)
    {
        // Rocket ships its matching PDB beside the executable. The engine's default
        // symbol-server fallback can stall stack inspection for minutes offline.
        var options = new EngineOptions
        {
            SymPath = Path.GetDirectoryName(Path.GetFullPath(executablePath)),
            SymOptIgnoreNtSympath = true,
        };
        TrySetWorkingDirectory(options, workingDirectory);
        return options;
    }

    private static void TrySetWorkingDirectory(EngineOptions options, string workingDirectory)
    {
        // DbgX has changed the name of this option across releases. Keep the compatibility shim
        // inside the adapter instead of leaking version-specific API into the rest of RocketIDE.
        var property = options.GetType().GetProperty("StartDirectory", BindingFlags.Public | BindingFlags.Instance)
            ?? options.GetType().GetProperty("WorkingDirectory", BindingFlags.Public | BindingFlags.Instance)
            ?? options.GetType().GetProperty("InitialDirectory", BindingFlags.Public | BindingFlags.Instance)
            ?? options.GetType().GetProperty("CurrentDirectory", BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite == true && property.PropertyType == typeof(string))
        {
            property.SetValue(options, workingDirectory);
        }
    }


    private void Engine_DmlOutput(object? sender, OutputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Output)) OutputReceived?.Invoke(this, new RocketDebugOutputEventArgs(e.Output));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void ForceTerminateOwnedProcesses()
    {
        // A delayed disposer must never discover and kill a subsequent session's host.
        _engineHosts.Terminate();
    }

    internal sealed class DebuggerSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly Thread _thread;
        private readonly TimeSpan _joinTimeout;
        private int _disposed;

        public DebuggerSynchronizationContext(TimeSpan? joinTimeout = null)
        {
            _joinTimeout = joinTimeout ?? TimeSpan.FromMilliseconds(250);
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "RocketIDE.DbgX",
            };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            try { _queue.Add((d, state)); }
            catch (InvalidOperationException) when (Volatile.Read(ref _disposed) != 0) { }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        }

        private void Run()
        {
            SetSynchronizationContext(this);
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable()) item.Callback(item.State);
            }
            finally
            {
                // The worker owns queue disposal. A caller that times out on Join must not
                // destroy synchronization state while this thread still uses it.
                _queue.Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.CompleteAdding();
            if (Thread.CurrentThread != _thread) _thread.Join(_joinTimeout);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DebugBreakProcess(IntPtr process);
    }
}

internal static class WindowsArgumentQuoter
{
    internal static string Join(IReadOnlyList<string> arguments) => string.Join(" ", arguments.Select(Quote));

    private static string Quote(string argument)
    {
        argument ??= string.Empty;
        if (argument.Length > 0 && argument.All(ch => !char.IsWhiteSpace(ch) && ch != '"')) return argument;
        var builder = new System.Text.StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }
            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }
            builder.Append('\\', backslashes).Append(ch);
            backslashes = 0;
        }
        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }
}
