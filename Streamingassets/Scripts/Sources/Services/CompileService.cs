using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Memoria.DevConsole
{
    public sealed class CompileService : IDisposable
    {
        private const Int32 STD_INPUT_HANDLE = -10;
        private const Int32 STD_OUTPUT_HANDLE = -11;

        private const UInt16 KEY_EVENT = 0x0001;
        private const UInt16 VK_RETURN = 0x0D;

        private const Int32 SW_HIDE = 0;

        private readonly Action<String> _output;
        private readonly Action<Boolean> _completed;

        private Thread _thread;
        private volatile Boolean _running;
        private volatile Boolean _disposed;
        private volatile Boolean _awaitingRetry;
        private volatile Boolean _retryRequested;

        public Boolean IsRunning
        {
            get { return _running; }
        }

        public CompileService(Action<String> output, Action<Boolean> completed)
        {
            _output = output;
            _completed = completed;
        }

        public Boolean Start()
        {
            if (_disposed)
                return false;

            // If Memoria.Compiler is still alive at its own
            // "Press R to retry..." prompt, reuse that exact process.
            // This is much safer than killing it and trying to attach to a
            // second console process after every source error.
            if (_running)
            {
                if (_awaitingRetry)
                {
                    _retryRequested = true;
                    return true;
                }

                return false;
            }

            _retryRequested = false;
            _awaitingRetry = false;
            _running = true;

            _thread = new Thread(CompileThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "Memoria Dev Console Compiler";
            _thread.Start();

            return true;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private void CompileThreadMain()
        {
            Boolean success = false;
            Process process = null;
            Boolean attached = false;

            try
            {
                String gameRoot = Path.GetFullPath(Environment.CurrentDirectory);
                String compilerPath = FindCompiler(gameRoot);

                if (String.IsNullOrEmpty(compilerPath))
                    throw new FileNotFoundException(
                        "Memoria.Compiler.exe was not found under StreamingAssets/Scripts/Compiler.");

                String compilerDirectory = Path.GetDirectoryName(compilerPath);

                WriteLine("Compiler: " + compilerPath);
                WriteLine(String.Empty);

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = compilerPath;
                startInfo.WorkingDirectory = compilerDirectory;

                // IMPORTANT:
                // Memoria.Compiler calls Console.Clear and Console.ReadKey.
                // It must own a REAL console. Do not redirect stdout/stdin.
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = false;
                startInfo.WindowStyle = ProcessWindowStyle.Normal;

                process = Process.Start(startInfo);

                if (process == null)
                    throw new InvalidOperationException("Could not start Memoria.Compiler.exe.");

                // Give Windows a moment to create the compiler's console.
                for (Int32 attempt = 0; attempt < 30; attempt++)
                {
                    if (AttachConsole(unchecked((UInt32)process.Id)))
                    {
                        attached = true;
                        break;
                    }

                    if (process.HasExited)
                        break;

                    Thread.Sleep(50);
                }

                if (!attached)
                {
                    if (process.HasExited)
                    {
                        WriteLine("Memoria.Compiler exited during startup before the Dev Console could attach.");
                        WriteLine("This is treated as a normal compilation failure; FFIX will not restart.");
                        WriteLine(String.Empty);
                        WriteLine("------------------------------------------------------------");
                        WriteLine("COMPILATION FAILED");
                        WriteLine("Restart aborted.");
                        WriteLine("[ESC] Back");
                        return;
                    }

                    throw new InvalidOperationException(
                        "Could not attach to the compiler console. Win32 error: " +
                        Marshal.GetLastWin32Error());
                }

                IntPtr consoleWindow = GetConsoleWindow();

                if (consoleWindow != IntPtr.Zero)
                    ShowWindow(consoleWindow, SW_HIDE);

                IntPtr inputHandle = GetStdHandle(STD_INPUT_HANDLE);
                IntPtr outputHandle = GetStdHandle(STD_OUTPUT_HANDLE);

                if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1))
                    throw new InvalidOperationException("Could not open compiler console input.");

                if (outputHandle == IntPtr.Zero || outputHandle == new IntPtr(-1))
                    throw new InvalidOperationException("Could not open compiler console output.");

                // Memoria.Compiler compiles discovered source folders before it draws this menu.
                // A normal C# source error can therefore make the compiler exit before the menu exists.
                // Treat that as a compile failure, preserve the compiler screen, and leave Dev Console usable.
                String previousScreen;
                Boolean menuReady = WaitForText(
                    outputHandle,
                    inputHandle,
                    process,
                    "Pick which script(s) to compile",
                    60000,
                    out previousScreen);

                if (!menuReady)
                {
                    WriteLine(String.Empty);
                    WriteLine("------------------------------------------------------------");
                    WriteLine("COMPILATION FAILED");

                    if (!String.IsNullOrEmpty(previousScreen))
                    {
                        WriteLine("Last Memoria.Compiler screen:");
                        WriteRaw(previousScreen + Environment.NewLine);
                        WriteLine(String.Empty);
                    }

                    WriteLine("Memoria.Compiler exited before its normal menu became available.");
                    WriteLine("Restart aborted. Dev Console remains available.");
                    WriteLine("Press [R] to start a fresh compiler process, or [ESC] to go back.");
                    return;
                }

                Boolean compileFinished = false;

                while (!compileFinished && !_disposed && !process.HasExited)
                {
                    // Memoria.Compiler is at its normal A/C/Q menu.
                    SendKey(inputHandle, 'A', 0);

                    Boolean successPromptSeen = false;
                    Boolean failurePromptSeen = false;

                    while (!_disposed && !process.HasExited)
                    {
                        String screen = ReadConsoleText(outputHandle);

                        if (!String.IsNullOrEmpty(screen))
                        {
                            EmitScreenDelta(previousScreen, screen);
                            previousScreen = screen;

                            if (screen.IndexOf(
                                    "Press enter to exit...",
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                successPromptSeen = true;
                                break;
                            }

                            // Memoria.Compiler Program.Main catches compile errors
                            // and waits for R here. Keep THIS process alive.
                            if (screen.IndexOf(
                                    "Press R to retry",
                                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                                screen.IndexOf(
                                    "Fail!",
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                failurePromptSeen = true;
                                break;
                            }
                        }

                        Thread.Sleep(75);
                    }

                    if (successPromptSeen)
                    {
                        SendKey(inputHandle, '\r', VK_RETURN);
                        success = true;
                        compileFinished = true;
                        break;
                    }

                    if (failurePromptSeen)
                    {
                        WriteLine(String.Empty);
                        WriteLine("------------------------------------------------------------");
                        WriteLine("COMPILATION FAILED");
                        WriteLine("Restart aborted.");
                        WriteLine("Fix the source, then press [R] to compile again or [ESC] to go back.");

                        _retryRequested = false;
                        _awaitingRetry = true;

                        while (!_disposed && !process.HasExited && !_retryRequested)
                            Thread.Sleep(50);

                        if (_disposed || process.HasExited)
                        {
                            compileFinished = true;
                            break;
                        }

                        _retryRequested = false;
                        _awaitingRetry = false;

                        // This exactly follows Memoria.Compiler's own retry loop:
                        // R -> Program.Main loops -> Console.Clear() -> Compile()
                        // -> A/C/Q menu is drawn again.
                        SendKey(inputHandle, 'R', 0);

                        previousScreen = String.Empty;

                        Boolean retryMenuReady = WaitForText(
                            outputHandle,
                            inputHandle,
                            process,
                            "Pick which script(s) to compile",
                            60000,
                            out previousScreen);

                        if (!retryMenuReady)
                        {
                            compileFinished = true;
                            break;
                        }

                        // Menu is back. Outer loop sends A again.
                        continue;
                    }

                    compileFinished = true;
                }

                // Final screen-buffer read before detaching.
                String finalScreen = ReadConsoleText(outputHandle);

                if (!String.IsNullOrEmpty(finalScreen))
                {
                    EmitScreenDelta(previousScreen, finalScreen);
                    previousScreen = finalScreen;
                }

                WriteLine(String.Empty);
                WriteLine("------------------------------------------------------------");

                if (success)
                {
                    WriteLine("All sources compiled successfully.");
                    WriteLine(String.Empty);
                    WriteLine("Hard restarting FFIX...");
                }
                else if (!_awaitingRetry)
                {
                    WriteLine("COMPILATION FAILED");
                    WriteLine("Restart aborted.");
                    WriteLine("Fix the source, then press [R] to compile again or [ESC] to go back.");
                }
            }
            catch (Exception ex)
            {
                WriteLine(String.Empty);
                WriteLine("------------------------------------------------------------");
                WriteLine("COMPILATION FAILED");
                WriteLine(ex.ToString());
                WriteLine(String.Empty);
                WriteLine("Restart aborted.");
                WriteLine("[ESC] Back");
            }
            finally
            {
                if (attached)
                {
                    try
                    {
                        FreeConsole();
                    }
                    catch
                    {
                    }
                }

                if (process != null)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }

                _awaitingRetry = false;
                _retryRequested = false;
                _running = false;

                Action<Boolean> completed = _completed;

                if (completed != null)
                    completed(success);
            }
        }

        private Boolean WaitForText(
            IntPtr outputHandle,
            IntPtr inputHandle,
            Process process,
            String expectedText,
            Int32 timeoutMilliseconds,
            out String lastScreen)
        {
            Stopwatch timer = Stopwatch.StartNew();
            lastScreen = String.Empty;
            Boolean waitingForRetryPromptToClear = false;

            while (!_disposed)
            {
                String screen = ReadConsoleText(outputHandle);

                if (!String.IsNullOrEmpty(screen))
                {
                    lastScreen = screen;

                    if (screen.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _awaitingRetry = false;
                        _retryRequested = false;
                        EmitScreenDelta(String.Empty, screen);
                        return true;
                    }

                    Boolean hasRetryPrompt =
                        screen.IndexOf(
                            "Press R to retry",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        screen.IndexOf(
                            "Press R to",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        screen.IndexOf(
                            "retry",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        screen.IndexOf(
                            "Fail!",
                            StringComparison.OrdinalIgnoreCase) >= 0;

                    // After sending R, the old prompt can remain in the console
                    // buffer for a few frames while Memoria.Compiler clears and
                    // recompiles. Do not interpret that stale text as a second
                    // failure until we have observed the prompt disappear once.
                    if (waitingForRetryPromptToClear)
                    {
                        if (!hasRetryPrompt)
                            waitingForRetryPromptToClear = false;
                    }
                    else if (hasRetryPrompt)
                    {
                        EmitScreenDelta(String.Empty, screen);

                        WriteLine(String.Empty);
                        WriteLine("------------------------------------------------------------");
                        WriteLine("COMPILATION FAILED");
                        WriteLine("Source compilation failed before the compiler menu opened.");
                        WriteLine("Restart aborted. Dev Console remains available.");
                        WriteLine("Fix the source, then press [R] to retry in the SAME compiler process.");
                        WriteLine("[ESC] Back");

                        _retryRequested = false;
                        _awaitingRetry = true;

                        // Keep this compiler process and its real console alive.
                        // Memoria.Compiler already has a native retry loop; the
                        // Dev Console's R key simply tells that loop to retry.
                        while (!_disposed && !process.HasExited && !_retryRequested)
                            Thread.Sleep(50);

                        if (_disposed || process.HasExited)
                        {
                            _awaitingRetry = false;
                            _retryRequested = false;
                            return false;
                        }

                        _retryRequested = false;
                        _awaitingRetry = false;

                        SendKey(inputHandle, 'R', 0);

                        waitingForRetryPromptToClear = true;
                        lastScreen = String.Empty;
                        timer.Reset();
                        timer.Start();
                        Thread.Sleep(100);
                        continue;
                    }
                }

                if (process.HasExited)
                {
                    String finalScreen = ReadConsoleText(outputHandle);

                    if (!String.IsNullOrEmpty(finalScreen))
                        lastScreen = finalScreen;

                    if (!String.IsNullOrEmpty(lastScreen))
                        EmitScreenDelta(String.Empty, lastScreen);

                    _awaitingRetry = false;
                    _retryRequested = false;
                    return false;
                }

                // Do not time out while intentionally waiting for the user to
                // fix source code. The timeout only applies while the compiler
                // is actively trying to reach its menu.
                if (!_awaitingRetry && timer.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    throw new TimeoutException(
                        "Timed out waiting for the Memoria.Compiler menu.");
                }

                Thread.Sleep(50);
            }

            _awaitingRetry = false;
            _retryRequested = false;
            return false;
        }


        private void EmitScreenDelta(String previous, String current)
        {
            if (String.IsNullOrEmpty(current))
                return;

            if (String.IsNullOrEmpty(previous))
            {
                WriteRaw(current + Environment.NewLine);
                return;
            }

            if (current.StartsWith(previous, StringComparison.Ordinal))
            {
                String delta = current.Substring(previous.Length);

                if (!String.IsNullOrEmpty(delta))
                    WriteRaw(delta);

                return;
            }

            // Memoria.Compiler moves the console cursor while drawing the selection line.
            // If the new screen is not a strict prefix-extension, emit only complete lines
            // that were not already present at the end of the previous snapshot.
            String[] oldLines = NormalizeLines(previous);
            String[] newLines = NormalizeLines(current);

            Int32 common = 0;
            Int32 maxCommon = Math.Min(oldLines.Length, newLines.Length);

            while (common < maxCommon &&
                   String.Equals(oldLines[common], newLines[common], StringComparison.Ordinal))
            {
                common++;
            }

            for (Int32 i = common; i < newLines.Length; i++)
            {
                if (!String.IsNullOrEmpty(newLines[i]) && newLines[i].Trim().Length > 0)
                    WriteLine(newLines[i]);
            }
        }

        private static String[] NormalizeLines(String text)
        {
            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new Char[] { '\n' }, StringSplitOptions.None);
        }

        private static String ReadConsoleText(IntPtr outputHandle)
        {
            CONSOLE_SCREEN_BUFFER_INFO info;

            if (!GetConsoleScreenBufferInfo(outputHandle, out info))
                return String.Empty;

            Int32 width = info.dwSize.X;

            if (width <= 0)
                return String.Empty;

            Int32 lastRow = info.dwCursorPosition.Y;

            if (lastRow < 0)
                return String.Empty;

            StringBuilder result = new StringBuilder();

            for (Int32 row = 0; row <= lastRow; row++)
            {
                StringBuilder line = new StringBuilder(width);
                UInt32 read;

                COORD origin = new COORD();
                origin.X = 0;
                origin.Y = unchecked((Int16)row);

                if (!ReadConsoleOutputCharacter(
                    outputHandle,
                    line,
                    unchecked((UInt32)width),
                    origin,
                    out read))
                {
                    continue;
                }

                String value = line.ToString().TrimEnd(' ', '\0');

                result.Append(value);

                if (row < lastRow)
                    result.Append("\r\n");
            }

            return result.ToString().TrimEnd('\r', '\n');
        }

        private static void SendKey(IntPtr inputHandle, Char character, UInt16 virtualKey)
        {
            INPUT_RECORD[] records = new INPUT_RECORD[2];

            UInt16 keyCode =
                virtualKey != 0
                    ? virtualKey
                    : CharToVirtualKey(character);

            records[0].EventType = KEY_EVENT;
            records[0].KeyEvent = new KEY_EVENT_RECORD();
            records[0].KeyEvent.bKeyDown = true;
            records[0].KeyEvent.wRepeatCount = 1;
            records[0].KeyEvent.wVirtualKeyCode = keyCode;
            records[0].KeyEvent.UnicodeChar = character;

            records[1].EventType = KEY_EVENT;
            records[1].KeyEvent = records[0].KeyEvent;
            records[1].KeyEvent.bKeyDown = false;

            UInt32 written;

            if (!WriteConsoleInput(inputHandle, records, 2, out written) || written != 2)
            {
                throw new InvalidOperationException(
                    "Could not send key to Memoria.Compiler. Win32 error: " +
                    Marshal.GetLastWin32Error());
            }
        }

        private void WriteLine(String line)
        {
            WriteRaw((line ?? String.Empty) + Environment.NewLine);
        }

        private void WriteRaw(String text)
        {
            Action<String> output = _output;

            if (output != null && !String.IsNullOrEmpty(text))
            {
                output(
                    text
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Replace("\n", "\r\n"));
            }
        }

        private static UInt16 CharToVirtualKey(Char character)
        {
            Int16 value = VkKeyScan(character);

            if (value == -1)
                return 0;

            return unchecked((UInt16)(value & 0xFF));
        }

        private static String FindCompiler(String gameRoot)
        {
            String scripts = Path.Combine(
                Path.Combine(gameRoot, "StreamingAssets"),
                "Scripts");

            String[] candidates =
            {
                Path.Combine(Path.Combine(scripts, "Compiler"), "Memoria.Compiler.exe"),
                Path.Combine(scripts, "Memoria.Compiler.exe"),
                Path.Combine(gameRoot, "Memoria.Compiler.exe")
            };

            for (Int32 i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return Path.GetFullPath(candidates[i]);
            }

            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public Int16 X;
            public Int16 Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SMALL_RECT
        {
            public Int16 Left;
            public Int16 Top;
            public Int16 Right;
            public Int16 Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public UInt16 wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT_RECORD
        {
            public UInt16 EventType;
            public KEY_EVENT_RECORD KeyEvent;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct KEY_EVENT_RECORD
        {
            [MarshalAs(UnmanagedType.Bool)]
            public Boolean bKeyDown;

            public UInt16 wRepeatCount;
            public UInt16 wVirtualKeyCode;
            public UInt16 wVirtualScanCode;
            public Char UnicodeChar;
            public UInt32 dwControlKeyState;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean AttachConsole(UInt32 dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(Int32 nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean GetConsoleScreenBufferInfo(
            IntPtr hConsoleOutput,
            out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern Boolean ReadConsoleOutputCharacter(
            IntPtr hConsoleOutput,
            StringBuilder lpCharacter,
            UInt32 nLength,
            COORD dwReadCoord,
            out UInt32 lpNumberOfCharsRead);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern Boolean WriteConsoleInput(
            IntPtr hConsoleInput,
            [In] INPUT_RECORD[] lpBuffer,
            UInt32 nLength,
            out UInt32 lpNumberOfEventsWritten);

        [DllImport("user32.dll")]
        private static extern Boolean ShowWindow(IntPtr hWnd, Int32 nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int16 VkKeyScan(Char ch);
    }
}
