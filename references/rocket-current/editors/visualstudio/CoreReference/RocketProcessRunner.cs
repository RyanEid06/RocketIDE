using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Rocket.VisualStudio
{
    internal sealed class RocketProcessResult
    {
        internal int ExitCode { get; set; }
        internal bool Cancelled { get; set; }
    }

    internal sealed class RocketProcessRunner : IDisposable
    {
        private readonly object gate = new object();
        private Process currentProcess;
        private SafeFileHandle currentJob;

        internal bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    if (currentProcess == null) return false;
                    try { return !currentProcess.HasExited; }
                    catch (InvalidOperationException) { return true; }
                }
            }
        }

        internal async Task<RocketProcessResult> RunAsync(string fileName, IList<string> arguments, string workingDirectory,
            IDictionary<string, string> environment, Action<string, bool> onLine, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = JoinArguments(arguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (environment != null)
                foreach (var item in environment) startInfo.EnvironmentVariables[item.Key] = item.Value;

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (sender, args) => { if (args.Data != null) onLine(args.Data, false); };
            process.ErrorDataReceived += (sender, args) => { if (args.Data != null) onLine(args.Data, true); };
            var job = CreateKillOnCloseJob();
            lock (gate)
            {
                if (currentProcess != null) throw new InvalidOperationException("A Rocket command is already running.");
                currentProcess = process;
                currentJob = job;
            }

            var cancelled = false;
            try
            {
                if (!process.Start()) throw new Win32Exception("The Rocket process did not start.");
                if (!AssignProcessToJobObject(job, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the Rocket process to its cancellation job.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using (cancellationToken.Register(() =>
                {
                    cancelled = true;
                    TerminateJob(job);
                }))
                {
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                    process.WaitForExit();
                }
                return new RocketProcessResult { ExitCode = process.ExitCode, Cancelled = cancelled };
            }
            finally
            {
                lock (gate)
                {
                    currentProcess = null;
                    currentJob = null;
                }
                job.Dispose();
                process.Dispose();
            }
        }

        internal void Stop()
        {
            SafeFileHandle job;
            lock (gate) job = currentJob;
            if (job != null) TerminateJob(job);
        }

        public void Dispose()
        {
            Stop();
        }

        internal static string JoinArguments(IEnumerable<string> arguments)
        {
            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length != 0) builder.Append(' ');
                builder.Append(QuoteArgument(argument ?? string.Empty));
            }
            return builder.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length != 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return argument;
            var builder = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    ++backslashes;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(character);
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static SafeFileHandle CreateKillOnCloseJob()
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == null || job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the Rocket process job.");
            var information = new JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            var length = Marshal.SizeOf(information);
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(information, pointer, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, pointer, (uint)length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure the Rocket process job.");
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
            return job;
        }

        private static void TerminateJob(SafeFileHandle job)
        {
            if (job == null || job.IsInvalid || job.IsClosed) return;
            TerminateJobObject(job, 0xC000013A);
        }

        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectExtendedLimitInformationClass = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            internal ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            internal ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            internal long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            internal uint LimitFlags;
            internal UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            internal uint ActiveProcessLimit;
            internal UIntPtr Affinity;
            internal uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            internal JobObjectBasicLimitInformation BasicLimitInformation;
            internal IoCounters IoInfo;
            internal UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    }
}
