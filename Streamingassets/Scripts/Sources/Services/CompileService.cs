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
            if (_disposed || _running)
                return false;

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
                    throw new InvalidOperationException(
                        "Could not attach to the compiler console. Win32 error: " +
                        Marshal.GetLastWin32Error());

                IntPtr consoleWindow = GetConsoleWindow();

                if (consoleWindow != IntPtr.Zero)
                    ShowWindow(consoleWindow, SW_HIDE);

                IntPtr inputHandle = GetStdHandle(STD_INPUT_HANDLE);
                IntPtr outputHandle = GetStdHandle(STD_OUTPUT_HANDLE);

                if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1))
                    throw new InvalidOperationException("Could not open compiler console input.");

                if (outputHandle == IntPtr.Zero || outputHandle == new IntPtr(-1))
                    throw new InvalidOperationException("Could not open compiler console output.");

                // Wait for Memoria.Compiler's selection prompt before pressing A.
                WaitForText(outputHandle, process, "Pick which script(s) to compile", 10000);

                SendKey(inputHandle, 'A', 0);

                String previousScreen = String.Empty;
                Boolean successPromptSeen = false;
                Boolean failurePromptSeen = false;
                Boolean exitKeySent = false;

                while (!process.HasExited)
                {
                    String screen = ReadConsoleText(outputHandle);

                    if (!String.IsNullOrEmpty(screen))
                    {
                        EmitScreenDelta(previousScreen, screen);
                        previousScreen = screen;

                        if (!successPromptSeen &&
                            screen.IndexOf(
                                "Press enter to exit...",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            successPromptSeen = true;
                        }

                        if (!failurePromptSeen &&
                            screen.IndexOf(
                                "Press R to retry or any other key to exit...",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            failurePromptSeen = true;
                        }

                        if (!exitKeySent && successPromptSeen)
                        {
                            SendKey(inputHandle, '\r', VK_RETURN);
                            exitKeySent = true;
                        }
                        else if (!exitKeySent && failurePromptSeen)
                        {
                            // Any key except R exits the failure screen.
                            SendKey(inputHandle, 'Q', 0);
                            exitKeySent = true;
                        }
                    }

                    Thread.Sleep(75);
                }

                // Final screen-buffer read before detaching.
                String finalScreen = ReadConsoleText(outputHandle);

                if (!String.IsNullOrEmpty(finalScreen))
                {
                    EmitScreenDelta(previousScreen, finalScreen);
                    previousScreen = finalScreen;
                }

                success = successPromptSeen && !failurePromptSeen;

                WriteLine(String.Empty);
                WriteLine("------------------------------------------------------------");

                if (success)
                {
                    WriteLine("All sources compiled successfully.");
                    WriteLine(String.Empty);
                    WriteLine("Hard restarting FFIX...");
                }
                else
                {
                    WriteLine("COMPILATION FAILED");
                    WriteLine("Restart aborted.");
                    WriteLine("[ESC] Back");
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

                _running = false;

                Action<Boolean> completed = _completed;

                if (completed != null)
                    completed(success);
            }
        }

        private void WaitForText(
            IntPtr outputHandle,
            Process process,
            String expectedText,
            Int32 timeoutMilliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();

            while (timer.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (process.HasExited)
                    throw new InvalidOperationException(
                        "Memoria.Compiler exited before showing its menu.");

                String screen = ReadConsoleText(outputHandle);

                if (!String.IsNullOrEmpty(screen))
                {
                    if (screen.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                }

                Thread.Sleep(50);
            }

            throw new TimeoutException(
                "Timed out waiting for the Memoria.Compiler menu.");
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
