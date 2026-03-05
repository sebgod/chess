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

    private enum VirtualKey : short
    {
        None = 0x00,
        LButton = 0x01,
        RButton = 0x02,
        Cancel = 0x03,
        MButton = 0x04,
        XButton1 = 0x05,
        XButton2 = 0x06,
        Back = 0x08,
        Tab = 0x09,
        Clear = 0x0C,
        Return = 0x0D,
        Shift = 0x10,
        Control = 0x11,
        Menu = 0x12,
        Pause = 0x13,
        Capital = 0x14,
        Kana = 0x15,
        ImeOn = 0x16,
        Junja = 0x17,
        Final = 0x18,
        Hanja = 0x19,
        ImeOff = 0x1A,
        Escape = 0x1B,
        Convert = 0x1C,
        NonConvert = 0x1D,
        Accept = 0x1E,
        ModeChange = 0x1F,
        Space = 0x20,
        Prior = 0x21,
        Next = 0x22,
        End = 0x23,
        Home = 0x24,
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28,
        Select = 0x29,
        Print = 0x2A,
        Execute = 0x2B,
        Snapshot = 0x2C,
        Insert = 0x2D,
        Delete = 0x2E,
        Help = 0x2F,
        Key0 = 0x30,
        Key1 = 0x31,
        Key2 = 0x32,
        Key3 = 0x33,
        Key4 = 0x34,
        Key5 = 0x35,
        Key6 = 0x36,
        Key7 = 0x37,
        Key8 = 0x38,
        Key9 = 0x39,
        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,
        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,
        LWin = 0x5B,
        RWin = 0x5C,
        Apps = 0x5D,
        Sleep = 0x5F,
        Numpad0 = 0x60,
        Numpad1 = 0x61,
        Numpad2 = 0x62,
        Numpad3 = 0x63,
        Numpad4 = 0x64,
        Numpad5 = 0x65,
        Numpad6 = 0x66,
        Numpad7 = 0x67,
        Numpad8 = 0x68,
        Numpad9 = 0x69,
        Multiply = 0x6A,
        Add = 0x6B,
        Separator = 0x6C,
        Subtract = 0x6D,
        Decimal = 0x6E,
        Divide = 0x6F,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        F13 = 0x7C,
        F14 = 0x7D,
        F15 = 0x7E,
        F16 = 0x7F,
        F17 = 0x80,
        F18 = 0x81,
        F19 = 0x82,
        F20 = 0x83,
        F21 = 0x84,
        F22 = 0x85,
        F23 = 0x86,
        F24 = 0x87,
        NumLock = 0x90,
        Scroll = 0x91,
        LShift = 0xA0,
        RShift = 0xA1,
        LControl = 0xA2,
        RControl = 0xA3,
        LMenu = 0xA4,
        RMenu = 0xA5,
        BrowserBack = 0xA6,
        BrowserForward = 0xA7,
        BrowserRefresh = 0xA8,
        BrowserStop = 0xA9,
        BrowserSearch = 0xAA,
        BrowserFavorites = 0xAB,
        BrowserHome = 0xAC,
        VolumeMute = 0xAD,
        VolumeDown = 0xAE,
        VolumeUp = 0xAF,
        MediaNextTrack = 0xB0,
        MediaPrevTrack = 0xB1,
        MediaStop = 0xB2,
        MediaPlayPause = 0xB3,
        LaunchMail = 0xB4,
        LaunchMediaSelect = 0xB5,
        LaunchApp1 = 0xB6,
        LaunchApp2 = 0xB7,
        Oem1 = 0xBA,
        OemPlus = 0xBB,
        OemComma = 0xBC,
        OemMinus = 0xBD,
        OemPeriod = 0xBE,
        Oem2 = 0xBF,
        Oem3 = 0xC0,
        GamepadA = 0xC3,
        GamepadB = 0xC4,
        GamepadX = 0xC5,
        GamepadY = 0xC6,
        GamepadRightShoulder = 0xC7,
        GamepadLeftShoulder = 0xC8,
        GamepadLeftTrigger = 0xC9,
        GamepadRightTrigger = 0xCA,
        GamepadDpadUp = 0xCB,
        GamepadDpadDown = 0xCC,
        GamepadDpadLeft = 0xCD,
        GamepadDpadRight = 0xCE,
        GamepadMenu = 0xCF,
        GamepadView = 0xD0,
        GamepadLeftThumbstickButton = 0xD1,
        GamepadRightThumbstickButton = 0xD2,
        GamepadLeftThumbstickUp = 0xD3,
        GamepadLeftThumbstickDown = 0xD4,
        GamepadLeftThumbstickRight = 0xD5,
        GamepadLeftThumbstickLeft = 0xD6,
        GamepadRightThumbstickUp = 0xD7,
        GamepadRightThumbstickDown = 0xD8,
        GamepadRightThumbstickRight = 0xD9,
        GamepadRightThumbstickLeft = 0xDA,
        Oem4 = 0xDB,
        Oem5 = 0xDC,
        Oem6 = 0xDD,
        Oem7 = 0xDE,
        Oem8 = 0xDF,
        Oem102 = 0xE2,
        ProcessKey = 0xE5,
        Packet = 0xE7,
        Attn = 0xF6,
        CrSel = 0xF7,
        ExSel = 0xF8,
        ErEof = 0xF9,
        Play = 0xFA,
        Zoom = 0xFB,
        Pa1 = 0xFD,
        OemClear = 0xFE,
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
        public VirtualKey wVirtualKeyCode;
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
    public static ((int Button, int X, int Y, bool IsRelease)? Mouse, ConsoleKey Key) TryReadInputEvent()
    {
        if (_inputHandle == nint.Zero
            || _inputHandle == new nint(-1) 
            || !GetNumberOfConsoleInputEvents(_inputHandle, out var eventCount)
            || eventCount <= 0
        )
        {
            return (null, ConsoleKey.None);
        }

        var buffer = new INPUT_RECORD[1];
        if (!ReadConsoleInput(_inputHandle, buffer, (uint)buffer.Length, out uint eventsRead) || eventsRead == 0)
        {
            return (null, ConsoleKey.None);
        }

        var record = buffer[0];

        if (record.EventType == InputEventType.Key)
        {
            var keyEvent = record.KeyEvent;
            if (keyEvent.bKeyDown != 0)
            {
                return (null, (ConsoleKey)keyEvent.wVirtualKeyCode);
            }
            return (null, ConsoleKey.None);
        }

        if (record.EventType != InputEventType.Mouse)
        {
            return (null, ConsoleKey.None);
        }

        var mouseEvent = record.MouseEvent;

        // We're interested in button press/release (None)
        if (mouseEvent.dwEventFlags != MouseEventFlags.None)
        {
            return (null, ConsoleKey.None);
        }

        // Check if left button is involved in this press/release event
        if (mouseEvent.dwButtonState.HasFlag(MouseButtonState.FromLeft1stButtonPressed))
        {
            return ((0, mouseEvent.dwMousePosition.X, mouseEvent.dwMousePosition.Y, false), ConsoleKey.None);
        }

        // Left button not pressed — this is a release (or another button we don't handle)
        return ((0, mouseEvent.dwMousePosition.X, mouseEvent.dwMousePosition.Y, true), ConsoleKey.None);
    }
}
