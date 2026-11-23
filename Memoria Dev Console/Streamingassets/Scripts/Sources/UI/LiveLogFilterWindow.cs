using System;
using System.Text;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private LiveLogFilters logFilters;
        private Int32 logFilterRow;
        private String lastLogOwner;
        private const String CyanOpen = "<MDC_CYAN>";
        private const String CyanClose = "</MDC_CYAN>";

        private void EnsureLogFilters()
        {
            if (logFilters != null) return;
            logFilters = new LiveLogFilters();
            logFilters.RefreshSources();
        }
        private void OpenLogFilters()
        {
            EnsureLogFilters(); logFilters.RefreshSources();
            logFilterRow = Math.Min(logFilterRow,logFilters.Sources.Count - 1);
            RenderLogFilters();
        }
        private void RenderLogFilters()
        {
            _page = Page.LiveLogFilters;
            SetWindowText(_windowHandle,"Memoria Dev Console v1.4.1 - Log Filters");
            var text = new StringBuilder();
            text.Append(Header("LIVE LOG / FILTERS"));
            text.Append("Game running. Filters change MDC's display, not Memoria.log.\r\n\r\n");
            text.Append("SOURCE                                  DISPLAY\r\n").Append(Rule());
            for (Int32 i = 0; i < logFilters.Sources.Count; i++)
            {
                String name = logFilters.Sources[i];
                text.Append(MenuLine(i == logFilterRow,Shorten(name,38).PadRight(40) + logFilters.Mode(name)));
            }
            text.Append("\r\nNormal: white   Hidden: omit   Cyan / Gold: highlight source\r\n");
            text.Append("Log tags identify mods where possible. Untagged or ambiguous entries\r\n");
            text.Append("remain under Memoria / Unattributed. Stack traces follow their entry.\r\n\r\n");
            text.Append(Footer("Up/Down: Source","Enter / Left/Right: Change","R: Reset all to Normal","Esc / F: Return to log"));
            SetConsoleText(text.ToString());
        }
        private Boolean HandleLogFilterKey(Int32 key)
        {
            if (key == VK_ESCAPE || key == VK_F) { RenderLiveLog(); return true; }
            if (key == VK_UP || key == VK_DOWN)
                logFilterRow = WrapSelection(logFilterRow + (key == VK_UP ? -1 : 1),logFilters.Sources.Count);
            else if (key == VK_RETURN || key == VK_LEFT || key == VK_RIGHT)
            {
                try { logFilters.Cycle(logFilters.Sources[logFilterRow],key == VK_LEFT ? -1 : 1); }
                catch { /* Keep the chosen in-memory filter if preferences cannot be saved. */ }
            }
            else if (key == VK_R)
            {
                try { logFilters.Reset(); } catch { }
            }
            else return false;
            RenderLogFilters(); return true;
        }
        private String FilteredLogHistory(String raw)
        {
            EnsureLogFilters(); lastLogOwner = null;
            var text = new StringBuilder();
            foreach (FilteredLogLine line in logFilters.Filter(raw,ref lastLogOwner))
                text.Append(line.Gold ? YellowOpen + line.Text + YellowClose : line.Cyan ? CyanOpen + line.Text + CyanClose : line.Text);
            return text.ToString();
        }
        private void AppendFilteredLog(String raw)
        {
            EnsureLogFilters();
            var lines = logFilters.Filter(raw,ref lastLogOwner);
            if (lines.Count == 0) return;
            SendMessage(_textHandle,0x000B,IntPtr.Zero,IntPtr.Zero);
            try
            {
                foreach (FilteredLogLine line in lines)
                {
                    Int32 end = GetWindowTextLength(_textHandle);
                    ApplyTextColor(end,end,line.Gold ? 0x0000D7FFu : line.Cyan ? 0x00FFFF00u : PrimaryColor);
                    SendMessageText(_textHandle,EM_REPLACESEL,IntPtr.Zero,line.Text);
                }
                ScrollBottom();
            }
            finally
            {
                SendMessage(_textHandle,0x000B,new IntPtr(1),IntPtr.Zero);
                InvalidateRect(_textHandle,IntPtr.Zero,false);
            }
        }
    }
}
