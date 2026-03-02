using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chess.Console;

[SupportedOSPlatform("windows")]
internal static class WindowsConsoleInput
{
    private const int STD_INPUT_HANDLE = -10;

    /// <summary>
    /// Provides native Windows console input handling, including mouse events.
    /// </summary>
    [Flags]
    private enum ConsoleInputMode : uint
    {
        None = 0,
        MouseInput = 0x0010,
        QuickEditMode = 0x0040,
        ExtendedFlags = 0x0080,
        VirtualTerminalInput = 0x0200,
    }

    private enum InputEventType : ushort
    {
        Key = 0x0001,
        Mouse = 0x0002,
        WindowBufferSize = 0x0004,
        Menu = 0x0008,
        Focus = 0x0010,
    }

    [Flags]
    private enum MouseButtonState : uint
    {
        None = 0,
        FromLeft1stButtonPressed = 0x0001,
        RightmostButtonPressed   = 0x0002,
        FromLeft2ndButtonPressed = 0x0004,
        FromLeft3rdButtonPressed = 0x0008,
        FromLeft4thButtonPressed = 0x0010,
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        None = 0,
        MouseMoved = 0x0001,
        DoubleClick = 0x0002,
        MouseWheel = 0x0004,
        MouseHorizontalWheel = 0x0008,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out ConsoleInputMode lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, ConsoleInputMode dwMode);

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
        public InputEventType EventType;
        //union {
        [FieldOffset(4)]
        public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(4)]
        public MOUSE_EVENT_RECORD MouseEvent;
        [FieldOffset(4)]
        public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
        [FieldOffset(4)]
        public MENU_EVENT_RECORD MenuEvent;
        [FieldOffset(4)]
        public FOCUS_EVENT_RECORD FocusEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public uint bKeyDown;
        public short wRepeatCount;
        public short wVirtualKeyCode;
        public short wVirtualScanCode;
        public char UnicodeChar;
        public int dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public MouseButtonState dwButtonState;
        public int dwControlKeyState;
        public MouseEventFlags dwEventFlags;
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOW_BUFFER_SIZE_RECORD
    {
        public COORD dwSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MENU_EVENT_RECORD
    {
        public int dwCommandId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FOCUS_EVENT_RECORD
    {
        public uint bSetFocus;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    private static nint _inputHandle;
    private static ConsoleInputMode _originalMode;

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

        var newMode = (_originalMode | ConsoleInputMode.VirtualTerminalInput | ConsoleInputMode.MouseInput | ConsoleInputMode.ExtendedFlags) & ~ConsoleInputMode.QuickEditMode;
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
        var newMode = (_originalMode | ConsoleInputMode.MouseInput | ConsoleInputMode.ExtendedFlags) & ~ConsoleInputMode.QuickEditMode;
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
        if (_inputHandle == nint.Zero
            || _inputHandle == new nint(-1) 
            || !GetNumberOfConsoleInputEvents(_inputHandle, out var eventCount)
            || eventCount <= 0
        )
        {
            return (null, null);
        }

        var buffer = new INPUT_RECORD[1];
        if (!ReadConsoleInput(_inputHandle, buffer, (uint)buffer.Length, out uint eventsRead) || eventsRead == 0)
        {
            return (null, null);
        }

        var record = buffer[0];

        if (record.EventType == InputEventType.Key)
        {
            var keyEvent = record.KeyEvent;
            if (keyEvent.bKeyDown != 0 && keyEvent.UnicodeChar != '\0')
            {
                return (null, keyEvent.UnicodeChar);
            }
            return (null, null);
        }

        if (record.EventType != InputEventType.Mouse)
        {
            return (null, null);
        }

        var mouseEvent = record.MouseEvent;

        // We're interested in button press/release (None)
        if (mouseEvent.dwEventFlags != MouseEventFlags.None)
        {
            return (null, null);
        }

        // Check if left button is involved in this press/release event
        if (mouseEvent.dwButtonState.HasFlag(MouseButtonState.FromLeft1stButtonPressed))
        {
            return ((0, mouseEvent.dwMousePosition.X, mouseEvent.dwMousePosition.Y, false), null);
        }

        // Left button not pressed — this is a release (or another button we don't handle)
        return ((0, mouseEvent.dwMousePosition.X, mouseEvent.dwMousePosition.Y, true), null);
    }
}
