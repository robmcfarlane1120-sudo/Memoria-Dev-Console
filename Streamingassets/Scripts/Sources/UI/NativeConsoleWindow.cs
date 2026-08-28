using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using XInputDotNetPure;

namespace Memoria.DevConsole
{
    public sealed class NativeConsoleWindow : IDisposable
    {
        private enum Page
        {
            Home,
            LiveLog,
            CompileRestart,
            ModelViewer,
            GameReset,
            FieldState
        }

        private const Int32 CW_USEDEFAULT = unchecked((Int32)0x80000000);

        private const UInt32 WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const UInt32 WS_CHILD = 0x40000000;
        private const UInt32 WS_VISIBLE = 0x10000000;
        private const UInt32 WS_VSCROLL = 0x00200000;

        private const UInt32 ES_MULTILINE = 0x0004;
        private const UInt32 ES_AUTOVSCROLL = 0x0040;
        private const UInt32 ES_AUTOHSCROLL = 0x0080;
        private const UInt32 ES_READONLY = 0x0800;

        private const Int32 SW_HIDE = 0;
        private const Int32 SW_SHOW = 5;
        private const Int32 SW_RESTORE = 9;

        private const UInt32 WM_DESTROY = 0x0002;
        private const UInt32 WM_SIZE = 0x0005;
        private const UInt32 WM_CLOSE = 0x0010;
        private const UInt32 WM_CHAR = 0x0102;
        private const UInt32 WM_HOTKEY = 0x0312;
        private const UInt32 WM_TIMER = 0x0113;
        private const UInt32 WM_KEYDOWN = 0x0100;
        private const UInt32 WM_KEYUP = 0x0101;
        private const UInt32 WM_SYSKEYDOWN = 0x0104;
        private const UInt32 WM_SYSKEYUP = 0x0105;
        private const UInt32 WM_SETFONT = 0x0030;
        private const UInt32 WM_VSCROLL = 0x0115;
        private const UInt32 WM_CTLCOLOREDIT = 0x0133;
        private const UInt32 WM_CTLCOLORSTATIC = 0x0138;

        private const UInt32 EM_SETSEL = 0x00B1;
        private const UInt32 EM_REPLACESEL = 0x00C2;
        private const UInt32 EM_SCROLLCARET = 0x00B7;

        private const UInt32 WM_APP_APPEND = 0x8001;
        private const UInt32 WM_APP_SHOW_HOME = 0x8002;
        private const UInt32 WM_APP_HIDE = 0x8003;
        private const UInt32 WM_APP_EXIT = 0x8004;
        private const UInt32 WM_APP_COMPILE_APPEND = 0x8005;
        private const UInt32 WM_APP_FIELD_STATE_TEXT = 0x8006;

        private const Int32 SB_BOTTOM = 7;
        private const Int32 TRANSPARENT = 1;
        private const Int32 OPAQUE = 2;
        private const Int32 DEFAULT_GUI_FONT = 17;
        private const Int32 BLACK_BRUSH = 4;

        private const Int32 VK_ESCAPE = 0x1B;
        private const Int32 VK_A = 0x41;
        private const Int32 VK_CONTROL = 0x11;
        private const Int32 VK_RETURN = 0x0D;
        private const Int32 VK_UP = 0x26;
        private const Int32 VK_DOWN = 0x28;
        private const UInt32 VK_F10 = 0x79;
        private const Int32 VK_1 = 0x31;
        private const Int32 VK_2 = 0x32;
        private const Int32 VK_3 = 0x33;
        private const Int32 VK_4 = 0x34;
        private const Int32 VK_5 = 0x35;
        private const Int32 VK_6 = 0x36;
        private const Int32 VK_S = 0x53;
        private const Int32 WH_KEYBOARD_LL = 13;
        private const UInt32 MB_OK = 0x00000000;
        private const UInt32 MB_ICONINFORMATION = 0x00000040;
        private const UInt32 WS_EX_TOPMOST = 0x00000008;
        private const Int32 HOTKEY_TOGGLE = 0x4D44;

        private static readonly UIntPtr CONTROLLER_TIMER_ID = new UIntPtr(0xDCC1u);
        private const UInt32 CONTROLLER_POLL_MS = 35;
        private const Single TRIGGER_THRESHOLD = 0.50f;

        private Thread _thread;
        private volatile Boolean _running;
        private volatile Boolean _ready;

        private IntPtr _windowHandle;
        private IntPtr _textHandle;
        private IntPtr _blackBrush;
        private IntPtr _fontHandle;

        private Page _page = Page.Home;

        private Int32 _homeSelection;
        private Int32 _fieldSelection;
        private Boolean _controllerWasConnected;
        private GamePadState _previousPadState;
        private Boolean _hardResetChordLatched;
        private Boolean _consoleChordLatched;

        private readonly Object _appendLock = new Object();
        private String _pendingAppend = String.Empty;

