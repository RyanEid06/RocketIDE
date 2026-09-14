using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RocketIDE.Infrastructure.Processes;

public static class WindowsCommandLine
{
    public static string JoinArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            builder.Append(QuoteArgument(argument ?? string.Empty));
        }
        return builder.ToString();
    }

    public static string QuoteArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length != 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
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

    public static IReadOnlyList<string> ParseArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows command-line parsing requires Windows.");
        }

        var vector = CommandLineToArgvW("rocket-program " + commandLine, out var count);
        if (vector == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var result = new List<string>(Math.Max(0, count - 1));
            for (var index = 1; index < count; index++)
            {
                var item = Marshal.ReadIntPtr(vector, index * IntPtr.Size);
                result.Add(Marshal.PtrToStringUni(item) ?? string.Empty);
            }
            return result;
        }
        finally
        {
            _ = LocalFree(vector);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
