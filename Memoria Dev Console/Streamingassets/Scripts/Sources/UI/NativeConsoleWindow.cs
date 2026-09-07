using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
            FieldState,
            AssetDumper,
            Warp,
            SoundBrowser
        }

        private enum SoundBrowserMode
        {
            Categories,
            Entries,
            SfxGroups,
            SfxEntries,
            FieldGroups,
            FieldEntries,
            FilterEntry,
            AliasEntry
        }

        private enum FieldSaveSubMode
        {
            Normal,
            TitleEntry,
            SaveList
        }

        private const Int32 CW_USEDEFAULT = unchecked((Int32)0x80000000);

        private const UInt32 WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const UInt32 WS_CHILD = 0x40000000;
        private const UInt32 WS_VISIBLE = 0x10000000;
        private const UInt32 WS_VSCROLL = 0x00200000;
        private const UInt32 SS_BITMAP = 0x0000000E;
        private const UInt32 SS_LEFT = 0x00000000;
        private const UInt32 STM_SETIMAGE = 0x0172;
        private const Int32 IMAGE_BITMAP = 0;

        // Save-state preview strip, anchored along the bottom of the window instead of a
        // side panel: bigger screenshot on the left, up to 4 party portraits + text to its right.
        private const Int32 PreviewStripHeight = 260;
        private const Int32 ThumbnailWidthPx = 320;
        private const Int32 ThumbnailHeightPx = 240;
        private const Int32 PartyTextWidth = 260;

        // Party portraits: cropped from a bundled reference atlas (see GetPortraitAtlasPath),
        // never from whatever texture mod happens to be active in the live game, so the
        // known-good pixel coordinates below always line up regardless of graphics mods.
        private const Int32 MaxPortraitSlots = 4;
        private const Int32 PortraitWidthPx = 64;
        private const Int32 PortraitHeightPx = 92;
        private const Int32 PortraitGap = 6;
        private const Int32 PortraitSourceWidth = 132;
        private const Int32 PortraitSourceHeight = 190;

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
        private const UInt32 WM_KEYDOWN = 0x0100;
        private const UInt32 WM_KEYUP = 0x0101;
        private const UInt32 WM_SYSKEYDOWN = 0x0104;
        private const UInt32 WM_SYSKEYUP = 0x0105;
        private const UInt32 WM_SETFONT = 0x0030;
        private const UInt32 WM_SETTEXT = 0x000C;
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
        private const UInt32 WM_APP_ASSET_DUMP_TEXT = 0x8007;
        private const UInt32 WM_APP_WARP_TEXT = 0x8008;
        private const UInt32 WM_APP_SOUND_BROWSER_TEXT = 0x8009;

        private const Int32 SB_BOTTOM = 7;
        private const Int32 TRANSPARENT = 1;
        private const Int32 OPAQUE = 2;
        private const Int32 DEFAULT_GUI_FONT = 17;
        private const Int32 BLACK_BRUSH = 4;

        private const Int32 VK_ESCAPE = 0x1B;
        private const Int32 VK_A = 0x41;
        private const Int32 VK_CONTROL = 0x11;
        private const Int32 VK_RETURN = 0x0D;
        private const Int32 VK_BACK = 0x08;
        private const Int32 VK_UP = 0x26;
        private const Int32 VK_DOWN = 0x28;
        private const Int32 VK_LEFT = 0x25;
        private const Int32 VK_RIGHT = 0x27;
        private const UInt32 VK_F10 = 0x79;
        private const Int32 VK_1 = 0x31;
        private const Int32 VK_2 = 0x32;
        private const Int32 VK_3 = 0x33;
        private const Int32 VK_4 = 0x34;
        private const Int32 VK_5 = 0x35;
        private const Int32 VK_6 = 0x36;
        private const Int32 VK_7 = 0x37;
        private const Int32 VK_8 = 0x38;
        private const Int32 VK_9 = 0x39;
        private const Int32 VK_S = 0x53;
        private const Int32 VK_O = 0x4F;
        private const Int32 VK_R = 0x52;
        private const Int32 VK_F = 0x46;
        private const Int32 VK_N = 0x4E;
        private const Int32 VK_DELETE = 0x2E;
        private const Int32 WH_KEYBOARD_LL = 13;
        private const UInt32 MB_OK = 0x00000000;
        private const UInt32 MB_ICONINFORMATION = 0x00000040;
        private const UInt32 WS_EX_TOPMOST = 0x00000008;
        private const Int32 HOTKEY_TOGGLE = 0x4D44;


        private Thread _thread;
        private volatile Boolean _running;
        private volatile Boolean _ready;

        private IntPtr _windowHandle;
        private IntPtr _textHandle;
        private IntPtr _thumbnailHandle;
        private IntPtr _thumbnailBitmap;
        private IntPtr _partyTextHandle;
        private readonly IntPtr[] _portraitHandles = new IntPtr[MaxPortraitSlots];
        private readonly IntPtr[] _portraitBitmaps = new IntPtr[MaxPortraitSlots];
        private IntPtr _portraitAtlasImage;
        private Boolean _portraitAtlasLoadAttempted;
        private UIntPtr _gdiplusToken;
        private Boolean _gdiplusReady;
        private Boolean _thumbnailVisible;
        private IntPtr _blackBrush;
        private IntPtr _fontHandle;

        private Page _page = Page.Home;

        private Int32 _homeSelection;
        private Int32 _fieldSelection;
        private FieldSaveSubMode _fieldSaveSubMode = FieldSaveSubMode.Normal;
        private String _saveTitleEntryText = String.Empty;
        private Int32 _saveListSelection;
        private List<NamedSaveSummary> _namedSaves = new List<NamedSaveSummary>();
        private readonly Object _namedSaveLock = new Object();
        private String _pendingNamedSaveStatus = "No saved states yet.";
        private Int32 _assetSelection;
        private Int32 _warpSelection;
        private Int32 _warpDisc;
        private Boolean _warpSectionMode;
        private Boolean _warpEntryActive;
        private Int32 _warpEntryStage;
        private String _warpFieldEntryText = String.Empty;
        private String _warpScenarioEntryText = String.Empty;
        private Boolean _assetIdEntryActive;
        private Boolean _assetIdEntryIsField;
        private String _assetIdEntryText = String.Empty;
        private DevSoundCatalog _soundCatalog;
        private SoundBrowserMode _soundMode = SoundBrowserMode.Categories;
        private SoundBrowserMode _soundModeBeforeFilter = SoundBrowserMode.Categories;
        private Int32 _soundCategorySelection;
        private Int32 _soundEntrySelection;
        private DevSfxSoundGroup _soundActiveSfxGroup;
        private DevFieldSoundGroup _soundActiveFieldGroup;
        private DevSoundEntry _soundAliasEntry;
        private String _soundFilter = String.Empty;
        private String _soundFilterEntryText = String.Empty;
        private String _soundAliasEntryText = String.Empty;
        private const Int32 SoundPageSize = 11;

        // "List memory": remembers where the cursor was on a list you back out of, so
        // returning to it later (e.g. Field Locations -> a field's sounds -> Back)
        // lands you back where you left off instead of snapping to the first page.
        private Int32 _soundFieldGroupsCursorMemory;
        private readonly Dictionary<Int32, Int32> _soundCategoryEntryCursorMemory = new Dictionary<Int32, Int32>();
        private readonly Dictionary<String, Int32> _soundFieldEntryCursorMemory = new Dictionary<String, Int32>();

        private readonly Object _appendLock = new Object();
        private String _pendingAppend = String.Empty;

        // Keep our own log buffer so leaving Live Log and coming back does NOT blank it.
        private readonly StringBuilder _liveLogBuffer = new StringBuilder();
        private const Int32 MaxLiveLogCharacters = 500000;

        private readonly Object _compileLock = new Object();
        private String _pendingCompileAppend = String.Empty;
        private readonly Object _fieldStateLock = new Object();
        private String _pendingFieldStateText = String.Empty;
        private readonly Object _assetDumpLock = new Object();
        private String _pendingAssetDumpText = "Ready. Select one of the complete-game dump operations above.";
        private readonly Object _warpLock = new Object();
        private String _pendingWarpText = "Ready. Choose a disc, then a major story section. Story warps set field + scenario + expected party while preserving unrelated/custom quest state.";
        private readonly Object _soundBrowserLock = new Object();
        private String _pendingSoundBrowserStatus = "Loading audio metadata...";

        public event Action CompileRestartRequested;
        public event Action ModelViewerRequested;
        public event Action HardResetRequested;
        public event Action ResetToLauncherRequested;
        public event Action FieldStateMoveBackwardRequested;
        public event Action FieldStateMoveForwardRequested;
        public event Action FieldStateSetCheckpointRequested;
        public event Action FieldStateLoadCheckpointRequested;
        public event Action FieldStatePageOpened;
        public event Action<String> SaveStateCaptureRequested;
        public event Action<String> SaveStateLoadRequested;
        public event Action<String> SaveStateDeleteRequested;
        public event Action SaveStatesPageOpened;
        public event Action FieldBackgroundDumpRequested;
        public event Action TextureDumpRequested;
        public event Action<Int32> FieldBackgroundByIdRequested;
        public event Action<Int32> TextureByModelIdRequested;
        public event Action CurrentBattleBackgroundDumpRequested;
        public event Action<StoryWarpPreset> WarpRequested;
        public event Action SoundBrowserOpened;
        public event Action<DevSoundEntry> SoundPreviewRequested;
        public event Action SoundStopRequested;
        public event Action<Int32> SoundSfxLoadRequested;
        public event Action<DevSoundEntry, String> SoundAliasSaveRequested;

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

                // Save-state preview strip: screenshot + party text, both created hidden.
                // ShowSaveThumbnail / HideSaveThumbnail (called from the relevant Render*
                // methods) control visibility and content together.
                _thumbnailHandle = CreateWindowEx(
                    0,
                    "STATIC",
                    "",
                    WS_CHILD | SS_BITMAP,
                    12,
                    12,
                    ThumbnailWidthPx,
                    ThumbnailHeightPx,
                    _windowHandle,
                    IntPtr.Zero,
                    windowClass.hInstance,
                    IntPtr.Zero);

                _partyTextHandle = CreateWindowEx(
                    0,
                    "STATIC",
                    "",
                    WS_CHILD | SS_LEFT,
                    12,
                    12,
                    PartyTextWidth,
                    ThumbnailHeightPx,
                    _windowHandle,
                    IntPtr.Zero,
                    windowClass.hInstance,
                    IntPtr.Zero);

                if (_partyTextHandle != IntPtr.Zero)
                    SendMessage(_partyTextHandle, WM_SETFONT, _fontHandle, new IntPtr(1));

                for (Int32 i = 0; i < MaxPortraitSlots; i++)
                {
                    _portraitHandles[i] = CreateWindowEx(
                        0,
                        "STATIC",
                        "",
                        WS_CHILD | SS_BITMAP,
                        12,
                        12,
                        PortraitWidthPx,
                        PortraitHeightPx,
                        _windowHandle,
                        IntPtr.Zero,
                        windowClass.hInstance,
                        IntPtr.Zero);
                }

                GdiplusStartupInput gdiInput = new GdiplusStartupInput();
                gdiInput.GdiplusVersion = 1;
                _gdiplusReady = GdiplusStartup(out _gdiplusToken, ref gdiInput, IntPtr.Zero) == 0;
                if (!_gdiplusReady)
                    _lastError = "GdiplusStartup failed; save-state thumbnails will be unavailable.";

                RenderHome();

                ShowWindow(_windowHandle, SW_HIDE);
                UpdateWindow(_windowHandle);

            if (!RegisterHotKey(_windowHandle, HOTKEY_TOGGLE, 0, VK_F10))
                _lastError = "RegisterHotKey(F10) failed. Win32 error: " + Marshal.GetLastWin32Error();

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
                if (_thumbnailBitmap != IntPtr.Zero)
                {
                    DeleteObject(_thumbnailBitmap);
                    _thumbnailBitmap = IntPtr.Zero;
                }

                for (Int32 i = 0; i < MaxPortraitSlots; i++)
                {
                    if (_portraitBitmaps[i] != IntPtr.Zero)
                    {
                        DeleteObject(_portraitBitmaps[i]);
                        _portraitBitmaps[i] = IntPtr.Zero;
                    }
                    _portraitHandles[i] = IntPtr.Zero;
                }

                if (_portraitAtlasImage != IntPtr.Zero)
                {
                    GdipDisposeImage(_portraitAtlasImage);
                    _portraitAtlasImage = IntPtr.Zero;
                }

                if (_gdiplusReady)
                {
                    GdiplusShutdown(_gdiplusToken);
                    _gdiplusReady = false;
                }

                _ready = false;
                _running = false;
                _windowHandle = IntPtr.Zero;
                _textHandle = IntPtr.Zero;
                _thumbnailHandle = IntPtr.Zero;
                _partyTextHandle = IntPtr.Zero;
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

                case WM_APP_ASSET_DUMP_TEXT:
                    RenderAssetDumper();
                    return IntPtr.Zero;

                case WM_APP_WARP_TEXT:
                    RenderWarp();
                    return IntPtr.Zero;

                case WM_APP_SOUND_BROWSER_TEXT:
                    if (_page == Page.SoundBrowser)
                        RenderSoundBrowser();
                    return IntPtr.Zero;

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
            if (_page == Page.AssetDumper && !_assetIdEntryActive && (ch == 'o' || ch == 'O'))
            {
                OpenAssetOutputFolder();
                return;
            }

            if (_page == Page.CompileRestart && (ch == 'r' || ch == 'R'))
            {
                RetryCompile();
                return;
            }

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
                    case '7':
                        _homeSelection = 6;
                        ActivateHomeSelection();
                        return;
                    case '8':
                        _homeSelection = 7;
                        ActivateHomeSelection();
                        return;
                    case '9':
                        _homeSelection = 8;
                        ActivateHomeSelection();
                        return;
                }
            }
            else if (_page == Page.AssetDumper)
            {
                switch (ch)
                {
                    case '1':
                        _assetSelection = 0;
                        ActivateAssetSelection();
                        return;
                    case '2':
                        _assetSelection = 1;
                        ActivateAssetSelection();
                        return;
                    case '3':
                        _assetSelection = 2;
                        ActivateAssetSelection();
                        return;
                    case '4':
                        _assetSelection = 3;
                        ActivateAssetSelection();
                        return;
                    case '5':
                        _assetSelection = 4;
                        ActivateAssetSelection();
                        return;
                }
            }
            else if (_page == Page.Warp && !_warpEntryActive)
            {
                Int32 maxShortcut = _warpSectionMode ? Math.Min(9, StoryWarpCatalog.GetDisc(_warpDisc).Length) : 5;
                if (ch >= '1' && ch <= '9' && (ch - '1') < maxShortcut)
                {
                    _warpSelection = ch - '1';
                    ActivateWarpSelection();
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
            if (_page == Page.SoundBrowser && _soundMode == SoundBrowserMode.AliasEntry)
            {
                if (vk == VK_ESCAPE)
                {
                    _soundMode = _soundModeBeforeFilter;
                    RenderSoundBrowser();
                    return true;
                }
                if (vk == VK_RETURN)
                {
                    String alias = _soundAliasEntryText.Trim();
                    if (_soundAliasEntry != null)
                    {
                        _soundAliasEntry.Identity = alias;
                        Action<DevSoundEntry, String> aliasHandler = SoundAliasSaveRequested;
                        if (aliasHandler != null)
                            aliasHandler(_soundAliasEntry, alias);
                    }
                    _soundMode = _soundModeBeforeFilter;
                    RenderSoundBrowser();
                    return true;
                }
                if (vk == VK_BACK)
                {
                    if (_soundAliasEntryText.Length > 0)
                        _soundAliasEntryText = _soundAliasEntryText.Substring(0, _soundAliasEntryText.Length - 1);
                    RenderSoundBrowser();
                    return true;
                }
                if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A) || vk == 0x20 || vk == 0xBD)
                {
                    if (_soundAliasEntryText.Length < 80)
                        _soundAliasEntryText += vk == 0x20 ? " " : vk == 0xBD ? "-" : ((Char)vk).ToString();
                    RenderSoundBrowser();
                    return true;
                }
            }

            if (_page == Page.SoundBrowser && _soundMode == SoundBrowserMode.FilterEntry)
            {
                if (vk == VK_ESCAPE)
                {
                    _soundMode = _soundModeBeforeFilter;
                    RenderSoundBrowser();
                    return true;
                }
                if (vk == VK_RETURN)
                {
                    _soundFilter = _soundFilterEntryText.Trim();
                    _soundMode = _soundModeBeforeFilter;
                    _soundEntrySelection = 0;
                    RenderSoundBrowser();
                    return true;
                }
                if (vk == VK_BACK)
                {
                    if (_soundFilterEntryText.Length > 0)
                        _soundFilterEntryText = _soundFilterEntryText.Substring(0, _soundFilterEntryText.Length - 1);
                    RenderSoundBrowser();
                    return true;
                }
                if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A) || vk == 0x20 || vk == 0xBD)
                {
                    if (_soundFilterEntryText.Length < 60)
                        _soundFilterEntryText += vk == 0x20 ? " " : vk == 0xBD ? "-" : ((Char)vk).ToString();
                    RenderSoundBrowser();
                    return true;
                }
            }

            if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.TitleEntry)
            {
                if (vk == VK_ESCAPE)
                {
                    _fieldSaveSubMode = FieldSaveSubMode.Normal;
                    _saveTitleEntryText = String.Empty;
                    RenderFieldState();
                    return true;
                }
                if (vk == VK_RETURN)
                {
                    SubmitSaveTitleEntry();
                    return true;
                }
                if (vk == VK_BACK)
                {
                    if (_saveTitleEntryText.Length > 0)
                        _saveTitleEntryText = _saveTitleEntryText.Substring(0, _saveTitleEntryText.Length - 1);
                    RenderFieldState();
                    return true;
                }
                if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A) || vk == 0x20 || vk == 0xBD)
                {
                    if (_saveTitleEntryText.Length < 48)
                        _saveTitleEntryText += vk == 0x20 ? " " : vk == 0xBD ? "-" : ((Char)vk).ToString();
                    RenderFieldState();
                    return true;
                }

                // Any other key while naming a save is swallowed rather than falling
                // through to normal page navigation/shortcuts.
                return true;
            }

            if (vk == VK_ESCAPE)
            {
                if (_page == Page.Warp && _warpEntryActive)
                {
                    _warpEntryActive = false;
                    _warpEntryStage = 0;
                    _warpFieldEntryText = String.Empty;
                    _warpScenarioEntryText = String.Empty;
                    RenderWarp();
                    return true;
                }
                if (_page == Page.AssetDumper && _assetIdEntryActive)
                {
                    _assetIdEntryActive = false;
                    _assetIdEntryText = String.Empty;
                    RenderAssetDumper();
                    return true;
                }
                if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.SaveList)
                {
                    _fieldSaveSubMode = FieldSaveSubMode.Normal;
                    RenderFieldState();
                    return true;
                }
                // Live Log is passive while FFIX owns focus. The user must click
                // the Dev Console first before ESC is allowed to leave Live Log.
                if (_page == Page.LiveLog && !IsConsoleForeground())
                    return false;

                NavigateBack();
                return true;
            }

            if (vk == VK_UP)
            {
                MoveSelection(-1);
                return true;
            }

            if (vk == VK_DOWN)
            {
                MoveSelection(1);
                return true;
            }

            if (_page == Page.SoundBrowser && (vk == VK_LEFT || vk == VK_RIGHT))
            {
                MoveSoundSelectionByPage(vk == VK_LEFT ? -1 : 1);
                return true;
            }

            if (vk == VK_RETURN)
            {
                if (_page == Page.Warp && _warpEntryActive)
                    AdvanceOrSubmitWarpEntry();
                else if (_page == Page.AssetDumper && _assetIdEntryActive)
                    SubmitAssetIdEntry();
                else
                    ActivateSelection();
                return true;
            }

            if (_page == Page.Warp && _warpEntryActive)
            {
                if (vk == VK_BACK)
                {
                    if (_warpEntryStage == 0 && _warpFieldEntryText.Length > 0)
                        _warpFieldEntryText = _warpFieldEntryText.Substring(0, _warpFieldEntryText.Length - 1);
                    else if (_warpEntryStage == 1 && _warpScenarioEntryText.Length > 0)
                        _warpScenarioEntryText = _warpScenarioEntryText.Substring(0, _warpScenarioEntryText.Length - 1);
                    RenderWarp();
                    return true;
                }

                if (vk >= 0x30 && vk <= 0x39)
                {
                    if (_warpEntryStage == 0 && _warpFieldEntryText.Length < 8)
                        _warpFieldEntryText += (Char)vk;
                    else if (_warpEntryStage == 1 && _warpScenarioEntryText.Length < 8)
                        _warpScenarioEntryText += (Char)vk;
                    RenderWarp();
                    return true;
                }
            }

            if (_page == Page.AssetDumper && _assetIdEntryActive)
            {
                if (vk == VK_BACK)
                {
                    if (_assetIdEntryText.Length > 0)
                        _assetIdEntryText = _assetIdEntryText.Substring(0, _assetIdEntryText.Length - 1);
                    RenderAssetDumper();
                    return true;
                }

                if (vk >= 0x30 && vk <= 0x39)
                {
                    if (_assetIdEntryText.Length < 8)
                        _assetIdEntryText += (Char)vk;
                    RenderAssetDumper();
                    return true;
                }
            }

            if (_page == Page.AssetDumper && !_assetIdEntryActive && vk == VK_O)
            {
                OpenAssetOutputFolder();
                return true;
            }

            if (_page == Page.CompileRestart && vk == VK_R)
            {
                RetryCompile();
                return true;
            }

            if (_page == Page.SoundBrowser && vk == VK_F)
            {
                BeginSoundFilter();
                return true;
            }

            if (_page == Page.SoundBrowser && vk == VK_S)
            {
                Action stopHandler = SoundStopRequested;
                if (stopHandler != null)
                    stopHandler();
                SetSoundBrowserStatus("Stopped all preview audio.");
                return true;
            }

            if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.SaveList && vk == VK_N)
            {
                _fieldSaveSubMode = FieldSaveSubMode.TitleEntry;
                _saveTitleEntryText = String.Empty;
                RenderFieldState();
                return true;
            }

            if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.SaveList && vk == VK_DELETE)
            {
                NamedSaveSummary selectedForDelete = GetSelectedNamedSave();
                if (selectedForDelete != null)
                {
                    Action<String> deleteHandler = SaveStateDeleteRequested;
                    if (deleteHandler != null)
                        deleteHandler(selectedForDelete.Id);
                }
                return true;
            }

            if (_page == Page.Home)
            {
                if (vk >= VK_1 && vk <= VK_9)
                {
                    _homeSelection = vk - VK_1;
                    ActivateHomeSelection();
                    return true;
                }
            }
            else if (_page == Page.AssetDumper)
            {
                if (vk >= VK_1 && vk <= VK_5)
                {
                    _assetSelection = vk - VK_1;
                    ActivateAssetSelection();
                    return true;
                }
            }
            else if (_page == Page.Warp && !_warpEntryActive)
            {
                Int32 maxShortcut = _warpSectionMode ? Math.Min(9, StoryWarpCatalog.GetDisc(_warpDisc).Length) : 5;
                if (vk >= VK_1 && vk <= VK_9 && (vk - VK_1) < maxShortcut)
                {
                    _warpSelection = vk - VK_1;
                    ActivateWarpSelection();
                    return true;
                }
            }
            else if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.Normal)
            {
                if (vk >= VK_1 && vk <= VK_6)
                {
                    _fieldSelection = vk - VK_1;
                    ActivateFieldSelection();
                    return true;
                }
            }

            return false;
        }

        private Boolean IsConsoleForeground()
        {
            if (_windowHandle == IntPtr.Zero)
                return false;

            return GetForegroundWindow() == _windowHandle;
        }

        private void MoveSelection(Int32 delta)
        {
            if (_page == Page.Home)
            {
                _homeSelection = WrapSelection(_homeSelection + delta, 9);
                RenderHome();
            }
            else if (_page == Page.Warp && !_warpEntryActive)
            {
                Int32 warpCount = _warpSectionMode ? StoryWarpCatalog.GetDisc(_warpDisc).Length : 5;
                _warpSelection = WrapSelection(_warpSelection + delta, warpCount);
                RenderWarp();
            }
            else if (_page == Page.AssetDumper)
            {
                _assetSelection = WrapSelection(_assetSelection + delta, 5);
                RenderAssetDumper();
            }
            else if (_page == Page.FieldState)
            {
                if (_fieldSaveSubMode == FieldSaveSubMode.SaveList)
                {
                    Int32 saveCount = GetNamedSaveCount();
                    _saveListSelection = WrapSelection(_saveListSelection + delta, saveCount);
                    RenderFieldStateStatus();
                }
                else if (_fieldSaveSubMode == FieldSaveSubMode.Normal)
                {
                    _fieldSelection = WrapSelection(_fieldSelection + delta, 6);
                    RenderFieldStateStatus();
                }
            }
            else if (_page == Page.SoundBrowser && _soundMode != SoundBrowserMode.FilterEntry && _soundMode != SoundBrowserMode.AliasEntry)
            {
                Int32 count = GetSoundSelectionCount();
                _soundEntrySelection = WrapSelection(_soundEntrySelection + delta, count);
                if (_soundMode == SoundBrowserMode.Categories)
                    _soundCategorySelection = _soundEntrySelection;
                RenderSoundBrowser();
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

        private void ActivateSelection()
        {
            if (_page == Page.Home)
                ActivateHomeSelection();
            else if (_page == Page.Warp && !_warpEntryActive)
                ActivateWarpSelection();
            else if (_page == Page.AssetDumper)
                ActivateAssetSelection();
            else if (_page == Page.FieldState)
            {
                if (_fieldSaveSubMode == FieldSaveSubMode.SaveList)
                    ActivateSaveListSelection();
                else if (_fieldSaveSubMode == FieldSaveSubMode.Normal)
                    ActivateFieldSelection();
            }
            else if (_page == Page.SoundBrowser)
                ActivateSoundSelection();
        }

        private void NavigateBack()
        {
            if (_page == Page.Home)
            {
                // Escape must be identical to clicking the native X.
                // We are already on the Win32 window thread here, so hide synchronously
                // instead of posting another app message.
                if (_windowHandle != IntPtr.Zero)
                    ShowWindow(_windowHandle, SW_HIDE);
                return;
            }

            if (_page == Page.Warp && _warpSectionMode)
            {
                _warpSectionMode = false;
                _warpSelection = Math.Max(0, _warpDisc - 1);
                _warpDisc = 0;
                RenderWarp();
                return;
            }

            if (_page == Page.SoundBrowser)
            {
                if (_soundMode == SoundBrowserMode.FilterEntry)
                {
                    _soundMode = _soundModeBeforeFilter;
                    RenderSoundBrowser();
                    return;
                }
                if (_soundMode == SoundBrowserMode.AliasEntry)
                {
                    _soundMode = _soundModeBeforeFilter;
                    RenderSoundBrowser();
                    return;
                }
                if (_soundMode == SoundBrowserMode.SfxEntries)
                {
                    _soundMode = SoundBrowserMode.SfxGroups;
                    _soundEntrySelection = 0;
                    RenderSoundBrowser();
                    return;
                }
                if (_soundMode == SoundBrowserMode.FieldEntries)
                {
                    if (_soundActiveFieldGroup != null)
                        _soundFieldEntryCursorMemory[_soundActiveFieldGroup.Field] = _soundEntrySelection;
                    _soundMode = SoundBrowserMode.FieldGroups;
                    _soundEntrySelection = _soundFieldGroupsCursorMemory;
                    RenderSoundBrowser();
                    return;
                }
                if (_soundMode == SoundBrowserMode.Entries || _soundMode == SoundBrowserMode.SfxGroups || _soundMode == SoundBrowserMode.FieldGroups)
                {
                    if (_soundMode == SoundBrowserMode.Entries)
                        _soundCategoryEntryCursorMemory[_soundCategorySelection] = _soundEntrySelection;

                    _soundMode = SoundBrowserMode.Categories;
                    _soundEntrySelection = _soundCategorySelection;
                    _soundFilter = String.Empty;
                    RenderSoundBrowser();
                    return;
                }
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
                    RenderFieldState();

                    Action fieldOpenedHandler = FieldStatePageOpened;
                    if (fieldOpenedHandler != null)
                        fieldOpenedHandler();
                    return;

                case 5:
                    RenderAssetDumper();
                    return;

                case 6:
                    Action launcherHandler = ResetToLauncherRequested;
                    if (launcherHandler != null)
                        launcherHandler();
                    return;

                case 7:
                    RenderWarp();
                    return;

                case 8:
                    _soundMode = SoundBrowserMode.Categories;
                    _soundEntrySelection = _soundCategorySelection;
                    RenderSoundBrowser();
                    Action soundOpenedHandler = SoundBrowserOpened;
                    if (soundOpenedHandler != null)
                        soundOpenedHandler();
                    return;
            }
        }

        private void ActivateWarpSelection()
        {
            if (!_warpSectionMode)
            {
                if (_warpSelection >= 0 && _warpSelection <= 3)
                {
                    _warpDisc = _warpSelection + 1;
                    _warpSectionMode = true;
                    _warpSelection = 0;
                    RenderWarp();
                    return;
                }

                if (_warpSelection == 4)
                {
                    BeginWarpEntry();
                    return;
                }

                return;
            }

            StoryWarpPreset[] presets = StoryWarpCatalog.GetDisc(_warpDisc);
            if (_warpSelection < 0 || _warpSelection >= presets.Length)
                return;

            RequestWarp(presets[_warpSelection]);
        }

        private void BeginWarpEntry()
        {
            _warpEntryActive = true;
            _warpEntryStage = 0;
            _warpFieldEntryText = String.Empty;
            _warpScenarioEntryText = String.Empty;
            RenderWarp();
        }

        private void AdvanceOrSubmitWarpEntry()
        {
            Int32 value;

            if (_warpEntryStage == 0)
            {
                if (!Int32.TryParse(_warpFieldEntryText, out value))
                {
                    SetWarpStatus("Enter a numeric Field ID first.");
                    return;
                }

                _warpEntryStage = 1;
                RenderWarp();
                return;
            }

            Int32 fieldId;
            Int32 scenario;
            if (!Int32.TryParse(_warpFieldEntryText, out fieldId) ||
                !Int32.TryParse(_warpScenarioEntryText, out scenario))
            {
                SetWarpStatus("Enter numeric Field ID and ScenarioCounter values.");
                return;
            }

            _warpEntryActive = false;
            _warpEntryStage = 0;
            RequestWarp(new StoryWarpPreset("Manual warp", 0, fieldId, scenario));
        }

        private void RequestWarp(StoryWarpPreset preset)
        {
            if (preset == null)
                return;

            SetWarpStatus(preset.Name + " requested -> Field " + preset.FieldId + " / Scenario " + preset.Scenario + ".");

            Action<StoryWarpPreset> handler = WarpRequested;
            if (handler != null)
                handler(preset);

            // Warps execute on Unity's main thread. Hide the console immediately so
            // the Dev Console input-freeze layer is released before the field reload.
            Hide();
        }


        private void ActivateAssetSelection()
        {
            if (_assetSelection == 0)
            {
                SetAssetDumpStatus("Field Background Dump requested. Starting complete game dump...");
                Action handler = FieldBackgroundDumpRequested;
                if (handler != null)
                    handler();
            }
            else if (_assetSelection == 1)
            {
                SetAssetDumpStatus("2D Texture Dump requested. Starting complete game dump...");
                Action handler = TextureDumpRequested;
                if (handler != null)
                    handler();
            }
            else if (_assetSelection == 2)
            {
                BeginAssetIdEntry(true);
            }
            else if (_assetSelection == 3)
            {
                BeginAssetIdEntry(false);
            }
            else if (_assetSelection == 4)
            {
                SetAssetDumpStatus("Current Battle Background PNG dump requested...");
                Action handler = CurrentBattleBackgroundDumpRequested;
                if (handler != null)
                    handler();
            }
        }

        private void BeginAssetIdEntry(Boolean isField)
        {
            _assetIdEntryActive = true;
            _assetIdEntryIsField = isField;
            _assetIdEntryText = String.Empty;
            RenderAssetDumper();
        }

        private void SubmitAssetIdEntry()
        {
            Int32 id;
            if (!Int32.TryParse(_assetIdEntryText, out id))
            {
                SetAssetDumpStatus("Enter a numeric " + (_assetIdEntryIsField ? "field" : "model") + " ID first.");
                return;
            }

            Boolean isField = _assetIdEntryIsField;
            _assetIdEntryActive = false;
            _assetIdEntryText = String.Empty;

            if (isField)
            {
                SetAssetDumpStatus("Field ID " + id + " requested...");
                Action<Int32> handler = FieldBackgroundByIdRequested;
                if (handler != null)
                    handler(id);
            }
            else
            {
                SetAssetDumpStatus("Model ID " + id + " requested...");
                Action<Int32> handler = TextureByModelIdRequested;
                if (handler != null)
                    handler(id);
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

                case 4:
                    _fieldSaveSubMode = FieldSaveSubMode.TitleEntry;
                    _saveTitleEntryText = String.Empty;
                    RenderFieldState();
                    return;

                case 5:
                    _fieldSaveSubMode = FieldSaveSubMode.SaveList;
                    _saveListSelection = 0;
                    RenderFieldState();

                    Action savesOpenedHandler = SaveStatesPageOpened;
                    if (savesOpenedHandler != null)
                        savesOpenedHandler();
                    return;
            }
        }

        private void SubmitSaveTitleEntry()
        {
            String title = _saveTitleEntryText.Trim();
            if (title.Length == 0)
                title = "Untitled";

            _fieldSaveSubMode = FieldSaveSubMode.SaveList;
            _saveListSelection = 0;

            lock (_namedSaveLock)
                _pendingNamedSaveStatus = "Capturing screenshot and saving \"" + title + "\"...";

            RenderFieldState();

            Action<String> captureHandler = SaveStateCaptureRequested;
            if (captureHandler != null)
                captureHandler(title);
        }

        private void ActivateSaveListSelection()
        {
            NamedSaveSummary selected = GetSelectedNamedSave();
            if (selected == null)
                return;

            lock (_namedSaveLock)
                _pendingNamedSaveStatus = "Loading \"" + selected.Title + "\"...";

            RenderFieldStateStatus();

            Action<String> loadHandler = SaveStateLoadRequested;
            if (loadHandler != null)
                loadHandler(selected.Id);
        }

        private void RenderHome()
        {
            _page = Page.Home;
            HideSaveThumbnail();

            SetWindowText(_windowHandle, "Memoria Dev Console");

            SetConsoleText(
                "\r\n" +
                DoubleRule() +
                " __  __ _____ __  __  ___  ____  ___    _\r\n" +
                "|  \\/  | ____|  \\/  |/ _ \\|  _ \\|_ _|  / \\ \r\n" +
                "| |\\/| |  _| | |\\/| | | | | |_) || |  / _ \\ \r\n" +
                "| |  | | |___| |  | | |_| |  _ < | | / ___ \\ \r\n" +
                "|_|  |_|_____|_|  |_|\\___/|_| \\_\\___/_/   \\_\\\r\n" +
                "\r\n" +
                PadCentered("D E V   C O N S O L E              by Cheddarbung", ConsoleWidth) + "\r\n" +
                DoubleRule() +
                "\r\n" +
                MenuLine(_homeSelection == 0, "[1] Live Log") +
                MenuLine(_homeSelection == 1, "[2] Compile + Restart") +
                MenuLine(_homeSelection == 2, "[3] Open / Close Model Viewer") +
                MenuLine(_homeSelection == 3, "[4] Hard Reset") +
                MenuLine(_homeSelection == 4, "[5] Save States") +
                MenuLine(_homeSelection == 5, "[6] Asset Dumper") +
                MenuLine(_homeSelection == 6, "[7] Reset to Launcher") +
                MenuLine(_homeSelection == 7, "[8] Warp / Story Jump (EXPERIMENTAL)") +
                MenuLine(_homeSelection == 8, "[9] Sound Browser") +
                "\r\n" +
                Rule() +
                "DESCRIPTION\r\n" +
                Rule() +
                GetHomeDescription(_homeSelection) +
                "\r\n" +
                Footer(NavigateHint, SelectHint, "F10: Toggle    Number keys [1-9]: Shortcuts", "ESC: Close"));

            SetFocus(_windowHandle);
        }

        private static String GetFieldStateDescription(Int32 selection)
        {
            switch (selection)
            {
                case 0:
                    return
                        "Moves backward through the rolling history of the last 10 automatically captured field save states.\r\n" +
                        "Use it to return to older test locations without loading a normal save or replaying the game.\r\n";

                case 1:
                    return
                        "Moves forward through the timeline after you have moved backward.\r\n" +
                        "Use it to return toward newer captured states without changing the saved history.\r\n";

                case 2:
                    return
                        "Pins your current field save state as one dedicated checkpoint.\r\n" +
                        "The checkpoint is separate from the rolling 10-state history, so new captures cannot push it out.\r\n";

                case 3:
                    return
                        "Loads the dedicated checkpoint directly at any time.\r\n" +
                        "Useful when one boss, cutscene, or script setup is your main test point and you always want a fast return to it.\r\n";

                case 4:
                    return
                        "Captures a permanent, titled save with a full screenshot. Unlike the rolling history and\r\n" +
                        "checkpoint above, these never get overwritten automatically and are written to disk right\r\n" +
                        "away — useful real save slots when your actual in-game saves are full.\r\n";

                case 5:
                    return
                        "Lists every saved state you have made. Load one instantly, open its screenshot to see it\r\n" +
                        "before committing, or delete ones you no longer need.\r\n";
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
                        "No launcher or restart on entry; closing returns directly to the current field when possible.\r\n" +
                        "While the viewer is open, F11 dumps the current model to FBX. Already re-oriented/fixed\r\n" +
                        "on export, so it can be registered as a custom FBX and used as-is.\r\n" +
                        "EXPERIMENTAL: can now be opened mid-battle too. Closing resumes the same battle in place.\r\n";

                case 3:
                    return
                        "Hard resets the game without going to the launcher.\r\n" +
                        "Useful for editing BattlePatch.txt, DictionaryPatch.txt, and other parameters that require a hard reset before they can be tested in-game.\r\n";

                case 4:
                    return
                        "Automatically keeps the last 10 field save states for rapid testing, plus one dedicated\r\n" +
                        "checkpoint that never gets pushed out by new captures. Also includes permanent, titled\r\n" +
                        "save slots with a screenshot — real save slots for when your in-game saves are full,\r\n" +
                        "listed and loadable from Load / Manage Saved States.\r\n";

                case 5:
                    return
                        "Exports complete-game development assets directly into folders in the FFIX game root.\r\n" +
                        "Includes the full Field Creator-style background dump and the full runtime model 2D texture dump. These operations create a lot of files.\r\n";

                case 6:
                    return
                        "Shuts the game down and restarts to the Memoria launcher.\r\n" +
                        "Use it to change enabled mods, mod order, and Memoria settings before launching FFIX again.\r\n";

                case 7:
                    return
                        "Developer field/story warp terminal. Pick a tested checkpoint or enter any mapped Field ID and ScenarioCounter manually.\r\n" +
                        "EXPERIMENTAL: warps are still being validated across all discs/sections; keep a manual save before relying on one.\r\n" +
                        "Warps are queued back to Unity's main thread before reloading the field.\r\n";

                case 8:
                    return
                        "Browse and preview every indexed sound effect, BGM, song, embedded SFX sound, and movie-audio cue.\r\n" +
                        "Includes friendly names plus known vanilla field and script usage extracted from the game data.\r\n";
            }

            return String.Empty;
        }

        private static String MenuLine(Boolean selected, String text)
        {
            return (selected ? " >> " : "    ") + text + "\r\n";
        }

        // --- Shared page-chrome helpers -------------------------------------------------
        // Every page used to build its own separators/footers by hand, which is how the
        // dash counts and hint wording quietly drifted out of sync between pages. Routing
        // everything through these few helpers keeps new pages consistent for free.

        private const Int32 ConsoleWidth = 90;

        private static String Rule()
        {
            return new String('-', ConsoleWidth) + "\r\n";
        }

        private static String DoubleRule()
        {
            return new String('=', ConsoleWidth) + "\r\n";
        }

        /// Builds the "Home  >  Section  >  Sub-section" wayfinding line shown under the
        /// title on any page nested more than one level deep (Warp, Sound Browser, etc).
        private static String Breadcrumb(params String[] path)
        {
            return String.Join("  >  ", path) + "\r\n" + Rule();
        }

        /// Standard title block: MEMORIA DEV CONSOLE  /  <TITLE>, followed by a rule.
        private static String Header(String title)
        {
            return "MEMORIA DEV CONSOLE  /  " + title + "\r\n" + Rule();
        }

        /// Joins per-page control hints in one consistent order: Navigate first, Back last,
        /// anything page-specific goes in between (passed in as the middle args).
        private static String Footer(String navigate, String select, String extra, String back)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Rule());
            builder.Append(navigate);

            if (!String.IsNullOrEmpty(select))
                builder.Append("    ").Append(select);

            if (!String.IsNullOrEmpty(extra))
                builder.Append("    ").Append(extra);

            builder.Append("    ").Append(back);
            builder.Append("\r\n");
            return builder.ToString();
        }

        private const String NavigateHint = "Arrow Keys: Navigate";
        private const String SelectHint = "Enter: Select";
        private const String BackHint = "ESC: Back";

        public void SetSoundBrowserCatalog(DevSoundCatalog catalog)
        {
            lock (_soundBrowserLock)
            {
                _soundCatalog = catalog;
                _pendingSoundBrowserStatus = catalog == null ? "Audio metadata unavailable." : "Ready. Select an entry and press Enter to preview.";
            }

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_SOUND_BROWSER_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        public void SetSoundBrowserStatus(String status)
        {
            lock (_soundBrowserLock)
                _pendingSoundBrowserStatus = status ?? String.Empty;

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_SOUND_BROWSER_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        private void BeginSoundFilter()
        {
            if (_soundMode != SoundBrowserMode.Entries && _soundMode != SoundBrowserMode.SfxGroups && _soundMode != SoundBrowserMode.FieldGroups)
                return;

            _soundModeBeforeFilter = _soundMode;
            _soundFilterEntryText = _soundFilter;
            _soundMode = SoundBrowserMode.FilterEntry;
            RenderSoundBrowser();
        }

        private void BeginSoundAlias()
        {
            if (_soundMode != SoundBrowserMode.Entries && _soundMode != SoundBrowserMode.SfxEntries && _soundMode != SoundBrowserMode.FieldEntries)
                return;

            List<DevSoundEntry> entries = GetFilteredSoundEntries();
            if (_soundEntrySelection < 0 || _soundEntrySelection >= entries.Count)
                return;

            _soundAliasEntry = entries[_soundEntrySelection];
            _soundAliasEntryText = _soundAliasEntry.Identity ?? String.Empty;
            _soundModeBeforeFilter = _soundMode;
            _soundMode = SoundBrowserMode.AliasEntry;
            RenderSoundBrowser();
        }

        private Int32 GetSoundSelectionCount()
        {
            if (_soundMode == SoundBrowserMode.Categories)
                return 8;
            if (_soundMode == SoundBrowserMode.SfxGroups)
                return GetFilteredSfxGroups().Count;
            if (_soundMode == SoundBrowserMode.FieldGroups)
                return GetFilteredFieldGroups().Count;
            return GetFilteredSoundEntries().Count;
        }

        private void MoveSoundSelectionByPage(Int32 direction)
        {
            if (_page != Page.SoundBrowser || _soundMode == SoundBrowserMode.Categories || _soundMode == SoundBrowserMode.FilterEntry || _soundMode == SoundBrowserMode.AliasEntry)
                return;

            Int32 count = GetSoundSelectionCount();
            if (count <= 0)
                return;

            _soundEntrySelection = Math.Max(0, Math.Min(count - 1, _soundEntrySelection + direction * SoundPageSize));
            RenderSoundBrowser();
        }

        private void ActivateSoundSelection()
        {
            if (_soundMode == SoundBrowserMode.Categories)
            {
                _soundCategorySelection = _soundEntrySelection;
                _soundFilter = String.Empty;

                if (_soundCategorySelection == 0)
                {
                    _soundMode = SoundBrowserMode.FieldGroups;
                    _soundEntrySelection = _soundFieldGroupsCursorMemory;
                }
                else
                {
                    _soundMode = SoundBrowserMode.Entries;
                    Int32 remembered;
                    _soundEntrySelection = _soundCategoryEntryCursorMemory.TryGetValue(_soundCategorySelection, out remembered)
                        ? remembered
                        : 0;
                }

                RenderSoundBrowser();
                return;
            }

            if (_soundMode == SoundBrowserMode.SfxGroups)
            {
                List<DevSfxSoundGroup> groups = GetFilteredSfxGroups();
                if (_soundEntrySelection < 0 || _soundEntrySelection >= groups.Count)
                    return;

                _soundActiveSfxGroup = groups[_soundEntrySelection];
                _soundEntrySelection = 0;
                _soundFilter = String.Empty;
                _soundMode = SoundBrowserMode.SfxEntries;
                Action<Int32> loadHandler = SoundSfxLoadRequested;
                if (loadHandler != null)
                    loadHandler(_soundActiveSfxGroup.SfxId);
                RenderSoundBrowser();
                return;
            }

            if (_soundMode == SoundBrowserMode.FieldGroups)
            {
                List<DevFieldSoundGroup> groups = GetFilteredFieldGroups();
                if (_soundEntrySelection < 0 || _soundEntrySelection >= groups.Count)
                    return;

                _soundFieldGroupsCursorMemory = _soundEntrySelection;
                _soundActiveFieldGroup = groups[_soundEntrySelection];
                _soundFilter = String.Empty;
                _soundMode = SoundBrowserMode.FieldEntries;
                Int32 remembered;
                _soundEntrySelection = _soundFieldEntryCursorMemory.TryGetValue(_soundActiveFieldGroup.Field, out remembered)
                    ? remembered
                    : 0;
                RenderSoundBrowser();
                return;
            }

            List<DevSoundEntry> entries = GetFilteredSoundEntries();
            if (_soundEntrySelection < 0 || _soundEntrySelection >= entries.Count)
                return;

            Action<DevSoundEntry> previewHandler = SoundPreviewRequested;
            if (previewHandler != null)
                previewHandler(entries[_soundEntrySelection]);
        }

        private List<DevSoundEntry> GetFilteredSoundEntries()
        {
            List<DevSoundEntry> source = new List<DevSoundEntry>();
            if (_soundCatalog == null)
                return source;

            if (_soundMode == SoundBrowserMode.SfxEntries || (_soundMode == SoundBrowserMode.AliasEntry && _soundModeBeforeFilter == SoundBrowserMode.SfxEntries))
            {
                if (_soundActiveSfxGroup != null)
                    source.AddRange(_soundActiveSfxGroup.Sounds);
            }
            else if (_soundMode == SoundBrowserMode.FieldEntries || (_soundMode == SoundBrowserMode.AliasEntry && _soundModeBeforeFilter == SoundBrowserMode.FieldEntries))
            {
                if (_soundActiveFieldGroup != null)
                    source.AddRange(_soundActiveFieldGroup.Sounds);
            }
            else
            {
                switch (_soundCategorySelection)
                {
                    case 1: source.AddRange(_soundCatalog.EnemyBattleSounds); break;
                    case 2: source.AddRange(_soundCatalog.PlayerAttackSounds); break;
                    case 3: source.AddRange(_soundCatalog.BattleEffectSounds); break;
                    case 4: source.AddRange(_soundCatalog.KnownSoundEffects); break;
                    case 5: source.AddRange(_soundCatalog.Music); break;
                    case 6: source.AddRange(_soundCatalog.Songs); break;
                    case 7: source.AddRange(_soundCatalog.MovieAudio); break;
                }
            }

            if (String.IsNullOrEmpty(_soundFilter))
                return source;

            String filter = _soundFilter.ToLowerInvariant();
            return source.FindAll(delegate(DevSoundEntry entry) { return entry.SearchText.Contains(filter); });
        }

        private List<DevFieldSoundGroup> GetFilteredFieldGroups()
        {
            List<DevFieldSoundGroup> source = new List<DevFieldSoundGroup>();
            if (_soundCatalog != null)
                source.AddRange(_soundCatalog.FieldSoundGroups);
            if (String.IsNullOrEmpty(_soundFilter))
                return source;
            String filter = _soundFilter.ToLowerInvariant();
            return source.FindAll(delegate(DevFieldSoundGroup group) { return group.SearchText.Contains(filter); });
        }

        private List<DevSfxSoundGroup> GetFilteredSfxGroups()
        {
            List<DevSfxSoundGroup> source = new List<DevSfxSoundGroup>();
            if (_soundCatalog != null)
                source.AddRange(_soundCatalog.SfxGroups);
            if (String.IsNullOrEmpty(_soundFilter))
                return source;

            String filter = _soundFilter.ToLowerInvariant();
            return source.FindAll(delegate(DevSfxSoundGroup group) { return group.SearchText.Contains(filter); });
        }

        private void RenderSoundBrowser()
        {
            _page = Page.SoundBrowser;
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console - Sound Browser");

            StringBuilder builder = new StringBuilder();
            builder.Append("\r\n");
            builder.Append(Header("SOUND BROWSER"));

            if (_soundCatalog == null)
            {
                builder.AppendLine("Loading Memoria audio metadata on Unity's main thread...");
                builder.AppendLine("[ESC] Back");
                SetConsoleText(builder.ToString());
                return;
            }

            if (_soundMode == SoundBrowserMode.FilterEntry)
            {
                builder.AppendLine("FILTER: " + _soundFilterEntryText + "_");
                builder.AppendLine();
                builder.AppendLine("Type an ID, se key, identity, SoundType, field, attack, or alias.");
                builder.AppendLine("[Enter] Apply    [Backspace] Delete    [ESC] Cancel");
                SetConsoleText(builder.ToString());
                return;
            }

            if (_soundMode == SoundBrowserMode.AliasEntry)
            {
                builder.AppendLine("NAME THIS SOUND");
                builder.Append(Rule());
                builder.AppendLine("KEY: " + (_soundAliasEntry == null ? "<none>" : _soundAliasEntry.Name));
                builder.AppendLine("FILE: " + (_soundAliasEntry == null ? "<none>" : _soundAliasEntry.Path));
                builder.AppendLine();
                builder.AppendLine("CUSTOM NAME: " + _soundAliasEntryText + "_");
                builder.AppendLine();
                builder.AppendLine("[Enter] Save    [Backspace] Delete    Clear all text to remove alias    [ESC] Cancel");
                SetConsoleText(builder.ToString());
                return;
            }

            if (_soundMode == SoundBrowserMode.Categories)
            {
                builder.AppendLine("Choose an audio bank:");
                builder.AppendLine();
                builder.Append(MenuLine(_soundEntrySelection == 0, "Field Sound Effects".PadRight(26) + "(" + _soundCatalog.FieldSoundGroups.Count + " locations)"));
                builder.Append(MenuLine(_soundEntrySelection == 1, "Enemy Battle Sounds".PadRight(26) + "(" + _soundCatalog.EnemyBattleSounds.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 2, "Player Attack Sounds".PadRight(26) + "(" + _soundCatalog.PlayerAttackSounds.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 3, "Battle Effect Sounds".PadRight(26) + "(" + _soundCatalog.BattleEffectSounds.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 4, "Known Other Sounds".PadRight(26) + "(" + _soundCatalog.KnownSoundEffects.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 5, "BGM / Music".PadRight(26) + "(" + _soundCatalog.Music.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 6, "SType=Song Samples / Cues".PadRight(26) + "(" + _soundCatalog.Songs.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 7, "Movie Audio".PadRight(26) + "(" + _soundCatalog.MovieAudio.Count + ")"));
                builder.AppendLine();
                builder.Append(Footer(NavigateHint, "Enter: Open", "S: Stop all preview audio", BackHint));
            }
            else if (_soundMode == SoundBrowserMode.SfxGroups)
            {
                List<DevSfxSoundGroup> groups = GetFilteredSfxGroups();
                AppendSoundHeader(builder, "SFX EMBEDDED SOUND BANKS", groups.Count);
                Int32 start = (_soundEntrySelection / SoundPageSize) * SoundPageSize;
                for (Int32 i = start; i < Math.Min(start + SoundPageSize, groups.Count); i++)
                    builder.Append(MenuLine(i == _soundEntrySelection, "SFX " + groups[i].SfxId.ToString().PadLeft(4) + "  " + Shorten(groups[i].Name, 48) + "  [" + groups[i].Sounds.Count + "]"));
                AppendSoundFooter(builder, groups.Count);
            }
            else if (_soundMode == SoundBrowserMode.FieldGroups)
            {
                List<DevFieldSoundGroup> groups = GetFilteredFieldGroups();
                AppendSoundHeader(builder, "FIELD LOCATIONS WITH SOUND EFFECTS", groups.Count);
                Int32 start = (_soundEntrySelection / SoundPageSize) * SoundPageSize;
                for (Int32 i = start; i < Math.Min(start + SoundPageSize, groups.Count); i++)
                    builder.Append(MenuLine(i == _soundEntrySelection, Shorten(groups[i].Field, 68) + "  [" + groups[i].Sounds.Count + "]"));
                AppendSoundFooter(builder, groups.Count);
            }
            else
            {
                List<DevSoundEntry> entries = GetFilteredSoundEntries();
                String title;
                if (_soundMode == SoundBrowserMode.SfxEntries && _soundActiveSfxGroup != null)
                    title = "SFX " + _soundActiveSfxGroup.SfxId + " - " + _soundActiveSfxGroup.Name + " (MOVE-SPECIFIC ONLY)";
                else if (_soundMode == SoundBrowserMode.FieldEntries && _soundActiveFieldGroup != null)
                    title = "FIELD: " + _soundActiveFieldGroup.Field;
                else
                    title = GetSoundCategoryTitle();
                AppendSoundHeader(builder, title, entries.Count);
                Int32 start = (_soundEntrySelection / SoundPageSize) * SoundPageSize;
                for (Int32 i = start; i < Math.Min(start + SoundPageSize, entries.Count); i++)
                {
                    DevSoundEntry entry = entries[i];
                    String id = "ID " + entry.Id;
                    String resident = entry.Resident ? " [resident]" : String.Empty;
                    String identity = String.IsNullOrEmpty(entry.Identity) ? String.Empty : " — " + entry.Identity;
                    String slot = entry.Category == DevSoundCategory.SfxSound || entry.Category == DevSoundCategory.ResidentSfxSound
                        ? " [slot " + entry.RuntimeSlot + "]"
                        : String.Empty;
                    builder.Append(MenuLine(i == _soundEntrySelection, id.PadRight(10) + Shorten(entry.Name + identity, 58) + slot + resident));
                }

                if (entries.Count > 0 && _soundEntrySelection < entries.Count)
                {
                    DevSoundEntry selected = entries[_soundEntrySelection];
                    builder.AppendLine();
                    builder.AppendLine("KEY: " + selected.Name);
                    builder.AppendLine("FILE: " + selected.Path);
                    builder.AppendLine("SEQ CALL: " + selected.SequenceCall);
                    if (selected.Category == DevSoundCategory.SfxSound || selected.Category == DevSoundCategory.ResidentSfxSound)
                        builder.AppendLine("SFX BANK SLOT: local " + selected.LocalSlot + " / runtime " + selected.RuntimeSlot);
                    if (!String.IsNullOrEmpty(selected.Identity))
                        builder.AppendLine("IDENTITY: " + selected.Identity);
                    if (!String.IsNullOrEmpty(selected.Usage))
                        builder.AppendLine("VANILLA USE: " + selected.Usage);
                }
                AppendSoundFooter(builder, entries.Count);
            }

            String status;
            lock (_soundBrowserLock)
                status = _pendingSoundBrowserStatus;
            builder.AppendLine();
            builder.AppendLine("STATUS: " + status);
            SetConsoleText(builder.ToString());
            SetFocus(_windowHandle);
        }

        private void AppendSoundHeader(StringBuilder builder, String title, Int32 count)
        {
            Int32 page = count == 0 ? 0 : (_soundEntrySelection / SoundPageSize) + 1;
            Int32 pages = count == 0 ? 0 : ((count - 1) / SoundPageSize) + 1;
            builder.Append(Breadcrumb("Sound Browser", title));
            builder.AppendLine(count + " entries    page " + page + "/" + pages);
            builder.AppendLine("Filter: " + (String.IsNullOrEmpty(_soundFilter) ? "<none>" : _soundFilter));
            builder.Append(Rule());
        }

        private void AppendSoundFooter(StringBuilder builder, Int32 count)
        {
            builder.AppendLine();
            builder.Append(Footer(
                "Up/Down: Select    Left/Right: Page",
                "Enter: Open or Preview",
                "F: Filter    X/S: Stop    Results: " + count,
                BackHint));
        }

        private String GetSoundCategoryTitle()
        {
            switch (_soundCategorySelection)
            {
                case 1: return "ENEMY BATTLE SOUNDS";
                case 2: return "PLAYER ATTACK / WEAPON SOUNDS";
                case 3: return "BATTLE EFFECT SOUNDS";
                case 4: return "KNOWN OTHER SOUNDS";
                case 5: return "BGM / MUSIC";
                case 6: return "STYPE=SONG / RAW CUES";
                case 7: return "MOVIE AUDIO";
                default: return "AUDIO";
            }
        }

        private static String Shorten(String value, Int32 length)
        {
            if (String.IsNullOrEmpty(value))
                return "Unknown";
            return value.Length <= length ? value : value.Substring(0, Math.Max(0, length - 3)) + "...";
        }

        private void RenderLiveLog()
        {
            _page = Page.LiveLog;
            HideSaveThumbnail();

            SetWindowText(_windowHandle, "Memoria Dev Console - Live Log");

            StringBuilder builder = new StringBuilder();

            builder.AppendLine();
            builder.Append(Header("LIVE LOG"));
            builder.AppendLine("[ESC] Back");
            builder.AppendLine();

            lock (_appendLock)
            {
                builder.Append(_liveLogBuffer.ToString());
            }

            SetConsoleText(builder.ToString());
            ScrollBottom();
            SetFocus(_windowHandle);
        }

        private void RetryCompile()
        {
            RenderCompileRestart();

            Action compileHandler = CompileRestartRequested;
            if (compileHandler != null)
                compileHandler();
        }

        private void RenderCompileRestart()
        {
            _page = Page.CompileRestart;
            HideSaveThumbnail();

            lock (_compileLock)
                _pendingCompileAppend = String.Empty;

            SetWindowText(_windowHandle, "Memoria Dev Console - Compile + Restart");

            SetConsoleText(
                "\r\n" +
                Header("COMPILE + RESTART") +
                "\r\n" +
                "Starting compiler...\r\n\r\n" +
                "If compilation fails, fix the source and press [R] here to compile again.\r\n" +
                Rule() +
                "R: Retry Compile    ESC: Back\r\n");

            SetFocus(_windowHandle);
        }

        private void RenderModelViewer()
        {
            _page = Page.ModelViewer;
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console - Open / Close Model Viewer");

            SetConsoleText(
                "\r\n" +
                Header("OPEN / CLOSE MODEL VIEWER") +
                "\r\n" +
                "Preparing Model Viewer transition...\r\n" +
                "\r\n" +
                "All 10 field snapshots are persisted before the restart.\r\n" +
                "Open the Dev Console and choose Open / Close Model Viewer again to return to normal FFIX.\r\n" +
                "\r\n" +
                "TIP: F11 dumps the current model to FBX (already re-oriented/fixed on export,\r\n" +
                "so it can be registered as a custom FBX and used as-is).\r\n" +
                "\r\n" +
                Rule() +
                BackHint + "\r\n");

            SetFocus(_windowHandle);
        }

        public void SetModelViewerStatus(String status)
        {
            if (!_running || _textHandle == IntPtr.Zero)
                return;

            SetConsoleText(
                "\r\n" +
                Header("OPEN / CLOSE MODEL VIEWER") +
                "\r\n" +
                (status ?? String.Empty) +
                "\r\n\r\n" +
                "All 10 field snapshots are preserved across the restart.\r\n" +
                "TIP: F11 dumps the current model to FBX (already re-oriented/fixed on export,\r\n" +
                "so it can be registered as a custom FBX and used as-is).\r\n" +
                "\r\n" +
                Rule() +
                BackHint + "\r\n");
        }

        private void RenderWarp()
        {
            _page = Page.Warp;
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console - Story Warper");

            String status;
            lock (_warpLock)
                status = _pendingWarpText;

            StringBuilder body = new StringBuilder();
            body.Append("\r\n").Append(Header("STORY WARPER (EXPERIMENTAL)"));

            if (_warpSectionMode)
            {
                StoryWarpPreset[] presets = StoryWarpCatalog.GetDisc(_warpDisc);
                body.Append(Breadcrumb("Warp", "Disc " + _warpDisc));
                for (Int32 i = 0; i < presets.Length; i++)
                {
                    StoryWarpPreset preset = presets[i];
                    String key = i < 9 ? "[" + (i + 1) + "] " : "    ";
                    body.Append(MenuLine(_warpSelection == i, key + preset.Name +
                        "   F" + preset.FieldId + " / S" + preset.Scenario));
                }
                body.Append("\r\n").Append(Footer(NavigateHint, SelectHint, null, "ESC: Disc menu"));
            }
            else
            {
                body.Append(MenuLine(_warpSelection == 0, "[1] Disc 1"));
                body.Append(MenuLine(_warpSelection == 1, "[2] Disc 2"));
                body.Append(MenuLine(_warpSelection == 2, "[3] Disc 3"));
                body.Append(MenuLine(_warpSelection == 3, "[4] Disc 4"));
                body.Append(MenuLine(_warpSelection == 4, "[5] Manual Field + Scenario"));
                body.Append("\r\n");
                body.Append("Each section targets the first useful field/story state for that chapter.\r\n");
                body.Append("Story presets also normalize the expected party. Inventory and unrelated/custom\r\n");
                body.Append("quest flags are preserved so chained Lost Chapters testing can keep progressing.\r\n");
            }

            if (_warpEntryActive)
            {
                body.Append("\r\nMANUAL WARP\r\n");
                body.Append(Rule());
                if (_warpEntryStage == 0)
                    body.Append("Field ID: ").Append(_warpFieldEntryText.Length == 0 ? "_" : _warpFieldEntryText + "_").Append("\r\nEnter: Next    Backspace: Edit    ESC: Cancel\r\n");
                else
                    body.Append("Field ID: ").Append(_warpFieldEntryText).Append("\r\nScenarioCounter: ").Append(_warpScenarioEntryText.Length == 0 ? "_" : _warpScenarioEntryText + "_").Append("\r\nEnter: Warp    Backspace: Edit    ESC: Cancel\r\n");
            }

            body.Append("\r\nSTATUS\r\n");
            body.Append(Rule());
            body.Append(status ?? String.Empty).Append("\r\n\r\n");
            body.Append(Footer(NavigateHint, SelectHint, null, BackHint));

            SetConsoleText(body.ToString());
            SetFocus(_windowHandle);
        }

        public void SetWarpStatus(String status)
        {
            if (!_running)
                return;

            lock (_warpLock)
                _pendingWarpText = status ?? String.Empty;

            if (_windowHandle != IntPtr.Zero && _page == Page.Warp)
                PostMessage(_windowHandle, WM_APP_WARP_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        private void RenderAssetDumper()
        {
            _page = Page.AssetDumper;
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console - Asset Dumper");

            String status;
            lock (_assetDumpLock)
                status = _pendingAssetDumpText;

            SetConsoleText(
                "\r\n" +
                Header("ASSET DUMPER") +
                "\r\n" +
                MenuLine(_assetSelection == 0, "[1] Field Background Dump - Complete Game") +
                MenuLine(_assetSelection == 1, "[2] 2D Texture Dump - Complete Game") +
                MenuLine(_assetSelection == 2, "[3] Field Background by Field ID") +
                MenuLine(_assetSelection == 3, "[4] 2D Textures by Model ID") +
                MenuLine(_assetSelection == 4, "[5] Current Battle Background PNGs") +
                "\r\n" +
                Rule() +
                "DESCRIPTION\r\n" +
                Rule() +
                GetAssetDumpDescription(_assetSelection) +
                "\r\n" +
                "IMPORTANT\r\n" +
                Rule() +
                "Options 1 and 2 are COMPLETE GAME dumps and create a large number of files.\r\n" +
                "Options 3 and 4 are quick lookup dumps for one exact field ID or model ID.\r\n" +
                "Option 5 dumps only the currently loaded battle background textures.\r\n" +
                "Complete dumps may take a while; existing completed entries are skipped.\r\n" +
                "Output is written directly under the FINAL FANTASY IX game folder.\r\n" +
                "\r\n" +
                (_assetIdEntryActive
                    ? ((_assetIdEntryIsField ? "FIELD ID LOOKUP" : "MODEL ID LOOKUP") + "\r\n" +
                       Rule() +
                       "Type numeric ID: " + (_assetIdEntryText.Length == 0 ? "_" : _assetIdEntryText + "_") + "\r\n" +
                       "Enter: Dump    Backspace: Edit    ESC: Cancel\r\n\r\n")
                    : String.Empty) +
                "STATUS\r\n" +
                Rule() +
                status +
                "\r\n\r\n" +
                Footer(NavigateHint, "Enter: Start Dump", "O: Open Output Folder", BackHint));

            SetFocus(_windowHandle);
        }

        private void OpenAssetOutputFolder()
        {
            try
            {
                String folderName;
                if (_assetSelection == 0 || _assetSelection == 2)
                    folderName = "FieldDumper_Output";
                else if (_assetSelection == 4)
                    folderName = "BattleBackgroundDumper_Output";
                else
                    folderName = "2D Texture Dump Output";
                String outputPath = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), folderName);

                if (!Directory.Exists(outputPath))
                {
                    SetAssetDumpStatus("Output folder does not exist yet. Run this dump first.\r\nExpected: " + outputPath);
                    return;
                }

                Process.Start("explorer.exe", "\"" + outputPath + "\"");
                SetAssetDumpStatus("Opened output folder:\r\n" + outputPath);
            }
            catch (Exception ex)
            {
                SetAssetDumpStatus("Could not open output folder.\r\n" + ex.Message);
            }
        }

        private static String GetAssetDumpDescription(Int32 selection)
        {
            if (selection == 0)
            {
                return
                    "Exports every internal FFIX field background using Memoria's Field Creator reconstruction path.\r\n" +
                    "Creates FieldDumper_Output in the game root with per-field BGX metadata plus editable reconstructed PNG overlay images.\r\n";
            }

            if (selection == 1)
            {
                return
                    "Loads every known GEO model through Memoria and writes its runtime 2D textures as PNG files.\r\n" +
                    "Creates 2D Texture Dump Output in the game root, organized by model category and model ID/name or GEO key. No animations are dumped.\r\n";
            }

            if (selection == 2)
            {
                return
                    "Enter an exact FFIX field ID and export only that field's reconstructed BGX + editable PNG overlays.\r\n" +
                    "Uses the same FieldDumper_Output structure as the complete field dump. Great for grabbing one field without dumping all 817.\r\n";
            }

            if (selection == 3)
            {
                return
                    "Enter an exact GEO model ID and export only that model's runtime 2D textures plus GEO_KEY.txt.\r\n" +
                    "Uses the same 2D Texture Dump Output structure as the complete texture dump. Great for Model Viewer lookup workflow.\r\n";
            }

            return
                "While an actual battle is loaded, dumps every unique mainTexture currently used by the live battle background (btlBGPtr) as PNG.\r\n" +
                "Also writes MaterialMap.csv so the exact renderer, material, and texture names are preserved for one-off battle-specific reskins.\r\n";
        }

        public void SetAssetDumpStatus(String status)
        {
            if (!_running)
                return;

            lock (_assetDumpLock)
                _pendingAssetDumpText = status ?? String.Empty;

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_ASSET_DUMP_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        private void RenderFieldState()
        {
            _page = Page.FieldState;
            SetWindowText(_windowHandle, "Memoria Dev Console - Save States");
            SyncFieldStateThumbnail();
            SetConsoleText(BuildFieldStateText("Loading...\r\n"));
            SetFocus(_windowHandle);
        }

        /// Shows/hides the save-state preview strip (screenshot + party text) to match the
        /// current sub-mode and selection. Called from RenderFieldState/RenderFieldStateStatus
        /// rather than from inside the Build*Text methods below, which stay pure text builders.
        private void SyncFieldStateThumbnail()
        {
            if (_fieldSaveSubMode != FieldSaveSubMode.SaveList)
            {
                HideSaveThumbnail();
                return;
            }

            NamedSaveSummary selected = GetSelectedNamedSave();
            if (selected != null)
                ShowSaveThumbnail(selected);
            else
                HideSaveThumbnail();
        }

        /// Shared by RenderFieldState (first paint) and RenderFieldStateStatus (async status
        /// updates) so the two can no longer drift apart, as the old copy-pasted text did.
        /// Dispatches on _fieldSaveSubMode since "Field Save States" now covers both the
        /// original rolling-history/checkpoint tools AND the permanent titled save slots.
        private String BuildFieldStateText(String timelineText)
        {
            if (_fieldSaveSubMode == FieldSaveSubMode.TitleEntry)
                return BuildSaveTitleEntryText();
            if (_fieldSaveSubMode == FieldSaveSubMode.SaveList)
                return BuildSaveListText();
            return BuildFieldStateNormalText(timelineText);
        }

        private String BuildFieldStateNormalText(String timelineText)
        {
            return
                "\r\n" +
                Header("SAVE STATES") +
                "\r\n" +
                MenuLine(_fieldSelection == 0, "[1] Move Back") +
                MenuLine(_fieldSelection == 1, "[2] Move Forward") +
                MenuLine(_fieldSelection == 2, "[3] Set Checkpoint") +
                MenuLine(_fieldSelection == 3, "[4] Load Checkpoint") +
                MenuLine(_fieldSelection == 4, "[5] Save New State (Title + Screenshot)") +
                MenuLine(_fieldSelection == 5, "[6] Load / Manage Saved States") +
                "\r\n" +
                Rule() +
                "DESCRIPTION\r\n" +
                Rule() +
                GetFieldStateDescription(_fieldSelection) +
                "\r\n" +
                "FIELD SAVE STATE TIMELINE\r\n" +
                Rule() +
                timelineText +
                "\r\n" +
                Footer(NavigateHint, SelectHint, null, BackHint);
        }

        private String BuildSaveTitleEntryText()
        {
            return
                "\r\n" +
                Header("NEW SAVE STATE") +
                "\r\n" +
                "A screenshot is captured the instant you press Enter, alongside the full field/world state.\r\n" +
                "\r\n" +
                "TITLE: " + (_saveTitleEntryText.Length == 0 ? "_" : _saveTitleEntryText + "_") + "\r\n" +
                "\r\n" +
                Rule() +
                "Enter: Capture + Save    Backspace: Edit    ESC: Cancel\r\n";
        }

        private String BuildSaveListText()
        {
            List<NamedSaveSummary> saves;
            String status;

            lock (_namedSaveLock)
            {
                saves = new List<NamedSaveSummary>(_namedSaves);
                status = _pendingNamedSaveStatus;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("\r\n");
            builder.Append(Header("SAVED STATES"));

            if (saves.Count == 0)
            {
                builder.AppendLine("No saved states yet. Press ESC, then choose [5] Save New State.");
            }
            else
            {
                for (Int32 i = 0; i < saves.Count; i++)
                {
                    NamedSaveSummary save = saves[i];
                    String location = String.IsNullOrEmpty(save.LocationName)
                        ? "Field " + save.FieldId
                        : save.LocationName;
                    String line = Shorten(save.Title, 26).PadRight(28) +
                        Shorten(location, 36).PadRight(38) +
                        save.CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    builder.Append(MenuLine(i == _saveListSelection, line));
                }

                NamedSaveSummary selected = saves[Math.Max(0, Math.Min(_saveListSelection, saves.Count - 1))];
                builder.Append("\r\n");
                builder.AppendLine("LOCATION: " + selected.LocationName);
                builder.AppendLine(BuildSaveMapDetails(selected));
            }

            builder.Append("\r\n");
            builder.AppendLine("STATUS: " + status);
            builder.Append(Footer(NavigateHint, "Enter: Load", "N: New    Delete: Remove", BackHint));

            return builder.ToString();
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

        public void SetNamedSaveList(List<NamedSaveSummary> saves)
        {
            if (!_running)
                return;

            lock (_namedSaveLock)
                _namedSaves = saves ?? new List<NamedSaveSummary>();

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_FIELD_STATE_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        public void SetNamedSaveStatus(String status)
        {
            if (!_running)
                return;

            lock (_namedSaveLock)
                _pendingNamedSaveStatus = status ?? String.Empty;

            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_FIELD_STATE_TEXT, IntPtr.Zero, IntPtr.Zero);
        }

        private Int32 GetNamedSaveCount()
        {
            lock (_namedSaveLock)
                return _namedSaves.Count;
        }

        private NamedSaveSummary GetSelectedNamedSave()
        {
            lock (_namedSaveLock)
            {
                if (_saveListSelection < 0 || _saveListSelection >= _namedSaves.Count)
                    return null;
                return _namedSaves[_saveListSelection];
            }
        }

        private void RenderFieldStateStatus()
        {
            if (_page != Page.FieldState)
                return;

            String status;

            lock (_fieldStateLock)
                status = _pendingFieldStateText;

            SyncFieldStateThumbnail();
            SetConsoleText(BuildFieldStateText(status + "\r\n"));
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
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console - " + title.Replace(" ", ""));

            SetConsoleText(
                "\r\n" +
                Header(title) +
                "\r\n" +
                body +
                "\r\n\r\n" +
                Rule() +
                "[ESC] Back\r\n" +
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
            LayoutControls();
        }

        /// Lays out the main text control and (when visible) the save-state preview strip
        /// against the window's current client rect. Called on WM_SIZE and any time the
        /// strip's visibility changes, so nothing needs to track size state independently
        /// of what Windows actually reports right now.
        private void LayoutControls()
        {
            if (_windowHandle == IntPtr.Zero || _textHandle == IntPtr.Zero)
                return;

            RECT client;
            if (!GetClientRect(_windowHandle, out client))
                return;

            Int32 width = client.Right - client.Left;
            Int32 height = client.Bottom - client.Top;

            if (width <= 0 || height <= 0)
                return;

            Int32 reservedAtBottom = _thumbnailVisible ? PreviewStripHeight : 0;
            Int32 textWidth = Math.Max(100, width - 24);
            Int32 textHeight = Math.Max(100, height - 24 - reservedAtBottom);

            MoveWindow(_textHandle, 12, 12, textWidth, textHeight, true);

            if (_thumbnailVisible)
            {
                Int32 stripTop = 12 + textHeight + 12;

                if (_thumbnailHandle != IntPtr.Zero)
                    MoveWindow(_thumbnailHandle, 12, stripTop, ThumbnailWidthPx, ThumbnailHeightPx, true);

                Int32 columnX = 12 + ThumbnailWidthPx + 16;
                Int32 columnWidth = Math.Max(100, width - columnX - 12);

                for (Int32 i = 0; i < MaxPortraitSlots; i++)
                {
                    if (_portraitHandles[i] == IntPtr.Zero)
                        continue;

                    Int32 portraitX = columnX + i * (PortraitWidthPx + PortraitGap);
                    MoveWindow(_portraitHandles[i], portraitX, stripTop, PortraitWidthPx, PortraitHeightPx, true);
                }

                if (_partyTextHandle != IntPtr.Zero)
                {
                    Int32 partyY = stripTop + PortraitHeightPx + 8;
                    Int32 partyHeight = Math.Max(20, ThumbnailHeightPx - PortraitHeightPx - 8);
                    MoveWindow(_partyTextHandle, columnX, partyY, columnWidth, partyHeight, true);
                }
            }
        }

        /// Loads the PNG at path, generates a fixed-size thumbnail via GDI+, and shows the
        /// preview strip (screenshot + party text) below the text console, shrinking the
        /// console's height to make room. Safe to call repeatedly (e.g. once per
        /// list-selection change); each call replaces the previous bitmap and frees it so
        /// GDI handles don't leak.
        private void ShowSaveThumbnail(NamedSaveSummary save)
        {
            String pngPath = save == null ? null : save.ScreenshotPath;
            SetSavePreviewText(save);
            UpdatePortraits(save == null ? null : save.PartyCharacterIds);

            if (!_gdiplusReady || _thumbnailHandle == IntPtr.Zero || String.IsNullOrEmpty(pngPath) || !File.Exists(pngPath))
            {
                if (_thumbnailHandle != IntPtr.Zero)
                    ShowWindow(_thumbnailHandle, SW_HIDE);
                ShowPreviewStrip();
                return;
            }

            IntPtr image = IntPtr.Zero;
            IntPtr thumbImage = IntPtr.Zero;
            IntPtr newBitmap = IntPtr.Zero;

            try
            {
                if (GdipLoadImageFromFile(pngPath, out image) != 0 || image == IntPtr.Zero)
                {
                    ShowWindow(_thumbnailHandle, SW_HIDE);
                    ShowPreviewStrip();
                    return;
                }

                if (GdipGetImageThumbnail(image, (UInt32)ThumbnailWidthPx, (UInt32)ThumbnailHeightPx, out thumbImage, IntPtr.Zero, IntPtr.Zero) != 0 || thumbImage == IntPtr.Zero)
                {
                    ShowWindow(_thumbnailHandle, SW_HIDE);
                    ShowPreviewStrip();
                    return;
                }

                if (GdipCreateHBITMAPFromBitmap(thumbImage, out newBitmap, 0x00000000) != 0 || newBitmap == IntPtr.Zero)
                {
                    ShowWindow(_thumbnailHandle, SW_HIDE);
                    ShowPreviewStrip();
                    return;
                }

                SendMessage(_thumbnailHandle, STM_SETIMAGE, new IntPtr(IMAGE_BITMAP), newBitmap);

                if (_thumbnailBitmap != IntPtr.Zero)
                    DeleteObject(_thumbnailBitmap);
                _thumbnailBitmap = newBitmap;

                ShowWindow(_thumbnailHandle, SW_SHOW);
                ShowPreviewStrip();
            }
            catch
            {
                if (_thumbnailHandle != IntPtr.Zero)
                    ShowWindow(_thumbnailHandle, SW_HIDE);
                ShowPreviewStrip();
            }
            finally
            {
                if (thumbImage != IntPtr.Zero)
                    GdipDisposeImage(thumbImage);
                if (image != IntPtr.Zero)
                    GdipDisposeImage(image);
            }
        }

        /// Reveals the bottom strip itself (shrinking the text console) if it isn't
        /// already visible. Split out from ShowSaveThumbnail so the strip (and the party
        /// text within it) can still appear even when there is no screenshot to show.
        private void ShowPreviewStrip()
        {
            if (!_thumbnailVisible)
            {
                _thumbnailVisible = true;
                LayoutControls();
            }

            if (_partyTextHandle != IntPtr.Zero)
                ShowWindow(_partyTextHandle, SW_SHOW);
        }

        private void SetSavePreviewText(NamedSaveSummary save)
        {
            if (_partyTextHandle == IntPtr.Zero)
                return;

            String display;
            if (save == null)
            {
                display = "(save details unavailable)";
            }
            else
            {
                String location = String.IsNullOrEmpty(save.LocationName)
                    ? "Field " + save.FieldId
                    : save.LocationName;
                String party = String.IsNullOrEmpty(save.PartySummary)
                    ? "(party unavailable)"
                    : save.PartySummary.Replace(" / ", "\r\n");

                display = location + "\r\n" +
                    BuildSaveMapDetails(save).Replace("    ", "  |  ") + "\r\n" +
                    save.CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\r\n\r\n" +
                    party;
            }
            SendMessageText(_partyTextHandle, WM_SETTEXT, IntPtr.Zero, display);
        }

        private static String BuildSaveMapDetails(NamedSaveSummary save)
        {
            if (save == null)
                return "Map details unavailable";

            if (save.MapMode == 3)
                return "WORLD MAP: " + save.WorldMapId + "    SCENARIO: " + save.Scenario;

            return "FIELD: " + save.FieldId + "    LOCATION ID: " + save.LocationId +
                   "    SCENARIO: " + save.Scenario;
        }

        // --- Party portraits -------------------------------------------------------------
        // Cropped from a bundled copy of the reference face atlas at
        // Memoria Dev Console/FaceAtlas/Face Atlas.png — never from whatever texture mod
        // is active in the live game, so these known-good pixel coordinates (confirmed by
        // hand against that specific file) always line up. Coordinates are the same
        // 132x190 grid cells the community-standard "Face Atlas.tpsheet" already used for
        // this file; only 8 of the 12 playable characters are confirmed placed in it.

        private static String GetPortraitAtlasPath()
        {
            return Path.Combine(
                Path.Combine(
                    Path.Combine(Environment.CurrentDirectory, "Memoria Dev Console"),
                    "FaceAtlas"),
                "Face Atlas.png");
        }

        private static Boolean TryGetPortraitRect(String characterName, out Int32 x, out Int32 y)
        {
            switch (characterName)
            {
                case "Zidane":  x = 268; y = 384; return true;
                case "Vivi":    x = 804; y = 384; return true;
                // Garnet has two variants in the atlas (long hair / short hair, matching
                // her Disc 1 haircut story beat); short hair is used as the single default
                // since it covers most of the game.
                case "Garnet":  x = 134; y = 192; return true;
                case "Steiner": x = 670; y = 384; return true;
                case "Quina":   x = 0;   y = 192; return true;
                case "Eiko":    x = 0;   y = 384; return true;
                case "Freya":   x = 536; y = 384; return true;
                case "Amarant": x = 134; y = 384; return true;
                case "Beatrix": x = 402; y = 192; return true;
                case "Cinna":   x = 402; y = 384; return true;
                case "Marcus":  x = 536; y = 192; return true;
                case "Blank":   x = 670; y = 192; return true;
                default:
                    x = 0; y = 0; return false;
            }
        }

        /// Loads the bundled reference atlas once and keeps it in memory for the rest of
        /// the session (it never changes). Returns false if the file is missing or GDI+
        /// couldn't load it; callers should treat that as "no portraits available" rather
        /// than an error, since this file is optional.
        private Boolean EnsurePortraitAtlasLoaded()
        {
            if (_portraitAtlasImage != IntPtr.Zero)
                return true;

            if (_portraitAtlasLoadAttempted || !_gdiplusReady)
                return false;

            _portraitAtlasLoadAttempted = true;

            String path = GetPortraitAtlasPath();
            if (!File.Exists(path))
                return false;

            IntPtr image;
            if (GdipLoadImageFromFile(path, out image) != 0 || image == IntPtr.Zero)
                return false;

            _portraitAtlasImage = image;
            return true;
        }

        /// Refreshes all 4 portrait slots from a comma-separated CharacterId list (e.g.
        /// "Zidane,Vivi,Garnet,Steiner", as stored in NamedSaveSummary.PartyCharacterIds).
        /// Unused slots (fewer than 4 party members, or a member with no confirmed atlas
        /// entry) are hidden rather than left showing stale art.
        private void UpdatePortraits(String partyCharacterIdsCsv)
        {
            String[] names = String.IsNullOrEmpty(partyCharacterIdsCsv)
                ? new String[0]
                : partyCharacterIdsCsv.Split(',');

            Boolean atlasReady = EnsurePortraitAtlasLoaded();

            for (Int32 slot = 0; slot < MaxPortraitSlots; slot++)
            {
                if (_portraitHandles[slot] == IntPtr.Zero)
                    continue;

                Int32 x, y;
                if (!atlasReady || slot >= names.Length || !TryGetPortraitRect(names[slot].Trim(), out x, out y))
                {
                    ShowWindow(_portraitHandles[slot], SW_HIDE);
                    continue;
                }

                IntPtr cropped = IntPtr.Zero;
                IntPtr scaled = IntPtr.Zero;
                IntPtr newBitmap = IntPtr.Zero;

                try
                {
                    if (GdipCloneBitmapAreaI(x, y, PortraitSourceWidth, PortraitSourceHeight, 0, _portraitAtlasImage, out cropped) != 0 || cropped == IntPtr.Zero)
                    {
                        ShowWindow(_portraitHandles[slot], SW_HIDE);
                        continue;
                    }

                    if (GdipGetImageThumbnail(cropped, (UInt32)PortraitWidthPx, (UInt32)PortraitHeightPx, out scaled, IntPtr.Zero, IntPtr.Zero) != 0 || scaled == IntPtr.Zero)
                    {
                        ShowWindow(_portraitHandles[slot], SW_HIDE);
                        continue;
                    }

                    if (GdipCreateHBITMAPFromBitmap(scaled, out newBitmap, 0x00000000) != 0 || newBitmap == IntPtr.Zero)
                    {
                        ShowWindow(_portraitHandles[slot], SW_HIDE);
                        continue;
                    }

                    SendMessage(_portraitHandles[slot], STM_SETIMAGE, new IntPtr(IMAGE_BITMAP), newBitmap);

                    if (_portraitBitmaps[slot] != IntPtr.Zero)
                        DeleteObject(_portraitBitmaps[slot]);
                    _portraitBitmaps[slot] = newBitmap;

                    ShowWindow(_portraitHandles[slot], SW_SHOW);
                }
                catch
                {
                    ShowWindow(_portraitHandles[slot], SW_HIDE);
                }
                finally
                {
                    if (scaled != IntPtr.Zero)
                        GdipDisposeImage(scaled);
                    if (cropped != IntPtr.Zero)
                        GdipDisposeImage(cropped);
                }
            }
        }

        private void HidePortraits()
        {
            for (Int32 i = 0; i < MaxPortraitSlots; i++)
            {
                if (_portraitHandles[i] != IntPtr.Zero)
                    ShowWindow(_portraitHandles[i], SW_HIDE);
            }
        }

        /// Hides the whole preview strip and gives the full height back to the text
        /// console. Called from every page's Render* method (not just Save States') so the
        /// strip can never stay visible after navigating away from the save list.
        private void HideSaveThumbnail()
        {
            if (_thumbnailHandle != IntPtr.Zero)
                ShowWindow(_thumbnailHandle, SW_HIDE);

            if (_partyTextHandle != IntPtr.Zero)
                ShowWindow(_partyTextHandle, SW_HIDE);

            HidePortraits();

            if (_thumbnailBitmap != IntPtr.Zero)
            {
                DeleteObject(_thumbnailBitmap);
                _thumbnailBitmap = IntPtr.Zero;
            }

            if (_thumbnailVisible)
            {
                _thumbnailVisible = false;
                LayoutControls();
            }
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
        private struct RECT
        {
            public Int32 Left;
            public Int32 Top;
            public Int32 Right;
            public Int32 Bottom;
        }

        [DllImport("user32.dll")]
        private static extern Boolean GetClientRect(IntPtr hWnd, out RECT lpRect);

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

        [DllImport("gdi32.dll")]
        private static extern Boolean DeleteObject(IntPtr hObject);

        // --- GDI+ (thumbnail loading) ------------------------------------------------
        // Raw flat-API P/Invoke rather than System.Drawing: consistent with every other
        // native call in this file, and avoids depending on an assembly reference that
        // may or may not be wired into Memoria's compiler (see the ScreenCapture lesson).

        [StructLayout(LayoutKind.Sequential)]
        private struct GdiplusStartupInput
        {
            public UInt32 GdiplusVersion;
            public IntPtr DebugEventCallback;
            public Boolean SuppressBackgroundThread;
            public Boolean SuppressExternalCodecs;
        }

        [DllImport("gdiplus.dll")]
        private static extern Int32 GdiplusStartup(out UIntPtr token, ref GdiplusStartupInput input, IntPtr output);

        [DllImport("gdiplus.dll")]
        private static extern void GdiplusShutdown(UIntPtr token);

        [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GdipLoadImageFromFile(String filename, out IntPtr image);

        [DllImport("gdiplus.dll")]
        private static extern Int32 GdipGetImageThumbnail(IntPtr image, UInt32 thumbWidth, UInt32 thumbHeight, out IntPtr thumbImage, IntPtr callback, IntPtr callbackData);

        [DllImport("gdiplus.dll")]
        private static extern Int32 GdipCreateHBITMAPFromBitmap(IntPtr bitmap, out IntPtr hbmReturn, Int32 background);

        // Crops a sub-rectangle out of an already-loaded GDI+ image into a new bitmap.
        // format=0 (PixelFormatDontCare) keeps the source's native pixel format, so we
        // don't need to hardcode a specific ARGB format constant.
        [DllImport("gdiplus.dll")]
        private static extern Int32 GdipCloneBitmapAreaI(Int32 x, Int32 y, Int32 width, Int32 height, Int32 format, IntPtr srcBitmap, out IntPtr dstBitmap);

        [DllImport("gdiplus.dll")]
        private static extern Int32 GdipDisposeImage(IntPtr image);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(String lpModuleName);
    }
}