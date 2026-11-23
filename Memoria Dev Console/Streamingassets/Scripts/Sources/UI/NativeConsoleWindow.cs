using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow : IDisposable
    {
        private enum Page
        {
            Home,
            LiveLog,
            LiveLogFilters,
            ConsoleColors,
            CompileRestart,
            ModelViewer,
            GameReset,
            FieldState,
            AssetDumper,
            Warp,
            SoundBrowser,
            WorldEntrances,
            BattleSceneEditor
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

        private enum AssetDumpSubMenu
        {
            None,
            FieldBackgrounds,
            Textures2D,
            Other
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
        private const UInt32 EM_SETBKGNDCOLOR = 0x0443;
        private const UInt32 EM_SETCHARFORMAT = 0x0444;
        private const UInt32 SCF_SELECTION = 0x0001;
        private const UInt32 CFM_COLOR = 0x40000000;

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
        private const Int32 VK_X = 0x58;
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
        private const Int32 ConsoleFontPoints = 16;
        private const Int32 ConsoleHeadingPoints = 17;

        private Page _page = Page.Home;

        private Int32 _homeSelection;
        private Int32 _fieldSelection;
        private FieldSaveSubMode _fieldSaveSubMode = FieldSaveSubMode.Normal;
        private String _saveTitleEntryText = String.Empty;
        private Int32 _saveListSelection;
        private Int32 SavePageSize
        {
            get
            {
                RECT client;
                if (_windowHandle == IntPtr.Zero || !GetClientRect(_windowHandle, out client)) return 8;
                Int32 textHeight = client.Bottom - client.Top - 24 - PreviewStripHeight;
                return Math.Max(1, Math.Min(8, textHeight / 27 - 16));
            }
        }
        private List<NamedSaveSummary> _namedSaves = new List<NamedSaveSummary>();
        private readonly Object _namedSaveLock = new Object();
        private String _pendingNamedSaveStatus = "No saved states yet.";
        private Int32 _assetSelection = 1;
        private Int32 _assetSubSelection;
        private AssetDumpSubMenu _assetSubMenu = AssetDumpSubMenu.None;
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
        private Boolean _compositePngSelectionActive;
        private Int32 _lastFieldCreatorId = -1;
        private Int32 _compositeCutFieldId;
        private String[] _compositeInputPngs = new String[0];
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
        private String _pendingAssetDumpText = "Ready. Select a dump operation above.";
        private readonly Object _warpLock = new Object();
        private String _pendingWarpText = "Ready. Choose a disc, then a major story section. Story warps set field + scenario + expected party while preserving unrelated/custom quest state.";
        private readonly Object _soundBrowserLock = new Object();
        private String _pendingSoundBrowserStatus = "Loading audio metadata...";

        public event Action CompileRestartRequested;
        public event Action ModelViewerRequested;
        public event Action<Int32> ViewerRequested;
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
        public event Action CurrentFieldDumpRequested;
        public event Action TextureDumpRequested;
        public event Action<Int32> FieldBackgroundByIdRequested;
        public event Action<Int32> FieldCompositeByIdRequested;
        public event Action<Int32, String> FieldCompositeCutRequested;
        public event Action<Int32> TextureByModelIdRequested;
        public event Action<Int32> FbxByModelIdRequested;
        public event Action CurrentBattleBackgroundDumpRequested;
        public event Action BattleScriptDumpRequested;
        public event Action SfxDumpRequested;
        public event Action WorldMapTextureDumpRequested;
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
                if (_pendingAppend.Length > MaxLiveLogCharacters)
                    _pendingAppend = _pendingAppend.Substring(_pendingAppend.Length - MaxLiveLogCharacters);
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
                return IsVisible && (_page == Page.LiveLog || _page == Page.LiveLogFilters);
            }
        }

        public void ShowHome()
        {
            if (_windowHandle != IntPtr.Zero)
                PostMessage(_windowHandle, WM_APP_SHOW_HOME, IntPtr.Zero, IntPtr.Zero);
        }

        // Called while MDC still owns foreground input, on its native window thread.
        public void HideForGame()
        {
            if (_windowHandle != IntPtr.Zero)
                SendMessage(_windowHandle, WM_APP_HIDE, new IntPtr(1), IntPtr.Zero);
        }

        private delegate Boolean GameWindowVisitor(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern Boolean EnumWindows(GameWindowVisitor visitor, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr hwnd, out UInt32 process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetClassName(IntPtr hwnd, StringBuilder name, Int32 capacity);

        private void FocusGameWindow()
        {
            UInt32 ownProcess;
            GetWindowThreadProcessId(_windowHandle, out ownProcess);
            EnumWindows(delegate(IntPtr candidate, IntPtr parameter)
            {
                UInt32 process;
                GetWindowThreadProcessId(candidate, out process);
                if (process != ownProcess || candidate == _windowHandle) return true;
                StringBuilder name = new StringBuilder(128);
                GetClassName(candidate, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                SetForegroundWindow(candidate);
                return false;
            }, IntPtr.Zero);
        }

        public void Hide()
        {
            if (_windowHandle != IntPtr.Zero)
                SendMessage(_windowHandle, WM_APP_HIDE, IntPtr.Zero, IntPtr.Zero);
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
                windowClass.style |= 0x0008; // CS_DBLCLKS: native double-click timing.
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
                    -22, 0, 0, 0, 400,
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
                    1120,
                    760,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    windowClass.hInstance,
                    IntPtr.Zero);

                if (_windowHandle == IntPtr.Zero)
                    throw new Exception("CreateWindowEx(main) failed. Win32 error " + Marshal.GetLastWin32Error());

                LoadConsoleHeaderIcons();
                // RichEdit lets MDC use real per-line colors in descriptions/status text.
                LoadLibrary("Msftedit.dll");
                _textHandle = CreateWindowEx(
                    0,
                    "RICHEDIT50W",
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
                SendMessage(_textHandle, EM_SETBKGNDCOLOR, IntPtr.Zero, IntPtr.Zero);

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

                if (_headerSmallIcon != IntPtr.Zero) { DestroyIcon(_headerSmallIcon); _headerSmallIcon=IntPtr.Zero; }
                if (_headerLargeIcon != IntPtr.Zero) { DestroyIcon(_headerLargeIcon); _headerLargeIcon=IntPtr.Zero; }
                if (_homeCrystal != IntPtr.Zero) { GdipDisposeImage(_homeCrystal); _homeCrystal = IntPtr.Zero; }
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
            if (HandleWorldMessage(hwnd, msg)) return IntPtr.Zero;
            if (HandleStyledDocumentMessage(hwnd, msg, wParam, lParam)) return IntPtr.Zero;
            if (HandleStyledHomeMessage(hwnd, msg, wParam, lParam)) return IntPtr.Zero;
            if (HandleBattleMessage(msg)) return IntPtr.Zero;
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
                    if (_page == Page.AssetDumper) RenderAssetDumper();
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
                    if (wParam != IntPtr.Zero) FocusGameWindow();
                    ShowWindow(hwnd, SW_HIDE);
                    return IntPtr.Zero;

                case WM_APP_EXIT:
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;

                case WM_CTLCOLOREDIT:
                case WM_CTLCOLORSTATIC:
                    SetTextColor(wParam, (Int32)PrimaryColor);
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
            if (Memoria.Configuration.Debug.StartModelViewer) return;
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
                if (ch == 'm' || ch == 'M') { _homeSelection = 5; ActivateHomeSelection(); return; }
                if (ch == 'l' || ch == 'L') { RenderLiveLog(); return; }
                switch (ch)
                {
                    case '0':
                        _homeSelection = 9;
                        ActivateHomeSelection();
                        return;
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
                        _homeSelection = 6;
                        ActivateHomeSelection();
                        return;
                    case '7':
                        _homeSelection = 8;
                        ActivateHomeSelection();
                        return;
                    case '8':
                        _homeSelection = 10;
                        ActivateHomeSelection();
                        return;
                    case '9':
                        _homeSelection = 7;
                        ActivateHomeSelection();
                        return;
                }
            }
            else if (_page == Page.AssetDumper)
            {
                if (_assetSubMenu != AssetDumpSubMenu.None)
                {
                    if (ch == '1' || ch == '2' || ((ch == '3' || ch == '4') && _assetSubMenu == AssetDumpSubMenu.FieldBackgrounds))
                    {
                        _assetSubSelection = ch - '1';
                        ActivateAssetSelection();
                        return;
                    }
                }
                else if (ch >= '1' && ch <= '5')
                {
                    _assetSelection = ch - '1' + 1;
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
                        if (!_keyboardDown[vk] || (IsTextEntryActive && RepeatTextKey(vk)))
                        {
                            _keyboardDown[vk] = true;

                            // This is a system-wide hook: never edit/navigate MDC from
                            // keystrokes intended for the game, chat, or another app.
                            if (!IsWindowVisible(_windowHandle) || !IsConsoleForeground())
                                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                            // Let Windows update Shift/Caps Lock state before translating
                            // the next character. Editing repeats do not repeat menu actions.
                            if (IsTextEntryActive && TextModifier(vk))
                                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

                            if (IsWindowVisible(_windowHandle) && IsConsoleForeground() &&
                                vk == 0x43 && (GetKeyState(VK_CONTROL) & 0x8000) != 0 &&
                                _page == Page.SoundBrowser &&
                                (_soundMode == SoundBrowserMode.Entries || _soundMode == SoundBrowserMode.SfxEntries || _soundMode == SoundBrowserMode.FieldEntries))
                            {
                                CopySelectedSequenceCall();
                                return new IntPtr(1);
                            }
                            if (IsWindowVisible(_windowHandle) &&
                                vk == VK_A &&
                                (GetKeyState(VK_CONTROL) & 0x8000) != 0 &&
                                !IsTextEntryActive &&
                                _textHandle != IntPtr.Zero)
                            {
                                SendMessage(_textHandle, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                                return new IntPtr(1);
                            }

                            if (IsWindowVisible(_windowHandle) && HandleNavigationKey(vk))
                            {
                                // Consume navigation so the read-only text control
                                // cannot move its caret/selection behind the menu.
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
            if (Memoria.Configuration.Debug.StartModelViewer)
            {
                if (vk == VK_ESCAPE)
                {
                    Action closeViewer = ModelViewerRequested;
                    if (closeViewer != null) closeViewer();
                }
                return vk != 0x79; // Keep F10 available; no background menu actions.
            }
            if (_page == Page.BattleSceneEditor) return HandleBattleKey(vk);
            if (IsTextEntryActive && vk != VK_RETURN && vk != VK_ESCAPE && vk != 0x79)
                return HandleTextEntryKey(vk);
            if (_page == Page.LiveLogFilters) return HandleLogFilterKey(vk);
            if (_page == Page.LiveLog && vk == VK_F) { OpenLogFilters(); return true; }
            if (_page == Page.Home && (vk == 0x53 || vk == 0x46))
            {
                _homeSelection = vk == 0x53 ? 11 : 12;
                ActivateHomeSelection();
                return true;
            }
            if (_page == Page.Home && (vk == VK_LEFT || vk == VK_RIGHT))
            {
                Int32[] source = Array.IndexOf(HomeLeft, _homeSelection) >= 0 ? HomeLeft : HomeRight;
                Int32[] target = source == HomeLeft ? HomeRight : HomeLeft;
                _homeSelection = target[Math.Min(Array.IndexOf(source, _homeSelection), target.Length - 1)];
                RenderHome();
                return true;
            }
            if (_page == Page.Home && vk == 0x4C) { RenderLiveLog(); return true; }
            if (_page == Page.WorldEntrances) return HandleWorldKey(vk);
            if (_page == Page.Home && vk == 0x43) { _homeSelection=13; ActivateHomeSelection(); return true; }
            if (_page == Page.Home && vk == 0x4D) { _homeSelection = 5; ActivateHomeSelection(); return true; }
            if (_page == Page.Home && vk == 0x30) { _homeSelection = 9; ActivateHomeSelection(); return true; }
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
                if (_page == Page.AssetDumper && _assetSubMenu != AssetDumpSubMenu.None && !_assetIdEntryActive)
                {
                    Boolean wasFieldCreator = _assetSubMenu == AssetDumpSubMenu.FieldBackgrounds;
                    _assetSubMenu = AssetDumpSubMenu.None;
                    _assetSubSelection = 0;
                    if (wasFieldCreator) RenderHome(); else RenderAssetDumper();
                    return true;
                }
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
                    _compositePngSelectionActive = false;
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

            if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.SaveList &&
                (vk == VK_LEFT || vk == VK_RIGHT))
            {
                Int32 count = GetNamedSaveCount();
                _saveListSelection = Math.Max(0, Math.Min(Math.Max(0, count - 1),
                    _saveListSelection + (vk == VK_LEFT ? -SavePageSize : SavePageSize)));
                RenderFieldStateStatus();
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

            if (_page == Page.SoundBrowser && vk == VK_A)
            {
                BeginSoundAlias();
                return true;
            }
            if (_page == Page.SoundBrowser && vk == VK_F)
            {
                BeginSoundFilter();
                return true;
            }

            if (_page == Page.SoundBrowser && (vk == VK_S || vk == VK_X))
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
                    _homeSelection = HomeShortcut(vk - VK_1 + 1);
                    ActivateHomeSelection();
                    return true;
                }
            }
            else if (_page == Page.AssetDumper)
            {
                if (_assetSubMenu != AssetDumpSubMenu.None && vk >= VK_1 && vk <= (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds ? VK_4 : VK_2))
                {
                    _assetSubSelection = vk - VK_1;
                    ActivateAssetSelection();
                    return true;
                }
                if (_assetSubMenu == AssetDumpSubMenu.None && vk >= VK_1 && vk <= VK_5)
                {
                    _assetSelection = vk - VK_1 + 1;
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
            if (_page == Page.ConsoleColors) { _consoleColorRow=WrapSelection(_consoleColorRow+delta,ConsoleTheme.Names.Length); RenderConsoleColors(); return; }
            if (_page == Page.Home)
            {
                Int32[] column = Array.IndexOf(HomeLeft, _homeSelection) >= 0 ? HomeLeft : HomeRight;
                _homeSelection = column[WrapSelection(Array.IndexOf(column, _homeSelection) + delta, column.Length)];
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
                if (_assetSubMenu != AssetDumpSubMenu.None)
                    _assetSubSelection = WrapSelection(_assetSubSelection + delta, _assetSubMenu == AssetDumpSubMenu.FieldBackgrounds ? 4 : 2);
                else
                    _assetSelection = 1 + WrapSelection(_assetSelection - 1 + delta, 5);
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
            if (_page == Page.ConsoleColors) { ApplyConsoleColor(); return; }
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
                case 13: _consoleColorRow=ConsoleTheme.Selected; RenderConsoleColors(); return;
                case 11:
                case 12:
                    Int32 kind = _homeSelection == 11 ? 1 : 2;
                    RenderModelViewer();
                    Action<Int32> viewerHandler = ViewerRequested;
                    if (viewerHandler != null) viewerHandler(kind);
                    return;
                case 10:
                    OpenBattleSceneEditor();
                    return;
                case 6:
                    _assetSubMenu = AssetDumpSubMenu.FieldBackgrounds;
                    _assetSubSelection = 0;
                    RenderAssetDumper();
                    return;

                case 0:
                    RenderCompileRestart();

                    Action compileHandler = CompileRestartRequested;
                    if (compileHandler != null)
                        compileHandler();
                    return;

                case 5:
                    RenderModelViewer();

                    Action modelViewerHandler = ModelViewerRequested;
                    if (modelViewerHandler != null)
                        modelViewerHandler();
                    return;

                case 1:
                    Action hardResetHandler = HardResetRequested;
                    if (hardResetHandler != null)
                        hardResetHandler();
                    return;

                case 3:
                    RenderFieldState();

                    Action fieldOpenedHandler = FieldStatePageOpened;
                    if (fieldOpenedHandler != null)
                        fieldOpenedHandler();
                    return;

                case 9:
                    _assetSubMenu = AssetDumpSubMenu.None;
                    RenderAssetDumper();
                    return;

                case 2:
                    Action launcherHandler = ResetToLauncherRequested;
                    if (launcherHandler != null)
                        launcherHandler();
                    return;

                case 4:
                    RenderWarp();
                    return;

                case 8:
                    OpenWorldEntrances();
                    return;
                case 7:
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
            if (_assetSubMenu == AssetDumpSubMenu.Other)
            {
                if (_assetSubSelection == 0)
                {
                    SetAssetDumpStatus("Battle Script byte dump requested...");
                    Action handler = BattleScriptDumpRequested;
                    if (handler != null) handler();
                }
                else
                {
                    SetAssetDumpStatus("All SFX / SEQ data dump requested...");
                    Action handler = SfxDumpRequested;
                    if (handler != null) handler();
                }
                return;
            }
            if (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds)
            {
                if (_assetSubSelection == 1)
                {
                    SetAssetDumpStatus("Dump current field requested...");
                    Action currentHandler = CurrentFieldDumpRequested;
                    if (currentHandler != null) currentHandler();
                }
                else
                    BeginAssetIdEntry(true);
                return;
            }

            if (_assetSubMenu == AssetDumpSubMenu.Textures2D)
            {
                if (_assetSubSelection == 0)
                {
                    SetAssetDumpStatus("2D Texture Dump requested. Starting complete game dump...");
                    Action handler = TextureDumpRequested;
                    if (handler != null) handler();
                }
                else
                    BeginAssetIdEntry(false);
                return;
            }

            if (_assetSelection == 5)
            {
                BeginAssetIdEntry(false);
                return;
            }
            if (_assetSelection == 1)
            {
                _assetSubMenu = AssetDumpSubMenu.Textures2D;
                _assetSubSelection = 0;
                RenderAssetDumper();
            }
            else if (_assetSelection == 2)
            {
                SetAssetDumpStatus("Current Battle Background PNG dump requested...");
                Action handler = CurrentBattleBackgroundDumpRequested;
                if (handler != null) handler();
            }
            else if (_assetSelection == 3)
            {
                SetAssetDumpStatus("World map texture dump requested...");
                Action handler = WorldMapTextureDumpRequested;
                if (handler != null) handler();
            }
            else if (_assetSelection == 4)
            {
                _assetSubMenu = AssetDumpSubMenu.Other;
                _assetSubSelection = 0;
                RenderAssetDumper();
            }
        }

        private void BeginAssetIdEntry(Boolean isField)
        {
            _assetIdEntryActive = true;
            _compositePngSelectionActive = false;
            _assetIdEntryIsField = isField;
            _assetIdEntryText = String.Empty;
            RenderAssetDumper();
        }

        private void SubmitAssetIdEntry()
        {
            Int32 id;
            if (!Int32.TryParse(_assetIdEntryText, out id) &&
                !(_assetIdEntryIsField && !_compositePngSelectionActive &&
                  String.IsNullOrEmpty(_assetIdEntryText) && (id = _lastFieldCreatorId) >= 0))
            {
                SetAssetDumpStatus("Enter a numeric " + (_assetIdEntryIsField ? "field" : "model") + " ID first.");
                return;
            }

            Boolean isField = _assetIdEntryIsField;
            _assetIdEntryActive = false;
            _assetIdEntryText = String.Empty;

            if (isField && _compositePngSelectionActive)
            {
                _compositePngSelectionActive = false;
                if (id < 1 || id > _compositeInputPngs.Length)
                {
                    _assetIdEntryActive = true;
                    _compositePngSelectionActive = true;
                    SetAssetDumpStatus("Invalid PNG selection. Choose a number from the displayed list.");
                    return;
                }
                String selectedImage = _compositeInputPngs[id - 1];
                Action<Int32, String> cutHandler = FieldCompositeCutRequested;
                if (cutHandler != null) cutHandler(_compositeCutFieldId, selectedImage);
                else SetAssetDumpStatus("Field composite cutter handler unavailable.");
                return;
            }

            if (isField)
            {
                if (id < 0 || id > 8999)
                {
                    SetAssetDumpStatus("Field ID must be between 0 and 8999.");
                    return;
                }
                _lastFieldCreatorId = id;
                if (_assetSubSelection >= 2)
                {
                    String problem = FieldCreatorWorkflow.ValidateSource(id);
                    if (problem != null) { SetAssetDumpStatus(problem); return; }
                }
                SetAssetDumpStatus("Field ID " + id + " requested...");
                Action<Int32> handler = FieldBackgroundByIdRequested;
                if (_assetSubSelection == 3)
                {
                    BeginCompositePngSelection(id);
                }
                else if (_assetSubSelection == 2)
                {
                    Action<Int32> compositeHandler = FieldCompositeByIdRequested;
                    if (compositeHandler != null) compositeHandler(id);
                }
                else if (handler != null)
                    handler(id);
            }
            else
            {
                SetAssetDumpStatus("Model ID " + id + " requested...");
                Action<Int32> handler = _assetSubMenu == AssetDumpSubMenu.None && _assetSelection == 5 ? FbxByModelIdRequested : TextureByModelIdRequested;
                if (handler != null)
                    handler(id);
            }
        }

        // Unity's embedded console cannot reliably open System.Windows.Forms dialogs.
        // Use a numeric, in-console file selection instead; no desktop UI dependency.
        private void BeginCompositePngSelection(Int32 fieldId)
        {
            String inputDir = Path.Combine(Path.Combine(Path.Combine(
                Path.GetFullPath(Environment.CurrentDirectory), "Memoria Dev Console"), "Asset Dump"), "FieldDumper_Input");
            try
            {
                Directory.CreateDirectory(inputDir);
                _compositeInputPngs = Directory.GetFiles(inputDir, "*.png", SearchOption.TopDirectoryOnly);
                Array.Sort(_compositeInputPngs, StringComparer.OrdinalIgnoreCase);
                _compositeCutFieldId = fieldId;
                if (_compositeInputPngs.Length == 0)
                {
                    SetAssetDumpStatus("No PNG files found. Copy your redesigned composite into:\r\n" + inputDir +
                        "\r\nThen choose [4] Recut Edited PNG and select template field " + fieldId + " again.");
                    return;
                }
                _compositePngSelectionActive = true;
                _assetIdEntryActive = true;
                _assetIdEntryIsField = true;
                _assetIdEntryText = String.Empty;
                RenderAssetDumper();
            }
            catch (Exception ex)
            {
                SetAssetDumpStatus("Unable to list composite PNG files: " + ex.Message);
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
            Boolean enteringHome = _page != Page.Home || !_styledHomeSized;
            _page = Page.Home;
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1");
            if (enteringHome)
            {
                _styledHomeSized = true;
                FitConsoleToContent(new String(' ', 90) + new String('\n', 39) + ".");
                RECT homeClient; GetClientRect(_windowHandle,out homeClient);
                _homeFontScale=homeClient.Bottom/960.0;
                FitConsoleToContent(new String(' ',64)+new String('\n',26)+".");
                GetClientRect(_windowHandle,out homeClient);
                _homeLayoutHeight=homeClient.Bottom;
            }
            _homeHover = -2; _homeLiveSelected = false;
            RefreshHomeDescription();
            LayoutControls();
            InvalidateRect(_windowHandle, IntPtr.Zero, false);
            SetFocus(_windowHandle);
        }
        private static String GetFieldStateDescription(Int32 selection)
        {
            switch (selection)
            {
                case 0: return "Load the previous snapshot from the rolling history of up to 10 field entrances.\r\n";
                case 1: return "Return toward newer field snapshots after moving back through history.\r\n";
                case 2: return "Capture a field checkpoint that stays separate from the rolling history.\r\n";
                case 3: return "Restore your field checkpoint to repeat an encounter, event, or script test.\r\n";
                case 4: return "Create a named development snapshot with a screenshot on a field or world map.\r\nSnapshots are stored separately from normal game saves and are not overwritten automatically.\r\n";
                case 5: return "Browse named snapshots; the selected entry shows its screenshot and party below.\r\nPress Enter to load, N to create a snapshot, or Delete to remove the selected entry.\r\n";
            }
            return String.Empty;
        }
        private static readonly Int32[] HomeLeft = { 0, 1, 2, 3, 6, 8, 10, 4 };
        private static readonly Int32[] HomeRight = { 5, 11, 12, 7, 9, 13 };

        private static Int32 HomeShortcut(Int32 digit)
        {
            switch (digit)
            {
                case 0: return 9;
                case 6: return 6;
                case 7: return 8;
                case 8: return 10;
                case 9: return 7;
                default: return digit - 1;
            }
        }
        private String HomeCell(Int32 index, String text)
        {
            String cell = (index == _homeSelection ? " >> " : "    ") + text;
            return (index == _homeSelection ? Yellow(cell) : cell) + new String(' ', Math.Max(0, 55 - cell.Length));
        }

        private String RenderHomeColumns()
        {
            return "    SESSION".PadRight(51) + "    VIEWERS\r\n\r\n" +
                HomeCell(0, "[1] Compile + Restart") + HomeCell(5, "[M] Model Viewer") + "\r\n" +
                HomeCell(1, "[2] Restart Game") + HomeCell(11, "[S] SFX Previewer") + "\r\n" +
                HomeCell(2, "[3] Return to Launcher") + HomeCell(12, "[F] Rendered Field Viewer") + "\r\n" +
                HomeCell(3, "[4] Save States") + "\r\n\r\n" +
                "    ADVANCED TOOLS".PadRight(51) + "    ASSETS & AUDIO\r\n\r\n" +
                HomeCell(6, "[6] Field Background Toolkit") + HomeCell(7, "[9] Sound Browser") + "\r\n" +
                HomeCell(8, "[7] World Entrance Creator") + HomeCell(9, "[0] Asset Dumper") + "\r\n" +
                HomeCell(10, "[8] Battle Scene Editor") + "\r\n\r\n" +
                "    EXPERIMENTAL\r\n\r\n" +
                HomeCell(4, "[5] Warp / Story Jump") + "\r\n";
        }
        private static String GetHomeDescription(Int32 selection)
        {
            switch (selection)
            {
                case 11: return "Preview battle SFX animations and identify their effect IDs.\r\nBrowse effects and replay the selected animation.\r\n";
                case 13: return "Choose the color of console headers and selection highlights. Saved between sessions.\r\n";
                case 12: return "View fully rendered fields with live SPS effects and music.\r\nBrowse by field ID and identify the field's SPS and audio IDs.\r\n";
                case 10: return "Edit the active battle camera and each party/enemy home position.\r\nSave battle-specific JSON layouts and export a standalone mod runtime.\r\n";
                case 6: return "Extract and reconstruct existing FFIX field backgrounds into\r\neditable composite images. Cut modified artwork back into the\r\noriginal background tile layout for reimport.\r\n";
                case 0: return "Compile Memoria scripts and restart FFIX after a successful build.\r\nIf compilation fails, fix the source and press R to retry.\r\n";
                case 5: return "Inspect models, animations, bones, weapons, and textures from field, world, or battle.\r\n" + StatusText("Press F11 in the viewer to export the selected model as FBX.") + "\r\n";
                case 1: return "Restart FFIX directly without opening the launcher.\r\nUse after changing patches or data that require a fresh game process.\r\n";
                case 3: return "Return to recent field snapshots, set a checkpoint, or create named snapshots.\r\nNamed snapshots support field and world maps; they are separate from normal game saves.\r\n";
                case 9: return "Export model textures, battle backgrounds, scripts, SFX, and world map textures.\r\nField exports and artwork tools are in Field Background Toolkit on the home menu.\r\n";
                case 2: return "Close FFIX and reopen the Memoria launcher.\r\nUse to change enabled mods, mod order, or launcher settings.\r\n";
                case 4: return "Jump to a story checkpoint or enter a Field ID and ScenarioCounter.\r\nExperimental: certain scene warps can break the current game state.\r\nKeep a normal game save before testing warps.\r\n";
                case 8: return "Place custom entrances on the FFIX world map and export them to your mod using JSON and C# runtime files.\r\n\r\n" + CyanText("Setting Up the Return in Hades Workshop") + "\r\nEntering a custom entrance saves the player's world map position and sets " + CyanText("MemoriaDictionary(988, 0)") + " to " + CyanText("1") + ". Setting it back to " + CyanText("0") + " requests a return to that saved position.\r\n\r\nOpen the field containing your desired return exit in Hades Workshop and locate its " + CyanText("Region*_Range") + " function. Use MDC's Copy HWS Return Block, placing it after the movement guard and before existing exit actions.\r\n\r\n" + CyanText("Note: Return exits require manual field scripting. Do not reuse this dictionary flag for unrelated systems.") + "\r\n";
                case 7: return "Find and preview sounds, music, songs, and movie audio.\r\nPress Enter once to replace the current preview with the selected sound.\r\n";
            }
            return String.Empty;
        }
        private static String MenuLine(Boolean selected, String text)
        {
            return "<MDC_ITEM>" + (selected ? Yellow(" >> " + text) : "    " + text) + "</MDC_ITEM>\r\n";
        }

        // --- Shared page-chrome helpers -------------------------------------------------
        // Every page used to build its own separators/footers by hand, which is how the
        // dash counts and hint wording quietly drifted out of sync between pages. Routing
        // everything through these few helpers keeps new pages consistent for free.

        private const Int32 ConsoleWidth = 110;

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
            if (!String.IsNullOrEmpty(navigate)) builder.Append(navigate);
            if (!String.IsNullOrEmpty(select)) builder.Append("    ").Append(select);
            builder.AppendLine();
            if (!String.IsNullOrEmpty(extra)) builder.Append(extra).Append("    ");
            builder.AppendLine(back);
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
            if (_soundMode != SoundBrowserMode.Entries && _soundMode != SoundBrowserMode.SfxGroups && _soundMode != SoundBrowserMode.FieldGroups &&
                _soundMode != SoundBrowserMode.SfxEntries && _soundMode != SoundBrowserMode.FieldEntries)
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
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Sound Browser");

            StringBuilder builder = new StringBuilder();
            builder.Append("\r\n");
            builder.Append(Header("SOUND BROWSER"));

            if (_soundCatalog == null)
            {
                builder.AppendLine("Loading audio catalog...");
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
                builder.Append(MenuLine(_soundEntrySelection == 5, "Music".PadRight(26) + "(" + _soundCatalog.Music.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 6, "SType=Song Samples / Cues".PadRight(26) + "(" + _soundCatalog.Songs.Count + ")"));
                builder.Append(MenuLine(_soundEntrySelection == 7, "Movie Audio".PadRight(26) + "(" + _soundCatalog.MovieAudio.Count + ")"));
                builder.AppendLine();

            }
            else if (_soundMode == SoundBrowserMode.SfxGroups)
            {
                List<DevSfxSoundGroup> groups = GetFilteredSfxGroups();
                AppendSoundHeader(builder, "SFX EMBEDDED SOUND BANKS", groups.Count);
                Int32 start = (_soundEntrySelection / SoundPageSize) * SoundPageSize;
                for (Int32 i = start; i < Math.Min(start + SoundPageSize, groups.Count); i++)
                    builder.Append(MenuLine(i == _soundEntrySelection, "SFX " + groups[i].SfxId.ToString().PadLeft(4) + "  " + Shorten(groups[i].Name, 48) + "  [" + groups[i].Sounds.Count + "]"));

            }
            else if (_soundMode == SoundBrowserMode.FieldGroups)
            {
                List<DevFieldSoundGroup> groups = GetFilteredFieldGroups();
                AppendSoundHeader(builder, "FIELD LOCATIONS WITH SOUND EFFECTS", groups.Count);
                Int32 start = (_soundEntrySelection / SoundPageSize) * SoundPageSize;
                for (Int32 i = start; i < Math.Min(start + SoundPageSize, groups.Count); i++)
                    builder.Append(MenuLine(i == _soundEntrySelection, Shorten(groups[i].Field, 68) + "  [" + groups[i].Sounds.Count + "]"));

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
                    String id = entry.Category == DevSoundCategory.SfxSound
                        ? "SFX " + entry.SfxId
                        : entry.Category == DevSoundCategory.ResidentSfxSound
                            ? "RESIDENT"
                            : "ID " + entry.Id;
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
                    builder.AppendLine("SEQUENCE COMMAND: " + selected.SequenceCall);
                    if (selected.Category == DevSoundCategory.SfxSound || selected.Category == DevSoundCategory.ResidentSfxSound)
                        builder.AppendLine("SFX BANK SLOT: local " + selected.LocalSlot + " / runtime " + selected.RuntimeSlot);
                    if (!String.IsNullOrEmpty(selected.Identity))
                        builder.AppendLine("IDENTITY: " + selected.Identity);
                    if (!String.IsNullOrEmpty(selected.Usage))
                        builder.AppendLine("VANILLA USE: " + selected.Usage);
                }

            }

            String status;
            lock (_soundBrowserLock)
                status = _pendingSoundBrowserStatus;
            builder.AppendLine();
            builder.AppendLine("STATUS: " + StatusText(status ?? String.Empty));
            if (_soundMode == SoundBrowserMode.Categories)
                builder.Append(Footer(NavigateHint, "Enter: Open", "S/X: Stop preview", BackHint));
            else AppendSoundFooter(builder, GetSoundSelectionCount());
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

        private void CopySelectedSequenceCall()
        {
            List<DevSoundEntry> entries = GetFilteredSoundEntries();
            if (_soundEntrySelection < 0 || _soundEntrySelection >= entries.Count)
            {
                SetSoundBrowserStatus("No sound selected to copy.");
                return;
            }
            String command = entries[_soundEntrySelection].SequenceCall;
            IntPtr memory = IntPtr.Zero;
            Boolean opened = false;
            try
            {
                Byte[] bytes = Encoding.Unicode.GetBytes(command + "\0");
                memory = GlobalAlloc(0x0002, new UIntPtr((UInt32)bytes.Length));
                if (memory == IntPtr.Zero) throw new InvalidOperationException("Could not allocate clipboard text.");
                IntPtr data = GlobalLock(memory);
                if (data == IntPtr.Zero) throw new InvalidOperationException("Could not prepare clipboard text.");
                try { Marshal.Copy(bytes, 0, data, bytes.Length); }
                finally { GlobalUnlock(memory); }
                opened = OpenClipboard(_windowHandle);
                if (!opened) throw new InvalidOperationException("Clipboard is busy. Try Ctrl+C again.");
                if (!EmptyClipboard() || SetClipboardData(13, memory) == IntPtr.Zero)
                    throw new InvalidOperationException("Could not copy the SEQ call.");
                memory = IntPtr.Zero; // Windows owns the buffer after SetClipboardData.
                SetSoundBrowserStatus("Copied SEQ call: " + command);
            }
            catch (Exception ex) { SetSoundBrowserStatus(ex.Message); }
            finally
            {
                if (opened) CloseClipboard();
                if (memory != IntPtr.Zero) GlobalFree(memory);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(UInt32 format, IntPtr memory);
        [DllImport("user32.dll")]
        private static extern Boolean CloseClipboard();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(UInt32 flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern Boolean GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);
        private void AppendSoundFooter(StringBuilder builder, Int32 count)
        {
            builder.AppendLine();
            builder.Append(Footer(
                "Up/Down: Select    Left/Right: Page",
                (_soundMode == SoundBrowserMode.SfxGroups || _soundMode == SoundBrowserMode.FieldGroups) ? "Enter: Open" : "Enter: Play",
                (_soundMode == SoundBrowserMode.SfxGroups || _soundMode == SoundBrowserMode.FieldGroups)
                    ? "F: Filter    S/X: Stop" : "Ctrl+C: Copy SEQ    F: Filter    A: Rename    S/X: Stop",
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

        private void RenderLiveLog(Boolean focus = true)
        {
            _page = Page.LiveLog;
            HideSaveThumbnail();

            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Live Log");

            StringBuilder builder = new StringBuilder();

            lock (_appendLock)
            {
                builder.Append(FilteredLogHistory(_liveLogBuffer.ToString()));
            }

            SetConsoleText(builder.ToString());
            ScrollBottom();
            if (focus) SetFocus(_windowHandle);
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

            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Compile + Restart");

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
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Model Viewer");
            SetConsoleText(BuildModelViewerText("Preparing viewer transition..."));
            SetFocus(_windowHandle);
        }

        private static String BuildModelViewerText(String status)
        {
            Boolean active = Memoria.Configuration.Debug.StartModelViewer;
            return "\r\n" + Header("MODEL VIEWER") + "\r\n" +
                "STATE: " + (active ? "Open — game simulation paused." : "Closed — returned to the game.") + "\r\n\r\n" +
                "Inspect models and animations in the game window.\r\n" +
                "The console hides when the viewer opens. Escape returns to the game.\r\n" +
                "F10 shows or hides the console.\r\n" +
                "Closing from a field restores its latest stable entrance snapshot;\r\n" +
                "world maps and battles resume in place.\r\n\r\n" +
                StatusText("F11: Export Model Asset (FBX + textures)") + "\r\n" +
                Yellow("Output: Memoria Dev Console\\Asset Dump\\FBX\\") + "\r\n\r\n" +
                "STATUS: " + StatusText(status ?? String.Empty) + "\r\n" +
                Footer(null, null, "F10: Hide console", active ? "ESC: Close Model Viewer" : BackHint);
        }

        public void SetModelViewerStatus(String status)
        {
            if (!_running || _textHandle == IntPtr.Zero) return;
            SetConsoleText(BuildModelViewerText(status));
        }
        private void RenderWarp()
        {
            _page = Page.Warp;
            HideSaveThumbnail();
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Story Warper");

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
            body.Append(StatusText(status ?? String.Empty)).Append("\r\n\r\n");
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
            SetWindowText(_windowHandle, _assetSubMenu == AssetDumpSubMenu.FieldBackgrounds
                ? "Memoria Dev Console v1.4.1 - Field Background Toolkit" : "Memoria Dev Console v1.4.1 - Asset Dumper");

            String status;
            lock (_assetDumpLock)
                status = _pendingAssetDumpText;

            if (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds && _assetSubSelection == 3 && _assetIdEntryActive)
            {
                String prompt = CyanText("RECUT EDITED PNG") + "\r\n\r\n";
                if (_compositePngSelectionActive)
                    prompt += CyanText("Step 2 of 2 - Select Edited PNG") + "\r\n" +
                        "Template field: " + _compositeCutFieldId + "\r\n\r\n" +
                        "Choose a PNG number from the list below.\r\n" +
                        "Place edited PNGs in Asset Dump\\FieldDumper_Input.\r\n\r\n" +
                        GetCompositePngList();
                else
                    prompt += CyanText("Step 1 of 2 - Select Template Field") + "\r\n\r\n" +
                        "Enter the ID of the field to use as your template.\r\n" +
                        String.Empty +
                        (_lastFieldCreatorId >= 0 ? "Leave blank to use field " + _lastFieldCreatorId + ".\r\n" : "");
                if (!String.IsNullOrEmpty(status) &&
                    (status.StartsWith("Invalid",StringComparison.OrdinalIgnoreCase) ||
                     status.StartsWith("Unable",StringComparison.OrdinalIgnoreCase) ||
                     status.StartsWith("Missing",StringComparison.OrdinalIgnoreCase) ||
                     status.StartsWith("Enter a numeric",StringComparison.OrdinalIgnoreCase)))
                    prompt += "\r\n" + StatusText(status) + "\r\n";
                SetConsoleText(prompt);
                SetFocus(_windowHandle);
                return;
            }
            String menu;
            String description;
            String important;

            if (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds)
            {
                menu =
                    MenuLine(_assetSubSelection == 0, "[1] Export Field by ID") +
                    MenuLine(_assetSubSelection == 1, "[2] Export Current Field") +
                    MenuLine(_assetSubSelection == 2, "[3] Create Editable Composite PNG") +
                    MenuLine(_assetSubSelection == 3, "[4] Recut Edited PNG");
                description = _assetSubSelection == 0
                    ? "Enter a field ID to export its background resources.\r\n"
                    : _assetSubSelection == 1
                    ? "Export the field currently loaded in FFIX.\r\n"
                    : _assetSubSelection == 2
                    ? "Combine an exported field's camera-0 layers into one PNG. Original files remain unchanged.\r\n"
                    : "Use an existing field as a template to recut your edited PNG into its original camera layers.\r\n";
                important = _assetSubSelection == 2
                    ? "Requires an exported field. Static camera-0 composite; animation and in-game tint may differ.\r\n"
                    : _assetSubSelection == 3
                    ? "Place edited PNGs in Asset Dump\\FieldDumper_Input.\r\n"
                    : "Designed for editing vanilla field artwork, not creating entirely new field scenes or geometry.\r\n";
                if (_assetSubSelection >= 2) important += "Designed for editing vanilla field artwork, not creating entirely new field scenes or geometry.\r\n";
            }
            else if (_assetSubMenu == AssetDumpSubMenu.Other)
            {
                menu = MenuLine(_assetSubSelection == 0, "[1] Battle Script Byte Dump") +
                    MenuLine(_assetSubSelection == 1, "[2] All SFX / SEQ Data");
                description = _assetSubSelection == 0
                    ? "Export raw battle event-script bytes and BattleScriptSizes.csv.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\BattleScriptDump\\") + "\r\n"
                    : "Export known native SFX effects: raw bytes, sequence text, metadata, and available camera/AKAO data.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\SFXDump\\") + "\r\n";
                important = "Reference-data exports. These operations may pause briefly while scanning assets.\r\n";
            }
            else if (_assetSubMenu == AssetDumpSubMenu.Textures2D)
            {
                menu =
                    MenuLine(_assetSubSelection == 0, "[1] Complete Game") +
                    MenuLine(_assetSubSelection == 1, "[2] Export by Model ID");
                description = _assetSubSelection == 0
                    ? "Loads every known GEO model and exports its runtime 2D textures as PNG files.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\2D Texture Dump Output\\") + "\r\n"
                    : "Enter one exact GEO model ID and export only that model's runtime 2D textures.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\2D Texture Dump Output\\") + "\r\n";
                important = _assetSubSelection == 0
                    ? "This dump operation creates a large number of files and may take a while.\r\n"
                    : "Single-model lookup. Enter the numeric model ID after selecting this option.\r\n";
            }
            else
            {
                menu =
                    MenuLine(_assetSelection == 1, "[1] 2D Texture Dump") +
                    MenuLine(_assetSelection == 2, "[2] Current Battle Background PNGs") +
                    MenuLine(_assetSelection == 3, "[3] World Map 2D Texture Dump") +
                    MenuLine(_assetSelection == 4, "[4] Other") + MenuLine(_assetSelection == 5, "[5] Dump FBX Model by ID");
                description = GetAssetDumpDescription(_assetSelection);
                important = "2D Texture Dump opens a submenu for Complete Game or ID lookup.\r\n";
            }

            SetConsoleText(
                "\r\n" + Header(_assetSubMenu == AssetDumpSubMenu.None ? "ASSET DUMPER" : (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds ? "FIELD BACKGROUND TOOLKIT" : (_assetSubMenu == AssetDumpSubMenu.Other ? "ASSET DUMPER / OTHER" : "ASSET DUMPER / 2D TEXTURES"))) +
                "\r\n" + menu + "\r\n" + Rule() + "DESCRIPTION\r\n" + Rule() + description + "\r\n" +
                (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds
                    ? "WORKFLOW\r\n" + Rule() +
                      "Export -> Create PNG -> Edit artwork -> Recut\r\n" +
                      (_lastFieldCreatorId >= 0 ? "Selected source: Field_" + _lastFieldCreatorId.ToString("D4") + " (blank field ID reuses this)\r\n" : "Select a source field using option [1] or [2].\r\n") +
                      "Folders: Memoria Dev Console\\Asset Dump\\\r\n" +
                      "O opens the current export, or latest recut folder on option [4].\r\n\r\n"
                    : String.Empty) +
                "IMPORTANT\r\n" + Rule() + important + (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds ? String.Empty : Yellow("All dumps: Memoria Dev Console\\Asset Dump\\")) + "\r\n\r\n" +
                (_assetIdEntryActive
                    ? (_compositePngSelectionActive
                        ? ("STEP 2 OF 2 - CHOOSE EDITED PNG\r\n" + Rule() +
                           CyanText("Template field: " + _compositeCutFieldId + " / Field_" + _compositeCutFieldId.ToString("D4")) + "\r\n" +
                           "Choose a PNG number from the list.\r\n" +
                           GetCompositePngList() +
                           "Type PNG number: " + (_assetIdEntryText.Length == 0 ? "_" : _assetIdEntryText + "_") + "\r\n" +
                           "Enter: Recut Field " + _compositeCutFieldId + "    Backspace: Edit    ESC: Cancel\r\n\r\n")
                        : ((_assetIdEntryIsField ? (_assetSubSelection == 3 ? "STEP 1 OF 2 - SELECT TEMPLATE FIELD" : "FIELD ID LOOKUP") : "MODEL ID LOOKUP") + "\r\n" + Rule() +
                           (_assetIdEntryIsField && _assetSubSelection == 3
                               ? "Enter the ID of the field to use as your template.\r\n"
                               : String.Empty) +
                           "Type numeric ID: " + (_assetIdEntryText.Length == 0 ? "_" : _assetIdEntryText + "_") + "\r\n" +
                           "Enter: Continue    Backspace: Edit    ESC: Cancel\r\n\r\n"))
                    : String.Empty) +
                "STATUS\r\n" + Rule() + StatusText(status ?? String.Empty) + "\r\n\r\n" +
                Footer(NavigateHint, "Enter: Select", "O: Open Output Folder", BackHint));

            SetFocus(_windowHandle);
        }

        private String GetCompositePngList()
        {
            System.Text.StringBuilder lines = new System.Text.StringBuilder();
            for (Int32 i = 0; i < _compositeInputPngs.Length; i++)
                lines.Append("[").Append(i + 1).Append("] ").Append(Path.GetFileName(_compositeInputPngs[i]))
                    .Append(" | ").Append(FieldCreatorWorkflow.DescribePng(_compositeInputPngs[i])).Append("\r\n");
            return lines.ToString() + "\r\n";
        }

        private void OpenAssetOutputFolder()
        {
            try
            {
                String folder;
                if (_assetSubMenu == AssetDumpSubMenu.Other)
                    folder = _assetSubSelection == 0 ? "BattleScriptDump" : "SFXDump";
                else if (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds)
                    folder = "FieldDumper_Output";
                else if (_assetSubMenu == AssetDumpSubMenu.Textures2D || (_assetSubMenu == AssetDumpSubMenu.None && _assetSelection == 1))
                    folder = "2D Texture Dump Output";
                else if (_assetSelection == 2)
                    folder = "BattleBackgroundDumper_Output";
                else if (_assetSelection == 5)
                    folder = "FBX";
                else if (_assetSelection == 3)
                    folder = "WorldMapTextureDumper_Output";
                else
                    folder = "";

                String outputPath = Path.Combine(Path.Combine(Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), "Memoria Dev Console"), "Asset Dump"), folder);
                if (_assetSubMenu == AssetDumpSubMenu.FieldBackgrounds && _lastFieldCreatorId >= 0)
                {
                    String selectedPath = _assetSubSelection == 3
                        ? FieldCreatorWorkflow.LatestCut(_lastFieldCreatorId)
                        : FieldCreatorWorkflow.Source(_lastFieldCreatorId);
                    if (selectedPath != null && Directory.Exists(selectedPath)) outputPath = selectedPath;
                }
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
            if (selection == 5)
                return "Export a GEO model by numeric ID using the Model Viewer FBX exporter.\r\nIncludes its orientation correction, rig, bind pose, and textures.\r\nOutput: Memoria Dev Console\\Asset Dump\\FBX\\\r\n";
            if (selection == 1)
                return "Export runtime GEO model textures. Choose Complete Game or Export by Model ID.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\2D Texture Dump Output\\") + "\r\n";
            if (selection == 2)
                return "Dumps the textures used by the currently loaded battle background as PNG files and writes MaterialMap.csv.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\BattleBackgroundDumper_Output\\") + "\r\n";
            if (selection == 3)
                return "Exports source 2D world-map texture PNGs only, including atlases and animation frames. No material-reference copies. Includes TextureIndex.csv. Can run from a field or the world map.\r\n" + Yellow("Output: Memoria Dev Console\\Asset Dump\\WorldMapTextureDumper_Output\\") + "\r\n";
            return "Other reference-data dumps: battle script bytes and all known native SFX / sequence data.\r\nSelect to open these operations.\r\n";
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

        public void SetFieldCreatorSource(Int32 fieldId)
        {
            _lastFieldCreatorId = fieldId;
        }

        private void RenderFieldState()
        {
            _page = Page.FieldState;
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Save States");
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
            return CyanText("HISTORY") + "\r\n" +
                MenuLine(_fieldSelection == 0, "[1] Previous State") +
                MenuLine(_fieldSelection == 1, "[2] Next State") +
                "\r\n" + CyanText("QUICK CHECKPOINT") + "\r\n" +
                MenuLine(_fieldSelection == 2, "[3] Set Checkpoint") +
                MenuLine(_fieldSelection == 3, "[4] Load Checkpoint") +
                "\r\n" + CyanText("SAVED STATES") + "\r\n" +
                MenuLine(_fieldSelection == 4, "[5] Save New State") +
                MenuLine(_fieldSelection == 5, "[6] Browse Saved States") +
                "\r\n" + CyanText("DESCRIPTION") + "\r\n" +
                GetFieldStateDescription(_fieldSelection) + "\r\n" +
                CyanText("HISTORY STATUS") + "\r\n" + timelineText + "\r\n";
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
                builder.AppendLine("No saved states yet. Press N to create your first snapshot.");
            }
            else
            {
                _saveListSelection = Math.Max(0, Math.Min(_saveListSelection, saves.Count - 1));
                Int32 start = (_saveListSelection / SavePageSize) * SavePageSize;
                builder.AppendLine(saves.Count + " states    Page " + (start / SavePageSize + 1) + "/" + ((saves.Count - 1) / SavePageSize + 1));
                builder.AppendLine("    " + "TITLE".PadRight(28) + "LOCATION".PadRight(38) + "SAVED");
                builder.Append(Rule());
                for (Int32 i = start; i < Math.Min(start + SavePageSize, saves.Count); i++)
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
            builder.AppendLine("STATUS: " + StatusText(status ?? String.Empty));
            builder.Append(Footer("Up/Down: Select    Left/Right: Page", "Enter: Load", "N: New    Delete: Remove", BackHint));

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
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - " + title.Replace(" ", ""));

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
            Boolean trimmed = false;

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
                        trimmed = true;
                    }
                }
            }

            if (String.IsNullOrEmpty(text) || _page != Page.LiveLog || _textHandle == IntPtr.Zero)
                return;

            if (trimmed) RenderLiveLog(false);
            else AppendFilteredLog(text);
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
            if (_page == Page.LiveLog) ShowScrollBar(_textHandle,3,false);
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

        private const String YellowOpen = "<MDC_YELLOW>";
        private const String YellowClose = "</MDC_YELLOW>";

        private static String Yellow(String text)
        {
            return YellowOpen + text + YellowClose;
        }

        private void SetConsoleText(String text)
        {
            if (_page != Page.Home && _page != Page.LiveLog && _page != Page.CompileRestart)
            {
                SetStyledDocument(text);
                return;
            }
            _styledDocumentActive = false;
            if (_textHandle != IntPtr.Zero) ShowWindow(_textHandle, SW_SHOW);
            if (_textHandle == IntPtr.Zero)
                return;

            SendMessage(_textHandle, EM_SETBKGNDCOLOR, IntPtr.Zero, new IntPtr(_page == Page.Home || _page == Page.LiveLog || _page == Page.CompileRestart ? 0x00101010 : 0));
            // RichEdit wraps the home description to its real client width.
            // Other console pages retain their aligned, fixed-column layout.
            SendMessage(_textHandle, 0x0448 /* EM_SETTARGETDEVICE */, IntPtr.Zero, new IntPtr(_page == Page.Home || _page == Page.LiveLog ? 0 : 1));
            if (_page != Page.LiveLog && _page != Page.Home) text = WrapConsoleContent(text, ConsoleWidth);

            // RichEdit indexes paragraphs with one CR, not the two characters in CRLF.
            // Normalize before measuring spans so highlighting matches EM_SETSEL offsets.
            text = text.Replace("\r\n", "\r").Replace("\n", "\r");
            List<Int32[]> yellowRanges = new List<Int32[]>();
            StringBuilder clean = new StringBuilder();
            Int32 source = 0;
            while (source < text.Length)
            {
                Int32 open = text.IndexOf(YellowOpen, source, StringComparison.Ordinal);
                Int32 cyan = text.IndexOf(CyanOpen, source, StringComparison.Ordinal);
                Boolean forceCyan = cyan >= 0 && (open < 0 || cyan < open);
                if (forceCyan) open = cyan;
                Int32 statusOpen = text.IndexOf(StatusOpen, source, StringComparison.Ordinal);
                Boolean forceStatus = statusOpen >= 0 && (open < 0 || statusOpen < open);
                if (forceStatus) { open = statusOpen; forceCyan = false; }
                if (open < 0)
                {
                    clean.Append(text.Substring(source));
                    break;
                }
                clean.Append(text.Substring(source, open - source));
                Int32 start = clean.Length;
                Int32 contentStart = open + (forceStatus ? StatusOpen.Length : forceCyan ? CyanOpen.Length : YellowOpen.Length);
                String closing = forceStatus ? StatusClose : forceCyan ? CyanClose : YellowClose;
                Int32 close = text.IndexOf(closing, contentStart, StringComparison.Ordinal);
                if (close < 0)
                {
                    clean.Append(text.Substring(contentStart));
                    yellowRanges.Add(forceCyan || forceStatus ? new Int32[] { start, clean.Length, (Int32)(forceStatus ? WarningColor : _page==Page.LiveLog ? 0x00FFFF00u : DescriptionHighlightColor) } : (_page == Page.LiveLog ? new Int32[] { start, clean.Length, 0x0000D7FF } : new Int32[] { start, clean.Length }));
                    break;
                }
                clean.Append(text.Substring(contentStart, close - contentStart));
                yellowRanges.Add(forceCyan || forceStatus ? new Int32[] { start, clean.Length, (Int32)(forceStatus ? WarningColor : _page==Page.LiveLog ? 0x00FFFF00u : DescriptionHighlightColor) } : (_page == Page.LiveLog ? new Int32[] { start, clean.Length, 0x0000D7FF } : new Int32[] { start, clean.Length }));
                source = close + closing.Length;
            }

            // Formatting temporarily selects the whole document. Paint only after
            // those selections are cleared so they never flash blue on screen.
            Boolean visible = IsWindowVisible(_textHandle);
            if (visible) SendMessage(_textHandle, 0x000B /* WM_SETREDRAW */, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_textHandle, 0x043F /* EM_HIDESELECTION */, new IntPtr(1), IntPtr.Zero);
            try
            {
                if (_page == Page.LiveLog) FitLiveLogWindow(); else if (_page != Page.Home) FitConsoleToContent(clean.ToString());
                SetWindowText(_textHandle, clean.ToString());
                ApplyConsolePalette(clean.ToString(), yellowRanges);
                ApplyHomeHeadings(clean.ToString());
                LayoutControls();
            }
            finally
            {
                SendMessage(_textHandle, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);
                SendMessage(_textHandle, 0x043F /* EM_HIDESELECTION */, IntPtr.Zero, IntPtr.Zero);
                if (visible) SendMessage(_textHandle, 0x000B /* WM_SETREDRAW */, new IntPtr(1), IntPtr.Zero);
                InvalidateRect(_textHandle, IntPtr.Zero, false);
                InvalidateRect(_windowHandle, IntPtr.Zero, false);
            }
        }

        private void ApplyHomeHeadings(String text)
        {
            ApplyTextSize(0, text.Length, (_page==Page.Home || _page==Page.LiveLog?ConsoleFontPoints-4:ConsoleFontPoints) * 20 + 15);
            if (_page != Page.Home) return;
            Int32 start = 0;
            foreach (String line in text.Split('\r'))
            {
                String value = line.Trim();
                if (value == "[L] Live Log" || value.StartsWith("SESSION") ||
                    value.StartsWith("ADVANCED TOOLS") || value == "EXPERIMENTAL")
                {
                    ApplyTextColor(start, start + line.Length, SelectionColor);
                    ApplyTextSize(start, start + line.Length, ConsoleHeadingPoints * 20);
                }
                start += line.Length + 1;
            }
        }

        private void ApplyTextSize(Int32 start, Int32 end, Int32 twips)
        {
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(start), new IntPtr(end));
            CHARFORMAT2 format = new CHARFORMAT2();
            format.cbSize = (UInt32)Marshal.SizeOf(typeof(CHARFORMAT2));
            format.dwMask = 0x80000000; // CFM_SIZE
            format.yHeight = twips;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CHARFORMAT2)));
            try
            {
                Marshal.StructureToPtr(format, ptr, false);
                SendMessage(_textHandle, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        private void ApplyTextColor(Int32 start, Int32 end, UInt32 color)
        {
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(start), new IntPtr(end));
            CHARFORMAT2 format = new CHARFORMAT2();
            format.cbSize = (UInt32)Marshal.SizeOf(typeof(CHARFORMAT2));
            format.dwMask = CFM_COLOR;
            format.crTextColor = color;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CHARFORMAT2)));
            try
            {
                Marshal.StructureToPtr(format, ptr, false);
                SendMessage(_textHandle, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private void FitLiveLogWindow()
        {
            RECT bounds, client;
            GetWindowRect(_windowHandle,out bounds); GetClientRect(_windowHandle,out client);
            MONITORINFO monitor=new MONITORINFO(); monitor.Size=Marshal.SizeOf(typeof(MONITORINFO));
            if(!GetMonitorInfo(MonitorFromWindow(_windowHandle,2),ref monitor))return;
            Int32 width=Math.Min(1440,monitor.Work.Right-monitor.Work.Left);
            Int32 height=Math.Min(960,monitor.Work.Bottom-monitor.Work.Top);
            Int32 left=Math.Max(monitor.Work.Left,Math.Min(bounds.Left,monitor.Work.Right-width));
            Int32 top=Math.Max(monitor.Work.Top,Math.Min(bounds.Top,monitor.Work.Bottom-height));
            MoveWindow(_windowHandle,left,top,width,height,true);
        }
        [DllImport("user32.dll")]
        private static extern Boolean ShowScrollBar(IntPtr hwnd, Int32 bar, Boolean show);
        private void ScrollBottom()
        {
            if (_textHandle == IntPtr.Zero)
                return;

            Int32 end = GetWindowTextLength(_textHandle);
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(end), new IntPtr(end));
            SendMessage(_textHandle, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_textHandle, WM_VSCROLL, new IntPtr(SB_BOTTOM), IntPtr.Zero);
            if (_page == Page.LiveLog) ShowScrollBar(_textHandle,3,false);
        }

        private void ResizeTextArea(IntPtr lParam)
        {
            LayoutControls();
            if (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.SaveList)
                RenderFieldStateStatus();
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

            if (_page == Page.Home)
            {
                RECT area = HomeRect(38, 740, 1204, 161);
                MoveWindow(_textHandle, area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top, true);
                InvalidateRect(_windowHandle, IntPtr.Zero, false);
                return;
            }
            Int32 reservedAtBottom = _thumbnailVisible ? PreviewStripHeight : 0;
            Int32 textWidth = Math.Max(100, width - 24);
            Int32 textHeight = Math.Max(100, height - 24 - reservedAtBottom);

            if (_page == Page.LiveLog || _page == Page.CompileRestart)
                MoveWindow(_textHandle, 24, 72, Math.Max(100,width-48), Math.Max(80,height-128), true);
            else
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
        // Memoria Dev Console/FF9_Data/FaceAtlas/Face Atlas.png — never from whatever texture mod
        // is active in the live game, so these known-good pixel coordinates (confirmed by
        // hand against that specific file) always line up. Coordinates are the same
        // 132x190 grid cells the community-standard "Face Atlas.tpsheet" already used for
        // this file; only 8 of the 12 playable characters are confirmed placed in it.

        private static String GetPortraitAtlasPath()
        {
            return Path.Combine(
                Path.Combine(
                    Path.Combine(Path.Combine(Environment.CurrentDirectory, "Memoria Dev Console"), "FF9_Data"),
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


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CHARFORMAT2
        {
            public UInt32 cbSize;
            public UInt32 dwMask;
            public UInt32 dwEffects;
            public Int32 yHeight;
            public Int32 yOffset;
            public UInt32 crTextColor;
            public Byte bCharSet;
            public Byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public String szFaceName;
            public UInt16 wWeight;
            public UInt16 sSpacing;
            public UInt32 crBackColor;
            public Int32 lcid;
            public UInt32 dwReserved;
            public Int16 sStyle;
            public UInt16 wKerning;
            public Byte bUnderlineType;
            public Byte bAnimation;
            public Byte bRevAuthor;
            public Byte bReserved1;
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(String lpFileName);
    }
}

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private Boolean _styledHomeSized;
        private IntPtr _headerSmallIcon, _headerLargeIcon;
        private void LoadConsoleHeaderIcons()
        {
            String path=Path.Combine(Environment.CurrentDirectory,@"Memoria Dev Console\FF9_Data\UI\MdcCrystal.ico");
            _headerSmallIcon=LoadImage(IntPtr.Zero,path,1,16,16,0x10);
            _headerLargeIcon=LoadImage(IntPtr.Zero,path,1,32,32,0x10);
            if(_headerSmallIcon!=IntPtr.Zero)SendMessage(_windowHandle,0x80,IntPtr.Zero,_headerSmallIcon);
            if(_headerLargeIcon!=IntPtr.Zero)SendMessage(_windowHandle,0x80,new IntPtr(1),_headerLargeIcon);
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance,String name,UInt32 type,Int32 width,Int32 height,UInt32 flags);
        [DllImport("user32.dll")] private static extern Boolean DestroyIcon(IntPtr icon);
        private Int32 _homeLayoutHeight;
        private Double _homeFontScale = 1.0;
        private Int32 _homeHover = -2;

        private Boolean _homeLiveSelected;

        private IntPtr _homeCrystal;
        private Boolean _homeCrystalAttempted;
        private const UInt32 HomeTimer = 0x4D44;
        private static readonly String[] HomeTitles = {
            "Compile + Restart", "Restart Game", "Return to Launcher", "Save States",
            "Warp / Story Jump", "Model Viewer", "Field Background Toolkit",
            "Sound Browser", "World Entrance Creator", "Asset Dumper", "Battle Scene Editor",
            "SFX Previewer", "Rendered Field Viewer", "Console Color"
        };
        private static readonly String[] HomeKeys = { "1", "2", "3", "4", "5", "M", "6", "9", "7", "0", "8", "S", "F", "C" };

        private Int32 HomeDescriptionIndex { get { return _homeLiveSelected ? -1 : _homeSelection; } }
        private void RefreshHomeDescription()
        {
            Int32 index=HomeDescriptionIndex;
            SetConsoleText(index == -1 ? "Follow Memoria.log while playing, including startup, warnings and errors.\r\nLive Log keeps the game running. Press F for log filters." : GetHomeDescription(index));
            FitHomeDescription();
            InvalidateRect(_windowHandle,IntPtr.Zero,false);
        }
        private RECT HomeRect(Int32 x, Int32 y, Int32 w, Int32 h)
        {
            RECT client;
            GetClientRect(_windowHandle, out client);
            Int32 baseHeight = _homeLayoutHeight > 0 ? _homeLayoutHeight : client.Bottom;
            double sx = Math.Max(1, client.Right) / 1280.0, sy = Math.Max(1, baseHeight) / 960.0;
            Int32 extra = client.Bottom-baseHeight;
            RECT result = new RECT { Left = (Int32)(x*sx), Top = (Int32)(y*sy),
                Right = (Int32)((x+w)*sx), Bottom = (Int32)((y+h)*sy) };
            // Remove header padding without scaling fonts, artwork, or menu rows.
            if(y<916) { Int32 headerTrim=(Int32)(12*sy); result.Top-=headerTrim; result.Bottom-=headerTrim; }
            if(y>=916) { result.Top+=extra; result.Bottom+=extra; }
            else if(y==708 || y==740) result.Bottom+=extra;
            return result;
        }
        private void FitHomeDescription()
        {
            if(_textHandle==IntPtr.Zero || _homeLayoutHeight<=0 || IsZoomed(_windowHandle)) return;
            RECT client,bounds;
            GetClientRect(_windowHandle,out client); GetWindowRect(_windowHandle,out bounds);
            MONITORINFO monitor=new MONITORINFO();monitor.Size=Marshal.SizeOf(typeof(MONITORINFO));
            if(!GetMonitorInfo(MonitorFromWindow(_windowHandle,2),ref monitor))return;
            Int32 index=HomeDescriptionIndex;
            String text=index==-1 ? "Follow Memoria.log while playing, including startup, warnings and errors.\r\nLive Log keeps the game running. Press F for log filters." : GetHomeDescription(index);
            text=text.Replace(YellowOpen,"").Replace(YellowClose,"").Replace(CyanOpen,"").Replace(CyanClose,"").Replace(StatusOpen,"").Replace(StatusClose,"");
            RECT area=HomeRect(38,740,1204,161);
            RECT measure=new RECT { Right=Math.Max(80,area.Right-area.Left-24) };
            IntPtr measureFont=CreateFont(-17,0,0,0,400,0,0,0,1,0,0,0,49,"Consolas");
            IntPtr dc=GetDC(_textHandle),old=SelectObject(dc,measureFont);
            try { DrawText(dc,text,-1,ref measure,0x10|0x400|0x800); }
            finally { SelectObject(dc,old);ReleaseDC(_textHandle,dc);DeleteObject(measureFont); }
            Int32 chrome=bounds.Bottom-bounds.Top-client.Bottom;
            Int32 bottomSpace=Math.Max(38,(Int32)(_homeLayoutHeight*59/960.0));
            Int32 wanted=area.Top+Math.Max(24,measure.Bottom)+10+bottomSpace;
            Int32 height=Math.Min(wanted+chrome,monitor.Work.Bottom-monitor.Work.Top);
            Int32 top=Math.Max(monitor.Work.Top,Math.Min(bounds.Top,monitor.Work.Bottom-height));
            if(height!=bounds.Bottom-bounds.Top || top!=bounds.Top)
                MoveWindow(_windowHandle,bounds.Left,top,bounds.Right-bounds.Left,height,true);
            LayoutControls();
        }
        private RECT HomeButtonRect(Int32 index)
        {
            if (index == -1) return HomeRect(30, 216, 580, 36);
            Int32 column = Array.IndexOf(HomeLeft, index) >= 0 ? 0 : 1;
            Int32 x = column == 0 ? 30 : 665;
            Int32 y;
            switch(index) {
                case 0: case 5: y=300; break;
                case 1: case 11: y=342; break;
                case 2: case 12: y=384; break;
                case 3: y=426; break;
                case 6: case 7: y=510; break;
                case 8: case 9: y=552; break;
                case 10: y=594; break;
                default: y=666; break;
            }
            return HomeRect(x, y, column == 0 ? 580 : 585, 36);
        }
        private static Boolean HomeContains(RECT rect, Int32 x, Int32 y)
        { return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom; }
        private Int32 HomeHit(Int32 x, Int32 y)
        {
            if (HomeContains(HomeButtonRect(-1),x,y)) return -1;
            for (Int32 i=0;i<HomeTitles.Length;i++) if(HomeContains(HomeButtonRect(i),x,y)) return i;
            return -2;
        }
        private Boolean HandleStyledHomeMessage(IntPtr hwnd, UInt32 msg, IntPtr wParam, IntPtr lParam)
        {
            if (_page != Page.Home)
            {
                if (msg == 0x0113 && wParam.ToInt64() == HomeTimer) { KillTimer(hwnd,new UIntPtr(HomeTimer)); _homeHover=-2; return true; }
                return false;
            }
            if(msg==0x0014) return true; // The buffered paint owns the background.
            if(msg==0x000F) { PaintStyledHome(hwnd); return true; }
            if(msg==0x0200) {
                Int32 value=lParam.ToInt32();
                Int32 hit=HomeHit((Int16)(value & 65535),(Int16)(value>>16));
                if(hit!=_homeHover) { _homeHover=hit; InvalidateRect(hwnd,IntPtr.Zero,false); }
                if(hit!=-2) SetTimer(hwnd,new UIntPtr(HomeTimer),100,IntPtr.Zero);
                return true;
            }
            if(msg==0x0201 || msg==0x0203) { // select on down; open only on double-click
                Int32 value=lParam.ToInt32();
                Int32 hit=HomeHit((Int16)(value & 65535),(Int16)(value>>16));
                if(hit==-2) return true;
                _homeLiveSelected=hit==-1;
                if(hit>=0) _homeSelection=hit;
                _homeHover=hit; 
                RefreshHomeDescription();
                SetFocus(hwnd);
                if(msg==0x0203) {
                    _homeHover=-2; KillTimer(hwnd,new UIntPtr(HomeTimer));
                    if(hit==-1) RenderLiveLog(); else ActivateHomeSelection();
                }
                return true;
            }
            if(msg==0x0113 && wParam.ToInt64()==HomeTimer) {
                POINT point; GetCursorPos(out point); ScreenToClient(hwnd,ref point);
                if(HomeHit(point.x,point.y)!=_homeHover) {
                    _homeHover=-2; KillTimer(hwnd,new UIntPtr(HomeTimer));
                    InvalidateRect(hwnd,IntPtr.Zero,false);
                }
                return true;
            }
            return false;
        }
        private void HomeFill(IntPtr dc,RECT rect,UInt32 color)
        {
            IntPtr brush=CreateSolidBrush(color);
            try { FillRect(dc,ref rect,brush); } finally { DeleteObject(brush); }
        }
        private void HomeBox(IntPtr dc,RECT rect,UInt32 fill,UInt32 border)
        {
            IntPtr brush=CreateSolidBrush(fill),pen=CreatePen(0,1,border);
            IntPtr oldBrush=SelectObject(dc,brush),oldPen=SelectObject(dc,pen);
            try { RoundRect(dc,rect.Left,rect.Top,rect.Right,rect.Bottom,10,10); }
            finally { SelectObject(dc,oldBrush);SelectObject(dc,oldPen);DeleteObject(brush);DeleteObject(pen); }
        }
        private void DrawHomeButton(IntPtr dc, RECT rect, Boolean selected, Boolean hovered)
        {
            UInt32 top = selected ? 0x00434343u : hovered ? 0x00484848u : 0x00393939u;
            UInt32 bottom = selected ? 0x00292929u : hovered ? 0x002E2E2Eu : 0x00232323u;
            UInt32 edge = selected ? SelectionColor : 0x00595959u;
            if (!_gdiplusReady || GdipCreateFromHDC(dc, out IntPtr graphics) != 0)
            {
                HomeBox(dc, rect, bottom, edge);
                return;
            }

            IntPtr path = IntPtr.Zero, brush = IntPtr.Zero, pen = IntPtr.Zero;
            try
            {
                Int32 width = rect.Right - rect.Left - 1, height = rect.Bottom - rect.Top - 1;
                if (width < 2 || height < 2) return;
                Int32 corner = Math.Min(Math.Min(width, height), Math.Max(4, (Int32)(12 * _homeFontScale)));
                var bounds = new GdiPlusRect { X = rect.Left, Y = rect.Top, Width = width, Height = height };
                if (GdipCreatePath(0, out path) != 0 ||
                    GdipCreateLineBrushFromRectI(ref bounds, HomeArgb(top, selected ? 0xD0u : hovered ? 0xB8u : 0x96u), HomeArgb(bottom, 0x76u), 1, 0, out brush) != 0 ||
                    GdipCreatePen1(HomeArgb(edge), 1f, 2, out pen) != 0)
                {
                    HomeBox(dc, rect, bottom, edge);
                    return;
                }

                GdipSetSmoothingMode(graphics, 4);
                GdipAddPathArcI(path, rect.Left, rect.Top, corner, corner, 180, 90);
                GdipAddPathArcI(path, rect.Left + width - corner, rect.Top, corner, corner, 270, 90);
                GdipAddPathArcI(path, rect.Left + width - corner, rect.Top + height - corner, corner, corner, 0, 90);
                GdipAddPathArcI(path, rect.Left, rect.Top + height - corner, corner, corner, 90, 90);
                GdipClosePathFigure(path);
                GdipFillPath(graphics, brush, path);
                GdipDrawPath(graphics, pen, path);
            }
            finally
            {
                if (pen != IntPtr.Zero) GdipDeletePen(pen);
                if (brush != IntPtr.Zero) GdipDeleteBrush(brush);
                if (path != IntPtr.Zero) GdipDeletePath(path);
                GdipDeleteGraphics(graphics);
            }
        }

        private static UInt32 HomeArgb(UInt32 color, UInt32 alpha = 0xFFu)
        {
            return (alpha << 24) | ((color & 0xFFu) << 16) | (color & 0xFF00u) | ((color >> 16) & 0xFFu);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GdiPlusRect { public Int32 X, Y, Width, Height; }

        [DllImport("gdiplus.dll")] private static extern Int32 GdipCreatePath(Int32 fillMode, out IntPtr path);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipAddPathArcI(IntPtr path, Int32 x, Int32 y, Int32 width, Int32 height, Single start, Single sweep);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipClosePathFigure(IntPtr path);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipSetSmoothingMode(IntPtr graphics, Int32 mode);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipCreateLineBrushFromRectI(ref GdiPlusRect rect, UInt32 top, UInt32 bottom, Int32 mode, Int32 wrapMode, out IntPtr brush);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipCreatePen1(UInt32 color, Single width, Int32 unit, out IntPtr pen);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipDrawPath(IntPtr graphics, IntPtr pen, IntPtr path);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipDeletePen(IntPtr pen);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipDeleteBrush(IntPtr brush);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipDeletePath(IntPtr path);

        private void HomeText(IntPtr dc,String text,RECT rect,UInt32 color,IntPtr font,UInt32 flags)
        {
            IntPtr old=SelectObject(dc,font);
            SetTextColor(dc,(Int32)color);SetBkMode(dc,1);
            DrawText(dc,text,-1,ref rect,flags|0x800 /* no prefix */);
            SelectObject(dc,old);
        }
        private void PaintStyledHome(IntPtr hwnd)
        {
            HOMEPAINT paint;
            IntPtr dc=BeginPaint(hwnd,out paint);
            IntPtr buffer=IntPtr.Zero,bitmap=IntPtr.Zero,previous=IntPtr.Zero;
            try {
                RECT client; GetClientRect(hwnd,out client);
                if(client.Right<=0 || client.Bottom<=0) return;
                buffer=CreateCompatibleDC(dc);
                bitmap=CreateCompatibleBitmap(dc,client.Right,client.Bottom);
                if(buffer==IntPtr.Zero || bitmap==IntPtr.Zero) return;
                previous=SelectObject(buffer,bitmap);
                DrawStyledHome(buffer);
                // The RichEdit child paints its own text: never overwrite it.
                RECT description=HomeRect(38,740,1204,161);
                ExcludeClipRect(dc,description.Left,description.Top,description.Right,description.Bottom);
                BitBlt(dc,0,0,client.Right,client.Bottom,buffer,0,0,0x00CC0020);
            }
            finally {
                if(previous!=IntPtr.Zero) SelectObject(buffer,previous);
                if(bitmap!=IntPtr.Zero) DeleteObject(bitmap);
                if(buffer!=IntPtr.Zero) DeleteDC(buffer);
                EndPaint(hwnd,ref paint);
            }
        }
        private void DrawStyledHome(IntPtr dc)
        {
            IntPtr hwnd = _windowHandle;
            IntPtr font=IntPtr.Zero,heading=IntPtr.Zero,ascii=IntPtr.Zero;
            try {
                RECT client;GetClientRect(hwnd,out client);
                HomeFill(dc,client,0);
                double scale=_homeFontScale;
                Int32 size=Math.Max(11,(Int32)(19*scale)-4)+1;
                font=CreateFont(-size,0,0,0,400,0,0,0,1,0,0,0,49,"Consolas");
                heading=CreateFont(-size,0,0,0,700,0,0,0,1,0,0,0,49,"Consolas");
                ascii=CreateFont(-(Math.Max(10,(Int32)(21*scale)-4)+1),0,0,0,400,0,0,0,1,0,0,0,49,"Consolas");
                UInt32 cyan=SelectionColor; const UInt32 white=0x00F2F2F2;
                HomeFill(dc,HomeRect(24,30,1232,2),cyan);
                String logo=" __  __ _____ __  __  ___  ____  ___    _\r\n"+
                    "|  \\/  | ____|  \\/  |/ _ \\|  _ \\|_ _|  / \\ \r\n"+
                    "| |\\/| |  _| | |\\/| | | | | |_) || |  / _ \\ \r\n"+
                    "| |  | | |___| |  | | |_| |  _ < | | / ___ \\ \r\n"+
                    "|_|  |_|_____|_|  |_|\\___/|_| \\_\\___/_/   \\_\\";
                HomeText(dc,logo,HomeRect(30,36,850,142),cyan,ascii,0);
                HomeText(dc,"D E V  C O N S O L E",HomeRect(285,174,400,28),cyan,font,0);
                HomeText(dc,"by Cheddarbung",HomeRect(765,174,220,28),cyan,font,0);
                HomeText(dc,"v1.4.1",HomeRect(1160,174,86,26),white,font,0);
                if(!_homeCrystalAttempted && _gdiplusReady) {
                    _homeCrystalAttempted=true;
                    String path=Path.Combine(Environment.CurrentDirectory,@"Memoria Dev Console\FF9_Data\UI\MdcHeaderCrystal.png");
                    if(File.Exists(path)) GdipLoadImageFromFile(path,out _homeCrystal);
                }
                if(_homeCrystal!=IntPtr.Zero) {
                    IntPtr graphics;
                    if(GdipCreateFromHDC(dc,out graphics)==0) {
                        try { RECT r=HomeRect(1050,56,96,144);GdipDrawImageRectI(graphics,_homeCrystal,r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top); }
                        finally { GdipDeleteGraphics(graphics); }
                    }
                }
                HomeFill(dc,HomeRect(24,204,1232,2),cyan);
                HomeText(dc,"GAME PAUSED",HomeRect(1000,218,220,30),white,heading,0x8022);
                DrawHomeButton(dc,HomeButtonRect(-1),_homeLiveSelected,_homeHover==-1);
                HomeText(dc,"[L] Live Log",HomeRect(48,216,540,36),cyan,heading,0x8024);
                String[] groups={"SESSION","VIEWERS","ADVANCED TOOLS","ASSETS & AUDIO","EXPERIMENTAL","SETTINGS"};
                Int32[] gx={30,665,30,665,30,665},gy={266,266,476,476,632,632};
                for(Int32 i=0;i<groups.Length;i++) {
                    HomeText(dc,groups[i],HomeRect(gx[i],gy[i],360,28),cyan,heading,0);
                    HomeFill(dc,HomeRect(gx[i],gy[i]+29,580,1),ConsoleTheme.Background);
                }
                for(Int32 i=0;i<HomeTitles.Length;i++) {
                    Boolean selected=!_homeLiveSelected && i==_homeSelection,hover=i==_homeHover;
                    RECT r=HomeButtonRect(i);
                    DrawHomeButton(dc,r,selected,hover);
                    RECT key=r;key.Left+=12;key.Right=key.Left+(Int32)(65*scale);
                    HomeText(dc,(selected?"> ":"  ")+"["+HomeKeys[i]+"]",key,cyan,heading,0x24);
                    RECT title=r;title.Left=key.Right+5;title.Right-=10;
                    HomeText(dc,HomeTitles[i],title,white,font,0x8024);
                }
                // Description uses the existing RichEdit renderer to retain colored spans
                // and scrolling for long setup instructions.
                HomeBox(dc,HomeRect(24,708,1232,200),0x00101010,0x00737373);
                HomeText(dc,(HomeDescriptionIndex == -1 ? "LIVE LOG" : HomeTitles[HomeDescriptionIndex].ToUpperInvariant()),HomeRect(38,713,1190,24),cyan,heading,0x8020);
                HomeFill(dc,HomeRect(24,916,1232,1),cyan);
                HomeText(dc,"Click: select   Double-click / Enter: open   Arrows: navigate   Esc: close",
                    HomeRect(30,928,1220,28),0x00C0B4AA,font,0x8020);
            }
            finally {
                if(font!=IntPtr.Zero)DeleteObject(font);if(heading!=IntPtr.Zero)DeleteObject(heading);if(ascii!=IntPtr.Zero)DeleteObject(ascii);

            }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct HOMEPAINT {
            public IntPtr dc; public Int32 erase; public RECT rect;public Int32 restore,update;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public Byte[] reserved;
        }
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd,out HOMEPAINT paint);
        [DllImport("user32.dll")] private static extern Boolean EndPaint(IntPtr hwnd,ref HOMEPAINT paint);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern Int32 DrawText(IntPtr dc,String text,Int32 count,ref RECT rect,UInt32 flags);
        [DllImport("user32.dll")] private static extern Int32 FillRect(IntPtr dc,ref RECT rect,IntPtr brush);
        [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hwnd,UIntPtr id,UInt32 delay,IntPtr callback);
        [DllImport("user32.dll")] private static extern Boolean KillTimer(IntPtr hwnd,UIntPtr id);
        [DllImport("user32.dll")] private static extern Boolean GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern Boolean ScreenToClient(IntPtr hwnd,ref POINT point);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,Int32 width,Int32 height);
        [DllImport("gdi32.dll")] private static extern Boolean DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern Int32 ExcludeClipRect(IntPtr dc,Int32 left,Int32 top,Int32 right,Int32 bottom);
        [DllImport("gdi32.dll")] private static extern Boolean BitBlt(IntPtr dest,Int32 x,Int32 y,Int32 width,Int32 height,IntPtr src,Int32 sx,Int32 sy,UInt32 op);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(UInt32 color);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(Int32 style,Int32 width,UInt32 color);

        [DllImport("gdi32.dll")] private static extern Boolean RoundRect(IntPtr dc,Int32 l,Int32 t,Int32 r,Int32 b,Int32 ew,Int32 eh);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipCreateFromHDC(IntPtr dc,out IntPtr graphics);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipDrawImageRectI(IntPtr graphics,IntPtr image,Int32 x,Int32 y,Int32 w,Int32 h);
        [DllImport("gdiplus.dll")] private static extern Int32 GdipDeleteGraphics(IntPtr graphics);
    }
}

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private sealed class StyledBlock
        {
            internal String Text;
            internal Boolean Row, Selected, Entrance;
            internal Int32 Ordinal, Top, Height;
            internal UInt32 Color;
        }
        private readonly List<StyledBlock> _documentBlocks = new List<StyledBlock>();
        private Boolean _styledDocumentActive, _documentReveal;
        private Page _documentPage;
        private String _documentSource, _documentSelectedKey;
        private Int32 _documentScroll, _documentHeight, _documentViewport;
        private static String EntranceMenuLine(Boolean selected,String text)
        { return MenuLine(selected,text).Replace("MDC_ITEM","MDC_ENTRANCE"); }

        private static String PlainStyledText(String text)
        {
            return text.Replace(YellowOpen,"").Replace(YellowClose,"").Replace(CyanOpen,"").Replace(CyanClose,"")
                .Replace(StatusOpen,"").Replace(StatusClose,"").Replace("<MDC_ITEM>","").Replace("</MDC_ITEM>","")
                .Replace("<MDC_ENTRANCE>","").Replace("</MDC_ENTRANCE>","");
        }
        private static List<StyledBlock> ParseStyledDocument(String source)
        {
            var blocks=new List<StyledBlock>();
            Int32 row=0,entrance=0;
            foreach(String raw in source.Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
                String plain=PlainStyledText(raw).Trim();
                if(plain.Length>0 && (plain.Trim('=','-').Length==0)) continue;
                Boolean item=raw.Contains("<MDC_ITEM>"),entry=raw.Contains("<MDC_ENTRANCE>");
                Boolean selected=(item||entry) && plain.StartsWith(">>",StringComparison.Ordinal);
                if(selected) plain=plain.Substring(2).TrimStart();
                UInt32 color=raw.Contains(CyanOpen)?(plain==plain.ToUpperInvariant()?SelectionColor:DescriptionHighlightColor):LinePalette(plain);
                if(plain.Length>0 && plain==plain.ToUpperInvariant() && System.Text.RegularExpressions.Regex.IsMatch(plain,"[A-Z]"))
                    color=SelectionColor;
                blocks.Add(new StyledBlock { Text=plain,Row=item||entry,Entrance=entry,Selected=selected,
                    Ordinal=entry?entrance++:item?row++:-1,Color=item||entry?PrimaryColor:color });
            }
            return blocks;
        }
        private void SetStyledDocument(String source)
        {
            Boolean changedPage=!_styledDocumentActive || _documentPage!=_page;
            _styledDocumentActive=true;
            if(!changedPage && source==_documentSource)return;
            _documentPage=_page;_documentSource=source;
            _documentBlocks.Clear();_documentBlocks.AddRange(ParseStyledDocument(source));
            if(IsTextEntryActive) _documentBlocks.RemoveAll(b => !b.Row &&
                (b.Text.StartsWith("EDIT ",StringComparison.Ordinal) || b.Text.StartsWith("Type numeric ID:",StringComparison.Ordinal) ||
                 b.Text.StartsWith("Type PNG number:",StringComparison.Ordinal) || b.Text.StartsWith("TITLE:",StringComparison.Ordinal) ||
                 b.Text.StartsWith("Enter=apply",StringComparison.Ordinal)));
            String key="";
            foreach(var block in _documentBlocks) if(block.Row && block.Selected && !block.Entrance)key+=block.Ordinal+":"+block.Text;
            _documentReveal=changedPage || key!=_documentSelectedKey || IsTextEntryActive || (_page==Page.WorldEntrances && worldConfirm!=null);
            _documentSelectedKey=key;
            if(changedPage) {
                _documentScroll=0;

            }
            _inputCaretVisible=true;
            if(IsTextEntryActive) SetTimer(_windowHandle,new UIntPtr(InputCaretTimer),500,IntPtr.Zero);
            else KillTimer(_windowHandle,new UIntPtr(InputCaretTimer));
            FitStyledDocument();
            ShowWindow(_textHandle,SW_HIDE);
            LayoutControls();
            InvalidateRect(_windowHandle,IntPtr.Zero,false);
            SetFocus(_windowHandle);
        }
        private const UInt32 InputCaretTimer=0x4D49;
        private Boolean _inputCaretVisible=true;
        private String InputBubbleTitle()
        {
            if(_page==Page.WorldEntrances)return "EDIT: "+(worldEditing??"Value").Replace("set:","");
            if(_page==Page.BattleSceneEditor)return "EDIT: "+BattleLabel(battleEditing);
            if(_page==Page.FieldState)return "ENTER SAVE TITLE";
            if(_page==Page.Warp)return _warpEntryStage==0?"ENTER FIELD ID":"ENTER SCENARIO COUNTER";
            if(_page==Page.SoundBrowser)return _soundMode==SoundBrowserMode.AliasEntry?"ENTER SOUND ALIAS":"ENTER SEARCH TEXT";
                        if(_compositePngSelectionActive)return "PNG NUMBER";
            if(_assetIdEntryIsField)return _assetSubMenu==AssetDumpSubMenu.FieldBackgrounds && _assetSubSelection==3 ? "TEMPLATE FIELD ID" : "FIELD ID";
            return "MODEL ID";
        }
        private void DrawInputBubble(IntPtr dc,RECT client,IntPtr font)
        {
            Int32 bottom=client.Bottom-(_thumbnailVisible?PreviewStripHeight+12:48);
            RECT box=new RECT{Left=24,Top=bottom-142,Right=client.Right-36,Bottom=bottom-6};
            HomeBox(dc,box,0x00181818,SelectionColor);
            RECT header=new RECT{Left=box.Left+2,Top=box.Top+2,Right=box.Right-2,Bottom=box.Top+34};
            HomeFill(dc,header,ConsoleTheme.Background);
            header.Left+=12;
            HomeText(dc,InputBubbleTitle(),header,SelectionColor,font,0x8024);
            Int32 limit;Boolean numeric;
            String value=_page==Page.BattleSceneEditor?battleEntry:CurrentEntry(out limit,out numeric);
            value=value??"";
            IntPtr old=SelectObject(dc,font); SIZE cell;
            GetTextExtentPoint32(dc,"M",1,out cell); SelectObject(dc,old);
            Int32 capacity=Math.Max(8,(box.Right-box.Left-36)/Math.Max(1,cell.Width)-1);
            if(value.Length>capacity)value="..."+value.Substring(value.Length-capacity+3);
            RECT input=new RECT{Left=box.Left+14,Top=box.Top+44,Right=box.Right-14,Bottom=box.Top+87};
            HomeBox(dc,input,0x00080808,0x006E6E6E);
            input.Left+=10;input.Top+=8;
            HomeText(dc,value+(_inputCaretVisible?"|":" "),input,PrimaryColor,font,0x8020);
            HomeText(dc,(_page==Page.AssetDumper && _assetSubMenu==AssetDumpSubMenu.FieldBackgrounds && _assetSubSelection==3 ? (_compositePngSelectionActive?"Enter: recut":"Enter: continue") : "Enter: apply")+"   Ctrl+A: clear   Esc: cancel",new RECT{Left=box.Left+14,Top=box.Top+101,Right=box.Right-10,Bottom=box.Bottom-6},SecondaryColor,font,0x8020);
        }
        private Int32 StyledBlockHeight(StyledBlock block, Int32 measured)
        {
            Boolean compact = (_page == Page.BattleSceneEditor || _page == Page.WorldEntrances || _page == Page.FieldState || _page == Page.AssetDumper || _page == Page.LiveLogFilters || _page == Page.Warp || _page == Page.ConsoleColors);
            return block.Row ? Math.Max(compact?26:42,measured+(compact?6:18)) : block.Text.Length==0 ? (compact?6:12) : measured+(compact?4:9);
        }
        private Int32 StyledRowGap { get { return (_page==Page.BattleSceneEditor || _page==Page.WorldEntrances || _page==Page.FieldState || _page==Page.AssetDumper || _page==Page.LiveLogFilters || _page==Page.Warp || _page==Page.ConsoleColors) ? 3 : 6; } }
        private void FitStyledDocument()
        {
            if (_fittingContent || _windowHandle == IntPtr.Zero || IsZoomed(_windowHandle) || IsIconic(_windowHandle)) return;
            RECT client, bounds;
            GetClientRect(_windowHandle, out client); GetWindowRect(_windowHandle, out bounds);
            MONITORINFO monitor = new MONITORINFO(); monitor.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(MonitorFromWindow(_windowHandle, 2), ref monitor)) return;
            Int32 chromeWidth = bounds.Right-bounds.Left-client.Right;
            Int32 chromeHeight = bounds.Bottom-bounds.Top-client.Bottom;
            Int32 width = Math.Min((_page==Page.FieldState || _page==Page.AssetDumper || _page==Page.WorldEntrances || _page==Page.BattleSceneEditor || _page==Page.LiveLogFilters || _page==Page.Warp || _page==Page.ConsoleColors) ? 720 : 800, monitor.Work.Right-monitor.Work.Left-chromeWidth);
            Int32 contentHeight = 0;
            IntPtr compactFont = (_page==Page.BattleSceneEditor || _page==Page.WorldEntrances || _page==Page.FieldState || _page==Page.AssetDumper || _page==Page.LiveLogFilters || _page==Page.Warp || _page==Page.ConsoleColors) ? CreateFont(-17,0,0,0,400,0,0,0,1,0,0,0,49,"Consolas") : IntPtr.Zero;
            IntPtr dc = GetDC(_windowHandle), old = SelectObject(dc,compactFont!=IntPtr.Zero?compactFont:_fontHandle);
            try {
                foreach (var block in _documentBlocks) {
                    RECT measured = new RECT { Right = Math.Max(80,width-100) };
                    DrawText(dc,block.Text.Length==0 ? " " : block.Text,-1,ref measured,0x410|0x800);
                    contentHeight += StyledBlockHeight(block,measured.Bottom)+(block.Row?StyledRowGap:0);
                }
            } finally { SelectObject(dc,old); ReleaseDC(_windowHandle,dc); if(compactFont!=IntPtr.Zero)DeleteObject(compactFont); }
            Int32 wanted = Math.Max(520,76+contentHeight+(IsTextEntryActive?150:0)+(_thumbnailVisible ? PreviewStripHeight+12 : 48));
            Int32 height = Math.Min(wanted+chromeHeight,monitor.Work.Bottom-monitor.Work.Top);
            Int32 left = Math.Max(monitor.Work.Left,Math.Min(bounds.Left,monitor.Work.Right-width-chromeWidth));
            Int32 top = Math.Max(monitor.Work.Top,Math.Min(bounds.Top,monitor.Work.Bottom-height));
            if (bounds.Right-bounds.Left==width+chromeWidth && bounds.Bottom-bounds.Top==height && bounds.Top==top && bounds.Left==left) return;
            _fittingContent=true;
            try { MoveWindow(_windowHandle,left,top,width+chromeWidth,height,true); }
            finally { _fittingContent=false; }
        }
        private String StyledPageTitle()
        {
            switch(_page) {
                case Page.LiveLog:return "LIVE LOG";
                case Page.LiveLogFilters:return "LIVE LOG FILTERS";
                case Page.CompileRestart:return "COMPILE + RESTART";
                case Page.FieldState:return "SAVE STATES";
                case Page.WorldEntrances:return "WORLD ENTRANCE CREATOR";
                case Page.BattleSceneEditor:return "BATTLE SCENE EDITOR";
                case Page.AssetDumper:return _assetSubMenu==AssetDumpSubMenu.FieldBackgrounds?"FIELD BACKGROUND TOOLKIT":"ASSET DUMPER";
                case Page.ConsoleColors:return "CONSOLE COLOR";
                case Page.Warp:return "WARP / STORY JUMP";
                case Page.SoundBrowser:return "SOUND BROWSER";
                default:return "MEMORIA DEV CONSOLE";
            }
        }
        private RECT StyledToolbarButton(Int32 index,Int32 width)
        { return new RECT { Left=width-242+index*114,Top=18,Right=width-136+index*114,Bottom=55 }; }
        private Boolean StyledFrameActive { get { return _page!=Page.Home && (_styledDocumentActive && _documentPage==_page || _page==Page.LiveLog || _page==Page.CompileRestart); } }
        private Boolean HandleStyledDocumentMessage(IntPtr hwnd,UInt32 msg,IntPtr wParam,IntPtr lParam)
        {
            if(msg==0x0113 && wParam.ToInt64()==InputCaretTimer) {
                if(!IsTextEntryActive || !_styledDocumentActive) { KillTimer(hwnd,new UIntPtr(InputCaretTimer)); return true; }
                _inputCaretVisible=!_inputCaretVisible; InvalidateRect(hwnd,IntPtr.Zero,false); return true;
            }
            if(!StyledFrameActive)return false;
            if(msg==0x0014)return true;
            if(msg==0x000F) { PaintStyledDocument(hwnd);return true; }
            if(msg==WM_SIZE) { LayoutControls();InvalidateRect(hwnd,IntPtr.Zero,false);return true; }
            if(msg==0x020A && _styledDocumentActive) {
                Int32 delta=(Int16)((wParam.ToInt64()>>16)&65535);
                _documentScroll=Math.Max(0,Math.Min(Math.Max(0,_documentHeight-_documentViewport),_documentScroll-delta/120*100));
                InvalidateRect(hwnd,IntPtr.Zero,false);return true;
            }
            if(msg!=0x0201 && msg!=0x0203)return false;
            Int32 packed=lParam.ToInt32(),x=(Int16)(packed&65535),y=(Int16)(packed>>16);
            RECT client;GetClientRect(hwnd,out client);
            if(HomeContains(StyledToolbarButton(0,client.Right),x,y)) { if(msg==0x0201)HandleNavigationKey(VK_ESCAPE);return true; }
            if(HomeContains(StyledToolbarButton(1,client.Right),x,y)) { if(msg==0x0201)HandleNavigationKey(_page==Page.LiveLog?VK_F:_page==Page.CompileRestart?VK_R:(_page==Page.WorldEntrances && worldConfirm!=null)?0x59:VK_RETURN);return true; }
            if(!_styledDocumentActive || IsTextEntryActive || (_page==Page.WorldEntrances && worldConfirm!=null))return false;
            if(y<76 || y>76+_documentViewport || x<24 || x>client.Right-24)return false;
            foreach(var block in _documentBlocks) {
                if(block.Row && y>=76+block.Top-_documentScroll && y<76+block.Top+block.Height-_documentScroll) {
                    SelectStyledRow(block,msg==0x0203);return true;
                }
            }
            SetFocus(hwnd);return true;
        }
        private void SelectStyledRow(StyledBlock block,Boolean activate)
        {
            Int32 selected=0;
            foreach(var row in _documentBlocks) if(row.Row && row.Selected && row.Entrance==block.Entrance)selected=row.Ordinal;
            if(_page==Page.WorldEntrances) {
                if(block.Entrance) {
                    var view=Memoria.DevConsole.EntranceJson.JObject.Parse(worldViewJson);
                    Int32 current=(Int32?)view["selection"]??0;
                    WorldAction("selectIndex",(current+block.Ordinal-selected).ToString());return;
                }
                if(worldChoosingTarget)worldTargetIndex=block.Ordinal;
                else worldRow=block.Ordinal;
                RenderWorldEntrances();
                if(activate)HandleWorldKey(VK_RETURN);
            }
            else if(_page==Page.BattleSceneEditor) { battleRow=block.Ordinal;RenderBattleScene();if(activate)HandleBattleKey(VK_RETURN); }
            else if(_page==Page.LiveLogFilters) {logFilterRow=block.Ordinal;RenderLogFilters();if(activate)HandleLogFilterKey(VK_RETURN);}
            else { MoveSelection(block.Ordinal-selected);if(activate)ActivateSelection(); }
            SetFocus(_windowHandle);
        }
        private void PaintStyledDocument(IntPtr hwnd)
        {
            HOMEPAINT paint;IntPtr dc=BeginPaint(hwnd,out paint),buffer=IntPtr.Zero,bitmap=IntPtr.Zero,previous=IntPtr.Zero;
            try {
                RECT client;GetClientRect(hwnd,out client);
                if(client.Right<=0||client.Bottom<=0)return;
                buffer=CreateCompatibleDC(dc);bitmap=CreateCompatibleBitmap(dc,client.Right,client.Bottom);
                if(buffer==IntPtr.Zero||bitmap==IntPtr.Zero)return;
                previous=SelectObject(buffer,bitmap);DrawStyledDocument(buffer,client);
                if(!_styledDocumentActive)ExcludeClipRect(dc,24,72,client.Right-24,client.Bottom-56);
                if(_thumbnailVisible)ExcludeClipRect(dc,0,client.Bottom-PreviewStripHeight,client.Right,client.Bottom);
                BitBlt(dc,0,0,client.Right,client.Bottom,buffer,0,0,0x00CC0020);
            }
            finally {
                if(previous!=IntPtr.Zero)SelectObject(buffer,previous);
                if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(buffer!=IntPtr.Zero)DeleteDC(buffer);
                EndPaint(hwnd,ref paint);
            }
        }
        private void DrawStyledDocument(IntPtr dc,RECT client)
        {
            HomeFill(dc,client,0);
            IntPtr compactFont=(_page==Page.BattleSceneEditor || _page==Page.WorldEntrances || _page==Page.FieldState || _page==Page.AssetDumper || _page==Page.LiveLogFilters || _page==Page.Warp || _page==Page.ConsoleColors)?CreateFont(-17,0,0,0,400,0,0,0,1,0,0,0,49,"Consolas"):IntPtr.Zero;
            IntPtr bodyFont=compactFont!=IntPtr.Zero?compactFont:_fontHandle;
            IntPtr titleFont=CreateFont(-23,0,0,0,700,0,0,0,1,0,0,0,49,"Consolas");
            try {
                HomeText(dc,StyledPageTitle(),new RECT{Left=24,Top=22,Right=client.Right-250,Bottom=58},SelectionColor,titleFont,0x8020);
                for(Int32 i=0;i<2;i++) {
                    RECT button=StyledToolbarButton(i,client.Right);
                    HomeBox(dc,button,0x001B1B1B,0x006E6E6E);
                    HomeText(dc,i==0?"Back":_page==Page.LiveLog?"Filters":_page==Page.CompileRestart?"Retry":(_page==Page.WorldEntrances && worldConfirm!=null)?"Confirm":_page==Page.FieldState?(_fieldSelection>=4?"Open":"Run"):"Enter",button,SelectionColor,_fontHandle,0x25);
                }

                HomeFill(dc,new RECT{Left=24,Top=65,Right=client.Right-24,Bottom=66},SelectionColor);
                Int32 bottom=client.Bottom-(_thumbnailVisible?PreviewStripHeight+12:48);
                if(IsTextEntryActive)bottom-=150;
                _documentViewport=Math.Max(60,bottom-76);
                if(_styledDocumentActive) {
                    Int32 position=0,selectedTop=-1,selectedBottom=0;
                    foreach(var block in _documentBlocks) {
                        RECT measured=new RECT{Right=Math.Max(80,client.Right-100)};
                        IntPtr old=SelectObject(dc,bodyFont);
                        DrawText(dc,block.Text.Length==0?" ":block.Text,-1,ref measured,0x410|0x800);
                        SelectObject(dc,old);
                        block.Top=position;block.Height=StyledBlockHeight(block,measured.Bottom);
                        if(block.Selected && !block.Entrance){selectedTop=position;selectedBottom=position+block.Height;}
                        position+=block.Height+(block.Row?StyledRowGap:0);
                    }
                    _documentHeight=position;
                    if(_documentReveal) {
                        if(IsTextEntryActive || (_page==Page.WorldEntrances && worldConfirm!=null))_documentScroll=Math.Max(0,position-_documentViewport);
                        else if(selectedTop>=0) {
                            if(selectedTop<_documentScroll)_documentScroll=selectedTop;
                            else if(selectedBottom>_documentScroll+_documentViewport)_documentScroll=selectedBottom-_documentViewport;
                        }
                        _documentReveal=false;
                    }
                    _documentScroll=Math.Max(0,Math.Min(Math.Max(0,position-_documentViewport),_documentScroll));
                    Int32 saved=SaveDC(dc);IntersectClipRect(dc,24,76,client.Right-24,bottom);
                    try {
                        foreach(var block in _documentBlocks) {
                            RECT row=new RECT{Left=24,Top=76+block.Top-_documentScroll,Right=client.Right-36,Bottom=76+block.Top+block.Height-_documentScroll};
                            if(row.Bottom<76||row.Top>bottom)continue;
                            if(block.Row) {
                                HomeBox(dc,row,block.Selected?ConsoleTheme.Background:0x001B1B1Bu,block.Selected?SelectionColor:0x006E6E6Eu);
                                row.Left+=14;row.Right-=14;row.Top+=(_page==Page.BattleSceneEditor || _page==Page.WorldEntrances || _page==Page.FieldState || _page==Page.AssetDumper || _page==Page.LiveLogFilters || _page==Page.Warp || _page==Page.ConsoleColors)?3:8;row.Bottom-=(_page==Page.BattleSceneEditor || _page==Page.WorldEntrances || _page==Page.FieldState || _page==Page.AssetDumper || _page==Page.LiveLogFilters || _page==Page.Warp || _page==Page.ConsoleColors)?3:6;
                            }
                            HomeText(dc,(block.Row && block.Selected?">> ":"")+block.Text,row,block.Row?PrimaryColor:block.Color,bodyFont,0x10);
                        }
                    } finally {RestoreDC(dc,saved);}
                    if(position>_documentViewport) {
                        RECT rail=new RECT{Left=client.Right-29,Top=76,Right=client.Right-25,Bottom=bottom};
                        HomeFill(dc,rail,0x00252525);
                        Int32 thumb=Math.Max(24,_documentViewport*_documentViewport/position);
                        rail.Top+=(_documentViewport-thumb)*_documentScroll/Math.Max(1,position-_documentViewport);
                        rail.Bottom=rail.Top+thumb;HomeFill(dc,rail,SelectionColor);
                    }
                }
                if(IsTextEntryActive && _styledDocumentActive)DrawInputBubble(dc,client,bodyFont);
                if(!_thumbnailVisible) {
                    HomeFill(dc,new RECT{Left=24,Top=client.Bottom-40,Right=client.Right-24,Bottom=client.Bottom-39},SelectionColor);
                    HomeText(dc,IsTextEntryActive?"Type a value above. Backspace: edit   Esc: cancel":_styledDocumentActive?"Click: select   Double-click: open   Wheel: scroll   Esc: back":"F10: toggle   Esc: back",
                        new RECT{Left=24,Top=client.Bottom-32,Right=client.Right-24,Bottom=client.Bottom-4},SecondaryColor,bodyFont,0x8020);
                }
            } finally {DeleteObject(titleFont);if(compactFont!=IntPtr.Zero)DeleteObject(compactFont);}
        }
        [DllImport("gdi32.dll")]private static extern Int32 SaveDC(IntPtr dc);
        [DllImport("gdi32.dll")]private static extern Boolean RestoreDC(IntPtr dc,Int32 saved);
        [DllImport("gdi32.dll")]private static extern Int32 IntersectClipRect(IntPtr dc,Int32 left,Int32 top,Int32 right,Int32 bottom);
    }
}
