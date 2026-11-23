using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private Boolean _fittingContent;

        private static String WrapConsoleContent(String text, Int32 columns)
        {
            StringBuilder result = new StringBuilder();
            Int32 column = 0, space = -1, afterSpace = 0;
            for (Int32 i = 0; i < text.Length; i++)
            {
                String marker = null;
                if (String.CompareOrdinal(text, i, YellowOpen, 0, YellowOpen.Length) == 0) marker = YellowOpen;
                else if (String.CompareOrdinal(text, i, YellowClose, 0, YellowClose.Length) == 0) marker = YellowClose;
                else if (String.CompareOrdinal(text, i, CyanOpen, 0, CyanOpen.Length) == 0) marker = CyanOpen;
                else if (String.CompareOrdinal(text, i, CyanClose, 0, CyanClose.Length) == 0) marker = CyanClose;
                else if (String.CompareOrdinal(text, i, StatusOpen, 0, StatusOpen.Length) == 0) marker = StatusOpen;
                else if (String.CompareOrdinal(text, i, StatusClose, 0, StatusClose.Length) == 0) marker = StatusClose;
                if (marker != null) { result.Append(marker); i += marker.Length - 1; continue; }
                Char ch = text[i];
                if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    result.Append("\r\n"); column = 0; space = -1; afterSpace = 0; continue;
                }
                if (column >= columns)
                {
                    if (space >= 0)
                    {
                        result.Remove(space, 1).Insert(space, "\r\n");
                        column = afterSpace;
                    }
                    else { result.Append("\r\n"); column = 0; }
                    space = -1;
                }
                if (ch == ' ') { space = result.Length; afterSpace = 0; }
                else afterSpace++;
                result.Append(ch); column++;
            }
            return result.ToString();
        }

        private void FitConsoleToContent(String text)
        {
            // Logs stream indefinitely. Save-list paging already depends on client height.
            if (_fittingContent || _page == Page.LiveLog ||
                (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.SaveList) ||
                _windowHandle == IntPtr.Zero || IsZoomed(_windowHandle) || IsIconic(_windowHandle)) return;
            RECT bounds, client;
            if (!GetWindowRect(_windowHandle, out bounds) || !GetClientRect(_windowHandle, out client)) return;
            MONITORINFO monitor = new MONITORINFO();
            monitor.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(MonitorFromWindow(_windowHandle, 2), ref monitor)) return;
            IntPtr dc = GetDC(_textHandle);
            if (dc == IntPtr.Zero) return;
            IntPtr layoutFont=CreateFont(-21,0,0,0,400,0,0,0,1,0,0,0,49,"Consolas");
            IntPtr previous = SelectObject(dc, layoutFont);
            SIZE cell;
            try { if (!GetTextExtentPoint32(dc, "M", 1, out cell)) return; }
            finally { SelectObject(dc, previous); ReleaseDC(_textHandle, dc); DeleteObject(layoutFont); }
            String[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
            Int32 columns = 0;
            foreach (String line in lines) columns = Math.Max(columns, line.Length);
            Int32 chromeWidth = bounds.Right - bounds.Left - client.Right;
            Int32 chromeHeight = bounds.Bottom - bounds.Top - client.Bottom;
            Int32 width = Math.Min(monitor.Work.Right - monitor.Work.Left,
                Math.Max(600, columns * cell.Width + 48 + chromeWidth));
            Int32 height = Math.Min(monitor.Work.Bottom - monitor.Work.Top,
                Math.Max(260, (lines.Length + 1) * cell.Height + (_page == Page.Home ? cell.Height : 0) + 32 + chromeHeight + (_thumbnailVisible ? PreviewStripHeight : 0)));
            Int32 left = Math.Max(monitor.Work.Left, Math.Min(bounds.Left, monitor.Work.Right - width));
            Int32 top = Math.Max(monitor.Work.Top, Math.Min(bounds.Top, monitor.Work.Bottom - height));
            if (bounds.Right - bounds.Left == width && bounds.Bottom - bounds.Top == height && bounds.Left == left && bounds.Top == top) return;
            _fittingContent = true;
            try { MoveWindow(_windowHandle, left, top, width, height, true); }
            finally { _fittingContent = false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public Int32 Width, Height; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public Int32 Size; public RECT Monitor, Work; public UInt32 Flags; }
        [DllImport("user32.dll")] private static extern Boolean GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] private static extern Boolean IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern Boolean IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, UInt32 flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern Boolean GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern Int32 ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern Boolean GetTextExtentPoint32(IntPtr dc, String text, Int32 length, out SIZE size);
    }
}
