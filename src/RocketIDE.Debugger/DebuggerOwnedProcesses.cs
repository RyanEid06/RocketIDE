using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RocketIDE.Debugger;

internal static class DebuggerOwnedProcesses
{
    internal sealed record ProcessEntry(int Id, int ParentId, string Name);

    internal static IReadOnlyList<int> FindOwnedEngineHosts(int ideProcessId, IEnumerable<ProcessEntry> processes)
    {
        var entries = processes.ToDictionary(process => process.Id);
        var owned = new List<int>();
        foreach (var process in entries.Values)
        {
            if (!string.Equals(process.Name, "EngHost.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var ancestor = process.ParentId;
            var seen = new HashSet<int>();
            while (ancestor != 0 && seen.Add(ancestor) && entries.TryGetValue(ancestor, out var parent))
            {
                if (ancestor == ideProcessId)
                {
                    owned.Add(process.Id);
                    break;
                }
                ancestor = parent.ParentId;
            }
        }
        return owned;
    }

    internal static void KillEngineHosts(int ideProcessId)
    {
        foreach (var processId in FindOwnedEngineHosts(ideProcessId, SnapshotProcesses()))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // The host may already have exited during the process snapshot.
            }
        }
    }

    private static IReadOnlyList<ProcessEntry> SnapshotProcesses()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == new IntPtr(-1)) return [];
        try
        {
            var entry = new NativeProcessEntry { Size = (uint)Marshal.SizeOf<NativeProcessEntry>() };
            var processes = new List<ProcessEntry>();
            if (!Process32FirstW(snapshot, ref entry)) return processes;
            do
            {
                processes.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExecutableName));
            }
            while (Process32NextW(snapshot, ref entry));
            return processes;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref NativeProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr snapshot, ref NativeProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
