using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chess.Console;

/// <summary>
/// Provides native Windows console input handling, including mouse events.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsConsoleInput
{
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

    private const ushort KEY_EVENT = 0x0001;
    private const ushort MOUSE_EVENT = 0x0002;
    private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfConsoleInputEvents(nint hConsoleInput, out uint lpcNumberOfEvents);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInput(
        nint hConsoleInput,
        [Out] INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)]
        public ushort EventType;
        [FieldOffset(4)]
        public MOUSE_EVENT_RECORD MouseEvent;
        [FieldOffset(4)]
        public KEY_EVENT_RECORD KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct KEY_EVENT_RECORD
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    private static nint _inputHandle;
    private static uint _originalMode;
    private static uint _previousButtonState;

    /// <summary>
    /// Enables virtual terminal input processing so DEC Locator reports arrive as VT sequences.
    /// </summary>
    /// <returns>True if virtual terminal input was enabled successfully.</returns>
    public static bool EnableVirtualTerminalInput()
    {
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        if (_inputHandle == nint.Zero || _inputHandle == new nint(-1))
        {
            return false;
        }

        if (!GetConsoleMode(_inputHandle, out _originalMode))
        {
            return false;
        }

        uint newMode = (_originalMode | ENABLE_VIRTUAL_TERMINAL_INPUT | ENABLE_EXTENDED_FLAGS) & ~ENABLE_QUICK_EDIT_MODE;
        return SetConsoleMode(_inputHandle, newMode);
    }

    /// <summary>
    /// Enables mouse input on the Windows console.
    /// </summary>
    /// <returns>True if mouse input was enabled successfully.</returns>
    public static bool EnableMouseInput()
    {
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        if (_inputHandle == nint.Zero || _inputHandle == new nint(-1))
        {
            return false;
        }

        if (!GetConsoleMode(_inputHandle, out _originalMode))
        {
            return false;
        }

        // Enable mouse input and disable quick edit mode (which interferes with mouse)
        uint newMode = (_originalMode | ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS) & ~ENABLE_QUICK_EDIT_MODE;
        return SetConsoleMode(_inputHandle, newMode);
    }

    /// <summary>
    /// Restores the original console mode.
    /// </summary>
    public static void RestoreConsoleMode()
    {
        if (_inputHandle != nint.Zero && _inputHandle != new nint(-1))
        {
            SetConsoleMode(_inputHandle, _originalMode);
        }
    }

    /// <summary>
    /// Checks if there are any console input events available.
    /// </summary>
    public static bool HasInputEvents()
    {
        if (_inputHandle == nint.Zero || _inputHandle == new nint(-1))
        {
            return false;
        }

        return GetNumberOfConsoleInputEvents(_inputHandle, out uint count) && count > 0;
    }

    /// <summary>
    /// Tries to read an input event from the console.
    /// Returns a mouse event or a key character, depending on the input type.
    /// </summary>
    public static ((int Button, int X, int Y, bool IsRelease)? Mouse, char? KeyChar) TryReadInputEvent()
    {
        if (_inputHandle == nint.Zero || _inputHandle == new nint(-1))
        {
            return (null, null);
        }

        var buffer = new INPUT_RECORD[1];
        if (!ReadConsoleInput(_inputHandle, buffer, 1, out uint eventsRead) || eventsRead == 0)
        {
            return (null, null);
        }

        var record = buffer[0];

        if (record.EventType == KEY_EVENT)
        {
            var keyEvent = record.KeyEvent;
            if (keyEvent.bKeyDown && keyEvent.UnicodeChar != '\0')
            {
                return (null, keyEvent.UnicodeChar);
            }
            return (null, null);
        }

        if (record.EventType != MOUSE_EVENT)
        {
            return (null, null);
        }

        var mouseEvent = record.MouseEvent;

        // dwEventFlags: 0 = button press/release, 1 = mouse moved, 2 = double click
        // We're interested in button press/release (0)
        if (mouseEvent.dwEventFlags != 0)
        {
            return (null, null);
        }

        // Detect which button changed state by comparing with previous state
        uint currentState = mouseEvent.dwButtonState;
        uint changedButtons = currentState ^ _previousButtonState;
        _previousButtonState = currentState;

        // If no button state changed, ignore
        if (changedButtons == 0)
        {
            return (null, null);
        }

        // Check if left button changed - map to button 0
        if ((changedButtons & FROM_LEFT_1ST_BUTTON_PRESSED) != 0)
        {
            bool isRelease = (currentState & FROM_LEFT_1ST_BUTTON_PRESSED) == 0;
            return ((0, mouseEvent.dwMousePosition.X, mouseEvent.dwMousePosition.Y, isRelease), null);
        }

        return (null, null);
    }
}
