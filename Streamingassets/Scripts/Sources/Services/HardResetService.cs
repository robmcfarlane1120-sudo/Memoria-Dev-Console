using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public static class HardResetService
    {
        public static void Restart()
        {
            String[] commandLine = Environment.GetCommandLineArgs();
            String executable = GetExecutablePath(commandLine);

            // Keep the game root as the working directory. Memoria resolves
            // configuration and mod paths from here.
            String workingDirectory = Environment.CurrentDirectory;
            Int32 processId = unchecked((Int32)GetCurrentProcessId());
            String arguments = BuildArguments(commandLine);
            String script = CreateRestartScript(processId, executable, workingDirectory, arguments);

            ProcessStartInfo start = new ProcessStartInfo(
                "cmd.exe",
                "/D /S /C \"\"" + script + "\"\"");

            start.WorkingDirectory = workingDirectory;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;

            Process.Start(start);

            Log.Message("[Dev Console] Restart helper started; quitting current FFIX process.");
            UIManager.Input.ConfirmQuit();
        }


        public static void RestartToLauncher()
        {
            String workingDirectory = Environment.CurrentDirectory;
            String launcher = Path.Combine(workingDirectory, "FF9_Launcher.exe");

            if (!File.Exists(launcher))
                throw new FileNotFoundException("FF9_Launcher.exe was not found in the FFIX game directory.", launcher);

            Int32 processId = unchecked((Int32)GetCurrentProcessId());
            String script = CreateLauncherRestartScript(processId, launcher, workingDirectory);

            ProcessStartInfo start = new ProcessStartInfo(
                "cmd.exe",
                "/D /S /C \"\"" + script + "\"\"");

            start.WorkingDirectory = workingDirectory;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;

            Process.Start(start);

            Log.Message("[Dev Console] Launcher restart helper started; quitting current FFIX process.");
            UIManager.Input.ConfirmQuit();
        }

        private static String GetExecutablePath(String[] commandLine)
        {
            if (commandLine.Length > 0 && Path.IsPathRooted(commandLine[0]))
                return Path.GetFullPath(commandLine[0]);

            String executableName =
                commandLine.Length > 0
                    ? Path.GetFileName(commandLine[0])
                    : "FF9.exe";

            return Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                executableName);
        }

        private static String BuildArguments(String[] commandLine)
        {
            StringBuilder result = new StringBuilder();
            Boolean hasRunByLauncher = false;

            for (Int32 i = 1; i < commandLine.Length; i++)
            {
                String argument = commandLine[i];

                if (String.Equals(argument, "-screen-quality", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < commandLine.Length && !commandLine[i + 1].StartsWith("-"))
                        i++;

                    continue;
                }

                if (String.Equals(argument, "-runbylauncher", StringComparison.OrdinalIgnoreCase))
                    hasRunByLauncher = true;

                AppendArgument(result, argument);
            }

            if (!hasRunByLauncher)
            {
                if (result.Length > 0)
                    result.Insert(0, ' ');

                result.Insert(0, "-runbylauncher");
            }

            return result.ToString();
        }

        private static void AppendArgument(StringBuilder result, String argument)
        {
            if (result.Length > 0)
                result.Append(' ');

            result.Append(QuoteArgument(argument));
        }

        private static String CreateRestartScript(
            Int32 processId,
            String executable,
            String workingDirectory,
            String arguments)
        {
            String script = Path.Combine(
                Path.GetTempPath(),
                "MemoriaDevConsoleRestart_" + processId + ".cmd");

            String helperLog = Path.Combine(
                Path.GetTempPath(),
                "MemoriaDevConsoleRestart_Last.log");

            StringBuilder content = new StringBuilder();

            content.AppendLine("@echo off");
            content.AppendLine(
                "echo Dev Console restart helper started %date% %time% > \"" +
                EscapeBatchQuoted(helperLog) + "\"");

            content.AppendLine(":wait");
            content.AppendLine(
                "tasklist /FI \"PID eq " + processId +
                "\" /NH | findstr /R /C:\"[ ]" + processId + "[ ]\" >nul");

            content.AppendLine(
                "if not errorlevel 1 (ping 127.0.0.1 -n 2 >nul & goto wait)");

            content.AppendLine(
                "echo Old PID exited %date% %time% >> \"" +
                EscapeBatchQuoted(helperLog) + "\"");

            content.AppendLine(
                "cd /d \"" + EscapeBatchQuoted(workingDirectory) + "\"");

            content.AppendLine(
                "start \"\" /D \"" + EscapeBatchQuoted(workingDirectory) +
                "\" \"" + EscapeBatchQuoted(executable) + "\" " + arguments);

            content.AppendLine(
                "echo START errorlevel: %errorlevel% >> \"" +
                EscapeBatchQuoted(helperLog) + "\"");

            content.AppendLine("del \"%~f0\"");

            File.WriteAllText(script, content.ToString(), Encoding.ASCII);
            return script;
        }


        private static String CreateLauncherRestartScript(
            Int32 processId,
            String launcher,
            String workingDirectory)
        {
            String script = Path.Combine(
                Path.GetTempPath(),
                "MemoriaDevConsoleLauncherRestart_" + processId + ".cmd");

            String helperLog = Path.Combine(
                Path.GetTempPath(),
                "MemoriaDevConsoleLauncherRestart_Last.log");

            StringBuilder content = new StringBuilder();

            content.AppendLine("@echo off");
            content.AppendLine(
                "echo Dev Console launcher restart helper started %date% %time% > \"" +
                EscapeBatchQuoted(helperLog) + "\"");

            content.AppendLine(":wait");
            content.AppendLine(
                "tasklist /FI \"PID eq " + processId +
                "\" /NH | findstr /R /C:\"[ ]" + processId + "[ ]\" >nul");

            content.AppendLine(
                "if not errorlevel 1 (ping 127.0.0.1 -n 2 >nul & goto wait)");

            content.AppendLine(
                "echo Old PID exited %date% %time% >> \"" +
                EscapeBatchQuoted(helperLog) + "\"");

            content.AppendLine(
                "cd /d \"" + EscapeBatchQuoted(workingDirectory) + "\"");

            content.AppendLine(
                "start \"\" /D \"" + EscapeBatchQuoted(workingDirectory) +
                "\" \"" + EscapeBatchQuoted(launcher) + "\"");

            content.AppendLine(
                "echo START errorlevel: %errorlevel% >> \"" +
                EscapeBatchQuoted(helperLog) + "\"");

            content.AppendLine("del \"%~f0\"");

            File.WriteAllText(script, content.ToString(), Encoding.ASCII);
            return script;
        }

        private static String EscapeBatchQuoted(String value)
        {
            return value.Replace("\"", "\"\"");
        }

        private static String QuoteArgument(String value)
        {
            if (String.IsNullOrEmpty(value))
                return "\"\"";

            if (value.IndexOfAny(new Char[] { ' ', '\t', '\"' }) < 0)
                return value;

            StringBuilder result = new StringBuilder("\"");
            Int32 backslashes = 0;

            for (Int32 i = 0; i < value.Length; i++)
            {
                Char character = value[i];

                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                result.Append(
                    '\\',
                    character == '\"'
                        ? backslashes * 2 + 1
                        : backslashes);

                backslashes = 0;
                result.Append(character);
            }

            result.Append('\\', backslashes * 2);
            result.Append('\"');

            return result.ToString();
        }

        [DllImport("kernel32.dll")]
        private static extern UInt32 GetCurrentProcessId();
    }
}
