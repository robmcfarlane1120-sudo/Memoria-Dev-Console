using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed class LiveLogService : IDisposable
    {
        private readonly Int32 _maxLines;
        private readonly Queue<String> _pendingLines = new Queue<String>();
        private readonly Queue<String> _history = new Queue<String>();

        private FileStream _stream;
        private StreamReader _reader;
        private String _logPath;
        private String _status = "Looking for Memoria.log...";

        public LiveLogService(Int32 maxLines)
        {
            _maxLines = Math.Max(50, maxLines);
        }

        public void Initialize()
        {
            _logPath = FindLogPath();

            if (String.IsNullOrEmpty(_logPath))
            {
                _status = "Memoria.log not found yet";
                return;
            }

            OpenLog();
        }

        public void Update()
        {
            try
            {
                if (_reader == null)
                {
                    _logPath = FindLogPath();

                    if (!String.IsNullOrEmpty(_logPath))
                        OpenLog();

                    return;
                }

                if (_stream.Length < _stream.Position)
                {
                    OpenLog();
                    return;
                }

                ReadNewLines();
            }
            catch (Exception ex)
            {
                _status = "Log read error: " + ex.Message;
                CloseReader();
            }
        }

        public String DrainPendingText()
        {
            if (_pendingLines.Count == 0)
                return String.Empty;

            StringBuilder builder = new StringBuilder();

            while (_pendingLines.Count > 0)
                builder.AppendLine(_pendingLines.Dequeue());

            return builder.ToString();
        }

        public void Dispose()
        {
            CloseReader();
        }

        private void OpenLog()
        {
            CloseReader();

            _stream = new FileStream(
                _logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            _reader = new StreamReader(_stream);
            _status = Path.GetFileName(_logPath);

            // NativeConsoleWindow owns the Live Log header. Keep the log stream
            // itself pure so the page has one continuous header/separator only.
            ReadNewLines();
        }

        private void ReadNewLines()
        {
            String line;

            while ((line = _reader.ReadLine()) != null)
                AddLine(line);
        }

        private void AddLine(String line)
        {
            _history.Enqueue(line);
            _pendingLines.Enqueue(line);

            while (_history.Count > _maxLines)
                _history.Dequeue();
        }

        private void CloseReader()
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }

        private static String FindLogPath()
        {
            String current = Directory.GetCurrentDirectory();

            String[] candidates =
            {
                Path.Combine(current, "Memoria.log"),
                Path.Combine(Path.Combine(current, "x64"), "Memoria.log"),
                Path.Combine(Path.Combine(Application.dataPath, ".."), "Memoria.log"),
                Path.Combine(Path.Combine(Path.Combine(Application.dataPath, ".."), ".."), "Memoria.log")
            };

            for (Int32 i = 0; i < candidates.Length; i++)
            {
                try
                {
                    String full = Path.GetFullPath(candidates[i]);

                    if (File.Exists(full))
                        return full;
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