        // Keep our own log buffer so leaving Live Log and coming back does NOT blank it.
        private readonly StringBuilder _liveLogBuffer = new StringBuilder();
        private const Int32 MaxLiveLogCharacters = 500000;

        private readonly Object _compileLock = new Object();
        private String _pendingCompileAppend = String.Empty;
        private readonly Object _fieldStateLock = new Object();
        private String _pendingFieldStateText = String.Empty;

        public event Action CompileRestartRequested;
        public event Action ModelViewerRequested;
        public event Action HardResetRequested;
        public event Action ResetToLauncherRequested;
        public event Action FieldStateMoveBackwardRequested;
        public event Action FieldStateMoveForwardRequested;
        public event Action FieldStateSetCheckpointRequested;
        public event Action FieldStateLoadCheckpointRequested;
        public event Action FieldStatePageOpened;

        private volatile String _lastError;
        private WndProcDelegate _wndProc;
        private LowLevelKeyboardProc _keyboardProc;
        private IntPtr _keyboardHook;
        private readonly Boolean[] _keyboardDown = new Boolean[256];

        public Boolean IsReady
        {
            get { return _ready; }
        }

        public String LastError
        {
            get { return _lastError; }
        }

        public Boolean Start()
        {
            if (_running)
                return true;

            try
            {
                _running = true;

                _thread = new Thread(WindowThreadMain);
                _thread.IsBackground = true;
                _thread.Name = "Memoria Dev Console Window";
                _thread.Start();

                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                _lastError = "Start() failed: " + ex;
                return false;
            }
        }

        public void AppendLiveLog(String text)
        {
            if (!_running || String.IsNullOrEmpty(text))
                return;

            text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

            lock (_appendLock)
            {
                _pendingAppend += text;
            }

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_APPEND, IntPtr.Zero, IntPtr.Zero);
        }

