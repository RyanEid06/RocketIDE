using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RocketIDE.Core.Commands;
using RocketIDE.Core.Output;

namespace RocketIDE.Infrastructure.Processes;

public sealed class WindowsJobProcessRunner : IProcessRunner, IDisposable
{
    private readonly object _gate = new();
    private ActiveRun? _active;
    private bool _disposed;

    public async Task<ProcessRunResult> RunAsync(
        ProcessStartRequest request,
        IProgress<ProcessOutput> output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Rocket process execution requires Windows.");
        }

        var fileName = Path.GetFullPath(request.FileName);
        var workingDirectory = Path.GetFullPath(request.WorkingDirectory);
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException("Process executable was not found.", fileName);
        }
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"Process working directory '{workingDirectory}' does not exist.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        using var job = CreateKillOnCloseJob();
        var active = new ActiveRun(job);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_active is not null)
            {
                throw new InvalidOperationException("A Rocket command is already running.");
            }
            _active = active;
        }

        using var cancellationRegistration = cancellationToken.Register(() => RequestStop(active));
        SafeFileHandle? processHandle = null;
        SafeFileHandle? threadHandle = null;
        SafeFileHandle? stdoutRead = null;
        SafeFileHandle? stdoutWrite = null;
        SafeFileHandle? stderrRead = null;
        SafeFileHandle? stderrWrite = null;
        SafeFileHandle? stdinRead = null;
        SafeFileHandle? stdinWrite = null;
        IntPtr environmentBlock = IntPtr.Zero;

        try
        {
            CreateRedirectPipe(out stdoutRead, out stdoutWrite, parentReads: true);
            CreateRedirectPipe(out stderrRead, out stderrWrite, parentReads: true);
            CreateRedirectPipe(out stdinRead, out stdinWrite, parentReads: false);

            var startup = new StartupInfo
            {
                Cb = (uint)Marshal.SizeOf<StartupInfo>(),
                Flags = StartfUseStdHandles | StartfUseShowWindow,
                ShowWindow = SwHide,
                StdInput = stdinRead.DangerousGetHandle(),
                StdOutput = stdoutWrite.DangerousGetHandle(),
                StdError = stderrWrite.DangerousGetHandle(),
            };

            var commandLine = new StringBuilder(
                WindowsCommandLine.JoinArguments([fileName, .. request.Arguments]));
            environmentBlock = BuildEnvironmentBlock(request.Environment);
            var creationFlags = CreateNoWindow | CreateSuspended;
            if (environmentBlock != IntPtr.Zero)
            {
                creationFlags |= CreateUnicodeEnvironment;
            }

            if (!CreateProcessW(
                    fileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: true,
                    creationFlags,
                    environmentBlock,
                    workingDirectory,
                    ref startup,
                    out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the Rocket process.");
            }

            processHandle = new SafeFileHandle(processInformation.Process, ownsHandle: true);
            threadHandle = new SafeFileHandle(processInformation.Thread, ownsHandle: true);
            // Parent copies of child-side handles must close immediately so readers observe EOF.
            stdoutWrite.Dispose();
            stdoutWrite = null;
            stderrWrite.Dispose();
            stderrWrite = null;
            stdinRead.Dispose();
            stdinRead = null;
            stdinWrite.Dispose();
            stdinWrite = null;

            if (!AssignProcessToJobObject(job, processHandle.DangerousGetHandle()))
            {
                var error = Marshal.GetLastWin32Error();
                _ = TerminateProcess(processHandle.DangerousGetHandle(), StopExitCode);
                throw new Win32Exception(error, "Could not attach the Rocket process to its cancellation job.");
            }

            // Start both readers while the child is still suspended so short-lived commands cannot
            // race the pipe readers during process startup.
            var stdoutTask = PumpLinesAsync(stdoutRead, ProcessOutputStream.StandardOutput, output);
            stdoutRead = null;
            var stderrTask = PumpLinesAsync(stderrRead, ProcessOutputStream.StandardError, output);
            stderrRead = null;

            // The child was created suspended. It cannot execute user code before job assignment.
            if (active.StopRequested)
            {
                TerminateJob(job);
            }
            else if (ResumeThread(threadHandle.DangerousGetHandle()) == uint.MaxValue)
            {
                var error = Marshal.GetLastWin32Error();
                TerminateJob(job);
                throw new Win32Exception(error, "Could not resume the Rocket process.");
            }

            await Task.Run(() => WaitForProcess(processHandle), CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            if (!GetExitCodeProcess(processHandle.DangerousGetHandle(), out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read Rocket process exit code.");
            }

            return new ProcessRunResult(unchecked((int)exitCode), active.StopRequested);
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            stdoutRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrRead?.Dispose();
            stderrWrite?.Dispose();
            stdinRead?.Dispose();
            stdinWrite?.Dispose();
            threadHandle?.Dispose();
            processHandle?.Dispose();

            lock (_gate)
            {
                if (ReferenceEquals(_active, active))
                {
                    _active = null;
                }
            }
        }
    }

    public void StopActive()
    {
        ActiveRun? active;
        lock (_gate)
        {
            active = _active;
        }
        if (active is not null)
        {
            RequestStop(active);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        StopActive();
        GC.SuppressFinalize(this);
    }

    private void RequestStop(ActiveRun active)
    {
        active.StopRequested = true;
        TerminateJob(active.Job);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsJobProcessRunner));
        }
    }

    private static async Task PumpLinesAsync(
        SafeFileHandle readHandle,
        ProcessOutputStream stream,
        IProgress<ProcessOutput> output)
    {
        using (readHandle)
        {
            await using var fileStream = new FileStream(readHandle, FileAccess.Read, bufferSize: 4096, isAsync: false);
            using var reader = new StreamReader(fileStream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (IOException)
                {
                    return;
                }
                if (line is null)
                {
                    return;
                }
                output.Report(new ProcessOutput(line, stream));
            }
        }
    }

    private static void WaitForProcess(SafeFileHandle processHandle)
    {
        var wait = WaitForSingleObject(processHandle.DangerousGetHandle(), Infinite);
        if (wait != WaitObject0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for the Rocket process failed.");
        }
    }

    private static void CreateRedirectPipe(out SafeFileHandle readHandle, out SafeFileHandle writeHandle, bool parentReads)
    {
        var security = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        if (!CreatePipe(out readHandle, out writeHandle, ref security, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create redirected process pipe.");
        }

        var parentHandle = parentReads ? readHandle : writeHandle;
        if (!SetHandleInformation(parentHandle, HandleFlagInherit, 0))
        {
            var error = Marshal.GetLastWin32Error();
            readHandle.Dispose();
            writeHandle.Dispose();
            throw new Win32Exception(error, "Could not protect parent process pipe handle from inheritance.");
        }
    }

    private static SafeFileHandle CreateKillOnCloseJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the Rocket process job.");
        }

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };
        var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, pointer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, pointer, (uint)length))
            {
                var error = Marshal.GetLastWin32Error();
                job.Dispose();
                throw new Win32Exception(error, "Could not configure the Rocket process job.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
        return job;
    }

    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return IntPtr.Zero;
        }

        var variables = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is not null)
            .ToDictionary(entry => (string)entry.Key, entry => Convert.ToString(entry.Value) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides)
        {
            variables[pair.Key] = pair.Value;
        }

        var builder = new StringBuilder();
        foreach (var pair in variables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        builder.Append('\0');
        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private static void TerminateJob(SafeFileHandle job)
    {
        if (job.IsInvalid || job.IsClosed)
        {
            return;
        }
        _ = TerminateJobObject(job, StopExitCode);
    }

    private sealed class ActiveRun(SafeFileHandle job)
    {
        public SafeFileHandle Job { get; } = job;
        public volatile bool StopRequested;
    }

    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseShowWindow = 0x00000001;
    private const uint StartfUseStdHandles = 0x00000100;
    private const ushort SwHide = 0;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0x00000000;
    private const uint StopExitCode = 0xC000013A;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
