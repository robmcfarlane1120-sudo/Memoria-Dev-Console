using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        // Win32 COLORREF stores RGB values in BGR order.
        private const UInt32 PrimaryColor = 0x00F2F2F2;
        private static UInt32 SelectionColor { get { return ConsoleTheme.Accent; } } // #00FFFF
        private const UInt32 SelectedTextColor = 0x00FFFFFF;
        private static UInt32 SelectedBackgroundColor { get { return ConsoleTheme.Background; } } // #174A70
        private static UInt32 DescriptionHighlightColor { get { return ConsoleTheme.Accent == 0x00FFFF00u ? 0x0000D7FFu : 0x00FFFF00u; } }
        private static UInt32 HeadingColor { get { return ConsoleTheme.Accent; } }
        private const UInt32 SuccessColor = 0x0099FF66;   // #66FF99
        private const UInt32 WarningColor = 0x0000D7FF;   // #FFD700
        private const UInt32 SecondaryColor = 0x00C0B4AA; // #AAB4C0
        private static readonly Regex WarningLine = new Regex(
            @"\b(failed|failure|error|invalid|missing|blocked|refused|unavailable|experimental|requires restart|restart required|restart aborted)\b", RegexOptions.IgnoreCase);
        private static readonly Regex SuccessLine = new Regex(
            @"^(saved|exported|installed|updated|copied|opened|loaded|returned|compiled|definition copied|coordinates copied|all sources compiled|.*\bcomplete[.!]?$)", RegexOptions.IgnoreCase);
        private static readonly Regex ControlsLine = new Regex(
            @"^(\[(Enter|Up|Left|F\d|Esc|N\])|Arrow Keys:|Up/Down:|Enter[:=]|ESC[:=]|F\d+:|[A-Z]: |[A-Z]  |N / Enter:)", RegexOptions.IgnoreCase);

        private static UInt32 LinePalette(String line)
        {
            String value = line.Trim();
            if (value == "STATUS" || value.StartsWith("STATUS:",StringComparison.OrdinalIgnoreCase)) return WarningColor;
            if (value.StartsWith(">>",StringComparison.Ordinal)) return SelectedTextColor;
            if (value.StartsWith("MEMORIA DEV CONSOLE",StringComparison.Ordinal) ||
                value.Contains("D E V   C O N S O L E") ||
                value.StartsWith("__  __ _____",StringComparison.Ordinal) ||
                value.StartsWith("|",StringComparison.Ordinal)) return SelectionColor;
            // Menu labels keep the same normal color even when their names
            // contain a warning word such as EXPERIMENTAL.
            if (Regex.IsMatch(value,@"^\[[0-9L]\]\s")) return PrimaryColor;
            if (value == "IMPORTANT" || WarningLine.IsMatch(value)) return WarningColor;
            if (value.StartsWith("Output:",StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Export:",StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Place redesigned PNGs in:",StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("All dumps:",StringComparison.OrdinalIgnoreCase) ||
                value.IndexOf(":\\",StringComparison.Ordinal) >= 0) return SuccessColor;
            String status = value.StartsWith("STATUS: ",StringComparison.Ordinal) ? value.Substring(8) : value;
            if (SuccessLine.IsMatch(status)) return SuccessColor;
            if (ControlsLine.IsMatch(value) || value.StartsWith("---",StringComparison.Ordinal) || value.StartsWith("===",StringComparison.Ordinal)) return SecondaryColor;
            if (value.Length > 0 && value == value.ToUpperInvariant() && Regex.IsMatch(value,@"[A-Z]")) return HeadingColor;
            return PrimaryColor;
        }

        private static String CyanText(String text) { return CyanOpen + text + CyanClose; }
        private const String StatusOpen = "<MDC_STATUS>";
        private const String StatusClose = "</MDC_STATUS>";
        private static String StatusText(String text) { return StatusOpen + text + StatusClose; }

        private void ApplyTextBackground(Int32 start, Int32 end, UInt32 color)
        {
            SendMessage(_textHandle, EM_SETSEL, new IntPtr(start), new IntPtr(end));
            CHARFORMAT2 format = new CHARFORMAT2();
            format.cbSize = (UInt32)Marshal.SizeOf(typeof(CHARFORMAT2));
            format.dwMask = 0x04000000; // CFM_BACKCOLOR; explicit color, not automatic.
            format.crBackColor = color;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CHARFORMAT2)));
            try
            {
                Marshal.StructureToPtr(format, ptr, false);
                SendMessage(_textHandle, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        private void ApplyConsolePalette(String text, List<Int32[]> markedRanges)
        {
            ApplyTextBackground(0, text.Length, _page == Page.Home || _page == Page.LiveLog || _page == Page.CompileRestart ? 0x00101010u : 0u);
            ApplyTextColor(0,text.Length,PrimaryColor);
            Int32 start = 0;
            foreach (String line in text.Split('\r'))
            {
                UInt32 color = LinePalette(line);
                // Home has two independent menu cells per row. Its explicit
                // marked range colors only the selected cell, never its neighbor.
                if (_page == Page.Home && line.TrimStart().StartsWith(">>", StringComparison.Ordinal))
                    color = PrimaryColor;
                if (color != PrimaryColor) ApplyTextColor(start,start + line.Length,color);
                if (_page != Page.Home && line.TrimStart().StartsWith(">>", StringComparison.Ordinal))
                    ApplyTextBackground(start, start + line.TrimEnd().Length, SelectedBackgroundColor);
                start += line.Length + 1;
            }
            foreach (Int32[] range in markedRanges)
            {
                String value = text.Substring(range[0],range[1] - range[0]).TrimStart();
                UInt32 color = value.StartsWith(">>",StringComparison.Ordinal) ? SelectionColor :
                    (value.StartsWith("Output:",StringComparison.OrdinalIgnoreCase) || value.StartsWith("All dumps:",StringComparison.OrdinalIgnoreCase)) ? SuccessColor : WarningColor;
                if (range.Length > 2) color = (UInt32)range[2];
                if (value.StartsWith(">>", StringComparison.Ordinal))
                {
                    color = SelectedTextColor;
                    ApplyTextBackground(range[0], range[1], SelectedBackgroundColor);
                }
                ApplyTextColor(range[0],range[1],color);
            }
        }
    }
}

