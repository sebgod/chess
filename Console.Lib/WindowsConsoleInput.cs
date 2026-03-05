using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Console.Lib;

[SupportedOSPlatform("windows")]
internal static class WindowsConsoleInput
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;

    /// <summary>
    /// Provides native Windows console input handling, including mouse events.
    /// </summary>
    [Flags]
    private enum ConsoleMode : uint
    {
        None = 0,
        ProcessedInput = 0x0001,
        VirtualTerminalProcessing = 0x0004,
        WindowInput = 0x0008,
        MouseInput = 0x0010,
        QuickEditMode = 0x0040,
        ExtendedFlags = 0x0080,
        VirtualTerminalInput = 0x0200,
    }


    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out ConsoleMode lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, ConsoleMode dwMode);

    private static nint _inputHandle;
    private static nint _outputHandle;
    private static ConsoleMode _originalInputMode;
    private static ConsoleMode _originalOutputMode;

    /// <summary>
    /// Enables virtual terminal input and output processing.
    /// </summary>
    /// <returns>True if virtual terminal input and output processing was enabled successfully.</returns>
    public static bool EnableVirtualTerminalIO()
    {
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        if (_inputHandle == nint.Zero || _inputHandle == new nint(-1))
        {
            return false;
        }

        if (!GetConsoleMode(_inputHandle, out _originalInputMode))
        {
            return false;
        }

        _outputHandle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (!GetConsoleMode(_outputHandle, out _originalOutputMode))
        {
            return false;
        }

        var newInputMode = (
            ConsoleMode.VirtualTerminalInput
            | ConsoleMode.ProcessedInput
            | ConsoleMode.WindowInput
            | ConsoleMode.MouseInput
            | ConsoleMode.ExtendedFlags
        ) & ~ConsoleMode.QuickEditMode;

        return SetConsoleMode(_inputHandle, newInputMode)
            && SetConsoleMode(_outputHandle, _originalOutputMode | ConsoleMode.VirtualTerminalProcessing);
    }

    /// <summary>
    /// Restores the original console mode.
    /// </summary>
    public static void RestoreConsoleMode()
    {
        if (_inputHandle != nint.Zero && _inputHandle != new nint(-1))
        {
            SetConsoleMode(_inputHandle, _originalInputMode);
        }

        if (_outputHandle != nint.Zero && _outputHandle != new nint(-1))
        {
            SetConsoleMode(_outputHandle, _originalOutputMode);
        }
    }
}
