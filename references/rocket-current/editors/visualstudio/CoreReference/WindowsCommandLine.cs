using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Rocket.VisualStudio.Core
{
    internal static class WindowsCommandLine
    {
        internal static IReadOnlyList<string> ParseArguments(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return Array.Empty<string>();

            var argumentVector = CommandLineToArgvW("rocket-program " + commandLine, out var argumentCount);
            if (argumentVector == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var arguments = new List<string>(Math.Max(0, argumentCount - 1));
                for (var index = 1; index < argumentCount; ++index)
                {
                    var value = Marshal.ReadIntPtr(argumentVector, index * IntPtr.Size);
                    arguments.Add(Marshal.PtrToStringUni(value) ?? string.Empty);
                }
                return arguments;
            }
            finally
            {
                LocalFree(argumentVector);
            }
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine,
            out int argumentCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