namespace Memoria.DevConsole
{
    internal static class ConsoleTheme
    {
        internal static readonly String[] Names = { "Cyan", "Gold", "Red", "Dark Blue", "Purple", "White", "Pink", "Green", "Orange", "Blue", "Yellow" };
        private static readonly UInt32[] Colors = { 0xFFFF00, 0x00D7FF, 0x5050FF, 0xCD693D, 0xFF80C0, 0xFFFFFF, 0xC080FF, 0x66DD66, 0x329EFF, 0xFFB050, 0x55FFFF };
        private static readonly String SettingsPath = System.IO.Path.GetFullPath("Memoria Dev Console/FF9_Data/ConsoleColor.txt");
        private static Int32 selected = Load();
        internal static Int32 Selected { get { return selected; } }
        internal static UInt32 Accent { get { return Colors[selected]; } }
        internal static UInt32 Background { get { UInt32 c=Accent; return ((c&255)/5+16) | ((((c>>8)&255)/5+16)<<8) | ((((c>>16)&255)/5+16)<<16); } }
        internal static UnityEngine.Color UnityAccent { get { UInt32 c=Accent; return new UnityEngine.Color((c&255)/255f,((c>>8)&255)/255f,((c>>16)&255)/255f,1); } }
        internal static UnityEngine.Color UnityBackground { get { UInt32 c=Background; return new UnityEngine.Color((c&255)/255f,((c>>8)&255)/255f,((c>>16)&255)/255f,1); } }
        private static Int32 Load()
        {
            try { String name=System.IO.File.ReadAllText(SettingsPath).Trim(); Int32 index=Array.IndexOf(Names,name); return index<0?Array.IndexOf(Names,"White"):index; }
            catch { return Array.IndexOf(Names,"White"); }
        }
        internal static void Select(Int32 index)
        {
            if(index<0 || index>=Names.Length)return;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
            System.IO.File.WriteAllText(SettingsPath,Names[index]);
            selected=index;
        }
    }
    public sealed partial class NativeConsoleWindow
    {
        private Int32 _consoleColorRow;
        private String _consoleColorStatus="Select a color, then double-click or press Enter to apply.";
        private void RenderConsoleColors()
        {
            _page=Page.ConsoleColors;
            SetWindowText(_windowHandle,"Memoria Dev Console v1.4.1 - Console Color");
            var text=new System.Text.StringBuilder();
            text.Append(CyanText("CONSOLE COLOR")+"\r\n");
            for(Int32 i=0;i<ConsoleTheme.Names.Length;i++)
                text.Append(MenuLine(i==_consoleColorRow,ConsoleTheme.Names[i]+(i==ConsoleTheme.Selected?"  [Active]":"")));
            text.Append("\r\n"+_consoleColorStatus+"\r\nHeaders and selected highlights use this color. Saved automatically.\r\n");
            SetConsoleText(text.ToString());
        }
        private void ApplyConsoleColor()
        {
            try { ConsoleTheme.Select(_consoleColorRow); _consoleColorStatus="Applied: "+ConsoleTheme.Names[_consoleColorRow]; }
            catch(Exception ex) { _consoleColorStatus="Could not save color: "+ex.Message; }
            RenderConsoleColors();
        }
    }
}
namespace Memoria.DevConsole
{
    // Diagnostic call arguments are omitted entirely from normal builds.
    internal static class ViewerDiagnostics
    {
        [System.Diagnostics.Conditional("MDC_VIEWER_DIAGNOSTICS")]
        internal static void Message(String text) { Memoria.Prime.Log.Message(text); }
        [System.Diagnostics.Conditional("MDC_VIEWER_DIAGNOSTICS")]
        internal static void Warning(String text) { Memoria.Prime.Log.Warning(text); }
    }
}