        public void AppendCompileOutput(String text)
        {
            if (!_running || String.IsNullOrEmpty(text))
                return;

            text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

            lock (_compileLock)
                _pendingCompileAppend += text;

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_COMPILE_APPEND, IntPtr.Zero, IntPtr.Zero);
        }

        public Boolean IsVisible
        {
            get
            {
                return _windowHandle != IntPtr.Zero && IsWindowVisible(_windowHandle);
            }
        }

        public Boolean IsLiveLogActive
        {
            get
            {
                return IsVisible && _page == Page.LiveLog;
            }
        }

        public void ShowHome()
        {
            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_SHOW_HOME, IntPtr.Zero, IntPtr.Zero);
        }

        public void Hide()
        {
            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_HIDE, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (!_running)
                return;

            _running = false;
            _ready = false;

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero);

            if (_thread != null && _thread.IsAlive)
                _thread.Join(1500);

            _thread = null;
        }

        private void WindowThreadMain()
        {
            try
            {
                _wndProc = WindowProc;

                String className = "MemoriaDevConsoleWindowClass";

                WNDCLASSEX windowClass = new WNDCLASSEX();
                windowClass.cbSize = (UInt32)Marshal.SizeOf(typeof(WNDCLASSEX));
                windowClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc);
                windowClass.hInstance = GetModuleHandle(null);
                windowClass.hCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512));
                windowClass.hbrBackground = GetStockObject(BLACK_BRUSH);
                windowClass.lpszClassName = className;

                UInt16 atom = RegisterClassEx(ref windowClass);

                if (atom == 0)
                {
                    Int32 err = Marshal.GetLastWin32Error();

                    if (err != 1410)
                        throw new Exception("RegisterClassEx failed. Win32 error " + err);
                }

                _blackBrush = GetStockObject(BLACK_BRUSH);

                _fontHandle = CreateFont(
                    -16, 0, 0, 0, 400,
                    0, 0, 0,
                    1, 0, 0, 0,
                    49,
                    "Consolas");

                if (_fontHandle == IntPtr.Zero)
                    _fontHandle = GetStockObject(DEFAULT_GUI_FONT);

                _windowHandle = CreateWindowEx(
                    WS_EX_TOPMOST,
                    className,
                    "Memoria Dev Console",
                    WS_OVERLAPPEDWINDOW,
                    CW_USEDEFAULT,
                    CW_USEDEFAULT,
                    920,
                    650,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    windowClass.hInstance,
                    IntPtr.Zero);

                if (_windowHandle == IntPtr.Zero)
                    throw new Exception("CreateWindowEx(main) failed. Win32 error " + Marshal.GetLastWin32Error());

                _textHandle = CreateWindowEx(
                    0,
                    "EDIT",
                    "",
                    WS_CHILD | WS_VISIBLE | WS_VSCROLL |
                    ES_MULTILINE | ES_AUTOVSCROLL | ES_AUTOHSCROLL | ES_READONLY,
                    12,
                    12,
                    880,
                    590,
                    _windowHandle,
                    IntPtr.Zero,
                    windowClass.hInstance,
                    IntPtr.Zero);

                if (_textHandle == IntPtr.Zero)
                    throw new Exception("CreateWindowEx(edit) failed. Win32 error " + Marshal.GetLastWin32Error());

                SendMessage(_textHandle, WM_SETFONT, _fontHandle, new IntPtr(1));

                RenderHome();

                ShowWindow(_windowHandle, SW_HIDE);
                UpdateWindow(_windowHandle);

            if (!RegisterHotKey(_windowHandle, HOTKEY_TOGGLE, 0, VK_F10))
                _lastError = "RegisterHotKey(F10) failed. Win32 error: " + Marshal.GetLastWin32Error();

                if (SetTimer(_windowHandle, CONTROLLER_TIMER_ID, CONTROLLER_POLL_MS, IntPtr.Zero) == UIntPtr.Zero)
                    _lastError = "SetTimer(controller) failed. Win32 error: " + Marshal.GetLastWin32Error();

                _keyboardProc = KeyboardHookProc;
                _keyboardHook = SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    _keyboardProc,
                    GetModuleHandle(null),
                    0);

                if (_keyboardHook == IntPtr.Zero)
                    _lastError = "SetWindowsHookEx(keyboard) failed. Win32 error: " + Marshal.GetLastWin32Error();

                _ready = true;

                MSG message;

                while (_running && GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            catch (Exception ex)
            {
                _lastError = "Window thread failed: " + ex;
            }
            finally
            {
                _ready = false;
                _running = false;
                _windowHandle = IntPtr.Zero;
                _textHandle = IntPtr.Zero;
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, UInt32 msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_CLOSE:
                    ShowWindow(hwnd, SW_HIDE);
                    return IntPtr.Zero;

                case WM_SIZE:
                    ResizeTextArea(lParam);
                    return IntPtr.Zero;

                case WM_KEYDOWN:
                    // Keyboard navigation is handled by the low-level keyboard hook.
                    // This avoids the read-only EDIT child swallowing arrows/Enter.
                    break;

                case WM_APP_FIELD_STATE_TEXT:
                    RenderFieldStateStatus();
                    return IntPtr.Zero;

                case WM_TIMER:
                    if (new UIntPtr(unchecked((UInt64)wParam.ToInt64())) == CONTROLLER_TIMER_ID)
                    {
                        PollController();
                        return IntPtr.Zero;
                    }
                    break;

                case WM_HOTKEY:
                    if ((Int32)wParam == HOTKEY_TOGGLE)
                    {
                        if (IsWindowVisible(_windowHandle))
                            Hide();
                        else
                            ShowHome();

                        return IntPtr.Zero;
                    }
                    break;

                case WM_APP_APPEND:
                    DrainPendingAppend();
                    return IntPtr.Zero;

                case WM_APP_COMPILE_APPEND:
                    DrainPendingCompileAppend();
                    return IntPtr.Zero;

                case WM_APP_SHOW_HOME:
                    RenderHome();
                    ShowWindow(hwnd, SW_RESTORE);
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow(hwnd);
                    SetFocus(hwnd);
                    return IntPtr.Zero;

                case WM_APP_HIDE:
                    ShowWindow(hwnd, SW_HIDE);
                    return IntPtr.Zero;

                case WM_APP_EXIT:
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;

                case WM_CTLCOLOREDIT:
                case WM_CTLCOLORSTATIC:
                    SetTextColor(wParam, 0x00FFFFFF);
                    SetBkColor(wParam, 0x00000000);
                    SetBkMode(wParam, OPAQUE);
                    return _blackBrush;

                case WM_DESTROY:
                    KillTimer(hwnd, CONTROLLER_TIMER_ID);

                    if (_keyboardHook != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(_keyboardHook);
                        _keyboardHook = IntPtr.Zero;
                    }

                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void HandleCharacter(Char ch)
        {
            if (_page == Page.Home)
            {
                switch (ch)
                {
                    case '1':
                        _homeSelection = 0;
                        ActivateHomeSelection();
                        return;
                    case '2':
                        _homeSelection = 1;
                        ActivateHomeSelection();
                        return;
                    case '3':
                        _homeSelection = 2;
                        ActivateHomeSelection();
                        return;
                    case '4':
                        _homeSelection = 3;
                        ActivateHomeSelection();
                        return;
                    case '5':
                        _homeSelection = 4;
                        ActivateHomeSelection();
                        return;
                    case '6':
                        _homeSelection = 5;
                        ActivateHomeSelection();
                        return;
                }
            }
            else if (_page == Page.FieldState)
            {
                switch (ch)
                {
                    case '1':
                        _fieldSelection = 0;
                        ActivateFieldSelection();
                        return;
                    case '2':
                        _fieldSelection = 1;
                        ActivateFieldSelection();
                        return;
                    case 's':
                    case 'S':
                        _fieldSelection = 2;
                        ActivateFieldSelection();
                        return;
                }
            }
        }

        private IntPtr KeyboardHookProc(Int32 nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                UInt32 message = unchecked((UInt32)wParam.ToInt64());
                KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam,
                    typeof(KBDLLHOOKSTRUCT));

                Int32 vk = unchecked((Int32)data.vkCode);

                if (vk >= 0 && vk < _keyboardDown.Length)
                {
                    if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        _keyboardDown[vk] = false;
                    }
                    else if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    {
                        if (!_keyboardDown[vk])
                        {
                            _keyboardDown[vk] = true;

                            if (IsWindowVisible(_windowHandle) &&
                                vk == VK_A &&
                                (GetKeyState(VK_CONTROL) & 0x8000) != 0 &&
                                _textHandle != IntPtr.Zero)
                            {
                                SendMessage(_textHandle, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                                return new IntPtr(1);
                            }

                            if (IsWindowVisible(_windowHandle) && HandleNavigationKey(vk))
                            {
                                // When the Dev Console itself owns foreground focus,
                                // consume the key so the read-only text box cannot move
                                // its caret/selection. If the user clicked back into FFIX
                                // or another window, still navigate the console but let the
                                // key continue normally to that foreground application.
                                IntPtr foreground = GetForegroundWindow();

                                if (foreground == _windowHandle || foreground == _textHandle)
                                    return new IntPtr(1);
                            }
                        }
                    }
                }
            }

            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private Boolean HandleNavigationKey(Int32 vk)
        {
            if (vk == VK_ESCAPE)
            {
                // Live Log is passive while FFIX owns focus. The user must click
                // the Dev Console first before ESC is allowed to leave Live Log.
                if (_page == Page.LiveLog && !IsConsoleForeground())
                    return false;

                ControllerBack();
                return true;
            }

            if (vk == VK_UP)
            {
                MoveControllerSelection(-1);
                return true;
            }

            if (vk == VK_DOWN)
            {
                MoveControllerSelection(1);
                return true;
            }

            if (vk == VK_RETURN)
            {
                ActivateControllerSelection();
                return true;
            }

            if (_page == Page.Home)
            {
                if (vk >= VK_1 && vk <= VK_6)
                {
                    _homeSelection = vk - VK_1;
                    ActivateHomeSelection();
                    return true;
                }
            }
            else if (_page == Page.FieldState)
            {
                if (vk >= VK_1 && vk <= VK_4)
                {
                    _fieldSelection = vk - VK_1;
                    ActivateFieldSelection();
                    return true;
                }
            }

            return false;
        }

        private void PollController()
        {
            GamePadState state;

            try
            {
                state = GamePad.GetState(PlayerIndex.One);
            }
            catch
            {
                _controllerWasConnected = false;
                return;
            }

            if (!state.IsConnected)
            {
                _controllerWasConnected = false;
                _hardResetChordLatched = false;
                _consoleChordLatched = false;
                return;
            }

            if (!_controllerWasConnected)
            {
                _controllerWasConnected = true;
                _previousPadState = state;
                return;
            }

            Boolean shoulders =
                state.Buttons.LeftShoulder == ButtonState.Pressed &&
                state.Buttons.RightShoulder == ButtonState.Pressed &&
                state.Triggers.Left >= TRIGGER_THRESHOLD &&
                state.Triggers.Right >= TRIGGER_THRESHOLD;

            Boolean hardResetChord = shoulders && state.DPad.Up == ButtonState.Pressed;
            Boolean consoleChord = shoulders && state.DPad.Down == ButtonState.Pressed;

            if (hardResetChord)
            {
                if (!_hardResetChordLatched)
                {
                    _hardResetChordLatched = true;

                    Action resetHandler = HardResetRequested;
                    if (resetHandler != null)
                        resetHandler();
                }
            }
            else
            {
                _hardResetChordLatched = false;
            }

            if (consoleChord)
            {
                if (!_consoleChordLatched)
                {
                    _consoleChordLatched = true;

                    if (!IsWindowVisible(_windowHandle))
                    {
                        _homeSelection = 0;
                        RenderHome();
                        ShowWindow(_windowHandle, SW_RESTORE);
                        ShowWindow(_windowHandle, SW_SHOW);
                        SetForegroundWindow(_windowHandle);
                        SetFocus(_windowHandle);
                    }
                }
            }
            else
            {
                _consoleChordLatched = false;
            }

            // Chord D-pad presses must never also move the menu cursor.
            if (hardResetChord || consoleChord)
            {
                _previousPadState = state;
                return;
            }

            if (IsWindowVisible(_windowHandle))
            {
                Boolean upPressed =
                    state.DPad.Up == ButtonState.Pressed &&
                    _previousPadState.DPad.Up != ButtonState.Pressed;

                Boolean downPressed =
                    state.DPad.Down == ButtonState.Pressed &&
                    _previousPadState.DPad.Down != ButtonState.Pressed;

                Boolean aPressed =
                    state.Buttons.A == ButtonState.Pressed &&
                    _previousPadState.Buttons.A != ButtonState.Pressed;

                Boolean bPressed =
                    state.Buttons.B == ButtonState.Pressed &&
                    _previousPadState.Buttons.B != ButtonState.Pressed;

                if (bPressed)
                {
                    // Live Log must not steal FFIX's B/Cancel input while the
                    // player is actively playing. B backs out only after the
                    // Dev Console itself has been clicked/focused.
                    if (_page != Page.LiveLog || IsConsoleForeground())
                        ControllerBack();
                }
                else
                {
                    if (upPressed)
                        MoveControllerSelection(-1);
                    else if (downPressed)
                        MoveControllerSelection(1);

                    if (aPressed)
                        ActivateControllerSelection();
                }
            }

            _previousPadState = state;
        }

        private Boolean IsConsoleForeground()
        {
            if (_windowHandle == IntPtr.Zero)
                return false;

            return GetForegroundWindow() == _windowHandle;
        }

        private void MoveControllerSelection(Int32 delta)
        {
            if (_page == Page.Home)
            {
                _homeSelection = WrapSelection(_homeSelection + delta, 6);
                RenderHome();
            }
            else if (_page == Page.FieldState)
            {
                _fieldSelection = WrapSelection(_fieldSelection + delta, 4);
                RenderFieldStateStatus();
            }
        }

        private static Int32 WrapSelection(Int32 value, Int32 count)
        {
            if (count <= 0)
                return 0;

            while (value < 0)
                value += count;

            while (value >= count)
                value -= count;

            return value;
        }

        private void ActivateControllerSelection()
        {
            if (_page == Page.Home)
                ActivateHomeSelection();
            else if (_page == Page.FieldState)
                ActivateFieldSelection();
        }

        private void ControllerBack()
        {
            if (_page == Page.Home)
            {
                // Controller Back must be identical to clicking the native X.
                // We are already on the Win32 window thread here, so hide synchronously
                // instead of posting another app message.
                if (_windowHandle != IntPtr.Zero)
                    ShowWindow(_windowHandle, SW_HIDE);
                return;
            }

            RenderHome();
        }

        private void ActivateHomeSelection()
        {
            switch (_homeSelection)
            {
                case 0:
                    RenderLiveLog();
                    return;

                case 1:
                    RenderCompileRestart();

                    Action compileHandler = CompileRestartRequested;
                    if (compileHandler != null)
                        compileHandler();
                    return;

                case 2:
                    RenderModelViewer();

                    Action modelViewerHandler = ModelViewerRequested;
                    if (modelViewerHandler != null)
                        modelViewerHandler();
                    return;

                case 3:
                    Action hardResetHandler = HardResetRequested;
                    if (hardResetHandler != null)
                        hardResetHandler();
                    return;

                case 4:
                    _fieldSelection = 0;
                    RenderFieldState();

                    Action fieldOpenedHandler = FieldStatePageOpened;
                    if (fieldOpenedHandler != null)
                        fieldOpenedHandler();
                    return;

                case 5:
                    Action launcherHandler = ResetToLauncherRequested;
                    if (launcherHandler != null)
                        launcherHandler();
                    return;
            }
        }

        private void ActivateFieldSelection()
        {
            switch (_fieldSelection)
            {
                case 0:
                    Action backHandler = FieldStateMoveBackwardRequested;
                    if (backHandler != null)
                        backHandler();
                    return;

                case 1:
                    Action forwardHandler = FieldStateMoveForwardRequested;
                    if (forwardHandler != null)
                        forwardHandler();
                    return;

                case 2:
                    Action setCheckpointHandler = FieldStateSetCheckpointRequested;
                    if (setCheckpointHandler != null)
                        setCheckpointHandler();
                    return;

                case 3:
                    Action loadCheckpointHandler = FieldStateLoadCheckpointRequested;
                    if (loadCheckpointHandler != null)
                        loadCheckpointHandler();
                    return;
            }
        }

        private void RenderHome()
        {
            _page = Page.Home;

            SetWindowText(_windowHandle, "Memoria Dev Console");

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                MenuLine(_homeSelection == 0, "[1] Live Log") +
                MenuLine(_homeSelection == 1, "[2] Compile + Restart") +
                MenuLine(_homeSelection == 2, "[3] Open / Close Model Viewer") +
                MenuLine(_homeSelection == 3, "[4] Hard Reset") +
                MenuLine(_homeSelection == 4, "[5] Field State") +
                MenuLine(_homeSelection == 5, "[6] Reset to Launcher") +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "DESCRIPTION\r\n" +
                "------------------------------------------------------------\r\n" +
                GetHomeDescription(_homeSelection) +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "D-Pad / Arrow Keys: Navigate    A / Enter: Select    B / ESC: Close\r\n" +
                "F10: Toggle    Keyboard [1-6] still supported\r\n");

            SetFocus(_windowHandle);
        }

        private static String GetFieldStateDescription(Int32 selection)
        {
            switch (selection)
            {
                case 0:
                    return
                        "Moves backward through the rolling history of the last 10 automatically captured field states.\r\n" +
                        "Use it to return to older test locations without loading a save or replaying the game.\r\n";

                case 1:
                    return
                        "Moves forward through the timeline after you have moved backward.\r\n" +
                        "Use it to return toward newer captured states without changing the saved history.\r\n";

                case 2:
                    return
                        "Pins the exact field state you are currently in as one dedicated checkpoint.\r\n" +
                        "The checkpoint is separate from the rolling 10-state history, so new field captures cannot push it out.\r\n";

                case 3:
                    return
                        "Loads the dedicated checkpoint directly at any time.\r\n" +
                        "Useful when one boss, cutscene, or script setup is your main test point and you always want a fast return to it.\r\n";
            }

            return String.Empty;
        }

        private static String GetHomeDescription(Int32 selection)
        {
            switch (selection)
            {
                case 0:
                    return
                        "Displays the live Memoria.log output while FFIX is running.\r\n" +
                        "Useful for watching script errors, debug messages, and runtime behavior without leaving the game.\r\n";

                case 1:
                    return
                        "Compiles every Memoria script source folder into its DLL, then hard restarts FFIX.\r\n" +
                        "This skips the launcher during script testing, so code changes can be rebuilt and loaded in one step.\r\n";

                case 2:
                    return
                        "Opens or closes Memoria's Model Viewer directly inside the running FFIX process.\r\n" +
                        "No launcher or restart on entry; closing returns directly to the current field when possible.\r\n";

                case 3:
                    return
                        "Hard resets the game without going to the launcher.\r\n" +
                        "Useful for editing BattlePatch.txt, DictionaryPatch.txt, and other parameters that require a hard reset before they can be tested in-game.\r\n";

                case 4:
                    return
                        "Quickly reloads recent field states from the last 10 captured fields.\r\n" +
                        "Useful for field scripting and awkward test locations: no manual save reload or replaying the game to return to the test spot.\r\n";

                case 5:
                    return
                        "Shuts the game down and restarts to the Memoria launcher.\r\n" +
                        "Use it to change enabled mods, mod order, and Memoria settings before launching FFIX again.\r\n";
            }

            return String.Empty;
        }

        private static String MenuLine(Boolean selected, String text)
        {
            return (selected ? " > " : "   ") + text + "\r\n";
        }

        private void RenderLiveLog()
        {
            _page = Page.LiveLog;

            SetWindowText(_windowHandle, "Memoria Dev Console - Live Log");

            StringBuilder builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine("MEMORIA DEV CONSOLE  /  LIVE LOG");
            builder.AppendLine(new String('-', 90));
            builder.AppendLine("[B / ESC] Back");
            builder.AppendLine();

            lock (_appendLock)
            {
                builder.Append(_liveLogBuffer.ToString());
            }

            SetConsoleText(builder.ToString());
            ScrollBottom();
            SetFocus(_windowHandle);
        }

        private void RenderCompileRestart()
        {
            _page = Page.CompileRestart;

            lock (_compileLock)
                _pendingCompileAppend = String.Empty;

            SetWindowText(_windowHandle, "Memoria Dev Console - Compile + Restart");

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE  /  COMPILE + RESTART\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                "Starting compiler...\r\n\r\n");

            SetFocus(_windowHandle);
        }

        private void RenderModelViewer()
        {
            _page = Page.ModelViewer;
            SetWindowText(_windowHandle, "Memoria Dev Console - Open / Close Model Viewer");

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE  /  OPEN / CLOSE MODEL VIEWER\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                "Preparing Model Viewer transition...\r\n" +
                "\r\n" +
                "All 10 field snapshots are persisted before the restart.\r\n" +
                "Open the Dev Console and choose Open / Close Model Viewer again to return to normal FFIX.\r\n" +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "[B / ESC] Back\r\n");

            SetFocus(_windowHandle);
        }

        public void SetModelViewerStatus(String status)
        {
            if (!_running || _textHandle == IntPtr.Zero)
                return;

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE  /  OPEN / CLOSE MODEL VIEWER\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                (status ?? String.Empty) +
                "\r\n\r\n" +
                "All 10 field snapshots are preserved across the restart.\r\n" +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "[B / ESC] Back\r\n");
        }

        private void RenderFieldState()
        {
            _page = Page.FieldState;

            SetWindowText(_windowHandle, "Memoria Dev Console - Field State");

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE  /  FIELD STATE\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                MenuLine(_fieldSelection == 0, "[1] Move Back") +
                MenuLine(_fieldSelection == 1, "[2] Move Forward") +
                MenuLine(_fieldSelection == 2, "[3] Set Checkpoint") +
                MenuLine(_fieldSelection == 3, "[4] Load Checkpoint") +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "DESCRIPTION\r\n" +
                "------------------------------------------------------------\r\n" +
                GetFieldStateDescription(_fieldSelection) +
                "\r\n" +
                "FIELD TIMELINE\r\n" +
                "------------------------------------------------------------\r\n" +
                "Loading...\r\n" +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "D-Pad / Arrow Keys: Navigate    A / Enter: Select    B / ESC: Back\r\n");

            SetFocus(_windowHandle);
        }

        public void SetFieldStateStatus(String status)
        {
            if (!_running)
                return;

            lock (_fieldStateLock)
                _pendingFieldStateText = status ?? String.Empty;

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_FIELD_STATE_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        private void RenderFieldStateStatus()
        {
            if (_page != Page.FieldState)
                return;

            String status;

            lock (_fieldStateLock)
                status = _pendingFieldStateText;

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE  /  FIELD STATE\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                MenuLine(_fieldSelection == 0, "[1] Move Back") +
                MenuLine(_fieldSelection == 1, "[2] Move Forward") +
                MenuLine(_fieldSelection == 2, "[3] Set Checkpoint") +
                MenuLine(_fieldSelection == 3, "[4] Load Checkpoint") +
                "\r\n" +
                "------------------------------------------------------------\r\n" +
                "DESCRIPTION\r\n" +
                "------------------------------------------------------------\r\n" +
                GetFieldStateDescription(_fieldSelection) +
                "\r\n" +
                "FIELD TIMELINE\r\n" +
                "------------------------------------------------------------\r\n" +
                status +
                "\r\n\r\n" +
                "------------------------------------------------------------\r\n" +
                "D-Pad / Arrow Keys: Navigate    A / Enter: Select    B / ESC: Back\r\n");

            SetFocus(_windowHandle);
        }

        public void ShowFieldStateNotice(String title, String message)
        {
            if (!_running || _windowHandle == IntPtr.Zero)
                return;

            MessageBox(
                _windowHandle,
                message ?? "No more field snapshots are available.",
                title ?? "Out of Field Snapshots",
                MB_OK | MB_ICONINFORMATION);
        }

        private void RenderPlaceholder(String title, String body)
        {
            SetWindowText(_windowHandle, "Memoria Dev Console - " + title.Replace(" ", ""));

            SetConsoleText(
                "\r\n" +
                "MEMORIA DEV CONSOLE  /  " + title + "\r\n" +
                "------------------------------------------------------------\r\n" +
                "\r\n" +
                body +
                "\r\n\r\n" +
                "------------------------------------------------------------\r\n" +
                "[B / ESC] Back\r\n" +
                "\r\n" +
                "> _");

            SetFocus(_windowHandle);
        }

        private static String PadCentered(String value, Int32 width)
        {
            if (value == null)
                value = String.Empty;

            if (value.Length >= width)
                return value.Substring(0, width);

            Int32 total = width - value.Length;
            Int32 left = total / 2;
            Int32 right = total - left;

            return new String(' ', left) + value + new String(' ', right);
        }

        private void DrainPendingAppend()
        {
            String text;

            lock (_appendLock)
            {
                text = _pendingAppend;
                _pendingAppend = String.Empty;

                if (!String.IsNullOrEmpty(text))
                {
                    _liveLogBuffer.Append(text);

                    if (_liveLogBuffer.Length > MaxLiveLogCharacters)
                    {
                        Int32 remove = _liveLogBuffer.Length - MaxLiveLogCharacters;
                        _liveLogBuffer.Remove(0, remove);
                    }
                }
            }

            if (String.IsNullOrEmpty(text) || _page != Page.LiveLog || _textHandle == IntPtr.Zero)
                return;

            AppendTextAtEndAndFollow(text);
        }

        private void AppendTextAtEndAndFollow(String text)
        {
            if (_textHandle == IntPtr.Zero || String.IsNullOrEmpty(text))
                return;

            // EM_SETSEL(-1,-1) only clears the current selection; it does not
            // reliably move the caret to the end. Use the actual character
            // count so every new Memoria.log chunk is appended after the
            // existing log, then force the caret into view.
            Int32 end = GetWindowTextLength(_textHandle);
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(end), new IntPtr(end));
            SendMessageText(_textHandle, EM_REPLACESEL, new IntPtr(0), text);

            end = GetWindowTextLength(_textHandle);
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(end), new IntPtr(end));
            SendMessage(_textHandle, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_textHandle, WM_VSCROLL, new IntPtr(SB_BOTTOM), IntPtr.Zero);
            InvalidateRect(_textHandle, IntPtr.Zero, true);
        }

        private void DrainPendingCompileAppend()
        {
            if (_textHandle == IntPtr.Zero || _page != Page.CompileRestart)
                return;

            String text;

            lock (_compileLock)
            {
                text = _pendingCompileAppend;
                _pendingCompileAppend = String.Empty;
            }

            if (String.IsNullOrEmpty(text))
                return;

            SendMessage(_textHandle, EM_SETSEL, new IntPtr(-1), new IntPtr(-1));
            SendMessageText(_textHandle, EM_REPLACESEL, new IntPtr(0), text);
            InvalidateRect(_textHandle, IntPtr.Zero, true);
            ScrollBottom();
        }

        private void SetConsoleText(String text)
        {
            if (_textHandle != IntPtr.Zero)
            {
                SetWindowText(_textHandle, text);
                InvalidateRect(_textHandle, IntPtr.Zero, true);
            }
        }

        private void ScrollBottom()
        {
            if (_textHandle == IntPtr.Zero)
                return;

            Int32 end = GetWindowTextLength(_textHandle);
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(end), new IntPtr(end));
            SendMessage(_textHandle, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_textHandle, WM_VSCROLL, new IntPtr(SB_BOTTOM), IntPtr.Zero);
        }

        private void ResizeTextArea(IntPtr lParam)
        {
            if (_textHandle == IntPtr.Zero)
                return;

            Int32 packed = lParam.ToInt32();
            Int32 width = packed & 0xFFFF;
            Int32 height = (packed >> 16) & 0xFFFF;

            if (width <= 0 || height <= 0)
                return;

            MoveWindow(_textHandle, 12, 12, Math.Max(100, width - 24), Math.Max(100, height - 24), true);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public UInt32 cbSize;
            public UInt32 style;
            public IntPtr lpfnWndProc;
            public Int32 cbClsExtra;
            public Int32 cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;

            [MarshalAs(UnmanagedType.LPWStr)]
            public String lpszMenuName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public String lpszClassName;

            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public Int32 x;
            public Int32 y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public UInt32 message;
            public IntPtr wParam;
            public IntPtr lParam;
            public UInt32 time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public UInt32 vkCode;
            public UInt32 scanCode;
            public UInt32 flags;
            public UInt32 time;
            public UIntPtr dwExtraInfo;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, UInt32 msg, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelKeyboardProc(Int32 nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern Int16 GetKeyState(Int32 nVirtKey);


        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            UInt32 dwExStyle,
            String lpClassName,
            String lpWindowName,
            UInt32 dwStyle,
            Int32 x,
            Int32 y,
            Int32 nWidth,
            Int32 nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern UInt16 RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern Boolean DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern Boolean ShowWindow(IntPtr hWnd, Int32 nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, UInt32 uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean RegisterHotKey(
            IntPtr hWnd,
            Int32 id,
            UInt32 fsModifiers,
            UInt32 vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean UnregisterHotKey(IntPtr hWnd, Int32 id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            Int32 idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            UInt32 dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            Int32 nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 MessageBox(
            IntPtr hWnd,
            String lpText,
            String lpCaption,
            UInt32 uType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern Boolean IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern Boolean UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern Boolean SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern Boolean PostMessage(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageText(IntPtr hWnd, UInt32 msg, IntPtr wParam, String lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowTextW")]
        private static extern Boolean SetWindowText(IntPtr hWnd, String lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean InvalidateRect(
            IntPtr hWnd,
            IntPtr lpRect,
            Boolean bErase);

        [DllImport("user32.dll")]
        private static extern Boolean MoveWindow(IntPtr hWnd, Int32 x, Int32 y, Int32 nWidth, Int32 nHeight, Boolean bRepaint);

        [DllImport("user32.dll")]
        private static extern Int32 GetMessage(out MSG lpMsg, IntPtr hWnd, UInt32 wMsgFilterMin, UInt32 wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern Boolean TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(Int32 nExitCode);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("gdi32.dll")]
        private static extern Int32 SetTextColor(IntPtr hdc, Int32 crColor);

        [DllImport("gdi32.dll")]
        private static extern Int32 SetBkColor(IntPtr hdc, Int32 crColor);

        [DllImport("gdi32.dll")]
        private static extern Int32 SetBkMode(IntPtr hdc, Int32 iBkMode);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(Int32 fnObject);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFont(
            Int32 nHeight,
            Int32 nWidth,
            Int32 nEscapement,
            Int32 nOrientation,
            Int32 fnWeight,
            UInt32 fdwItalic,
            UInt32 fdwUnderline,
            UInt32 fdwStrikeOut,
            UInt32 fdwCharSet,
            UInt32 fdwOutputPrecision,
            UInt32 fdwClipPrecision,
            UInt32 fdwQuality,
            UInt32 fdwPitchAndFamily,
            String lpszFace);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(String lpModuleName);
    }
}
