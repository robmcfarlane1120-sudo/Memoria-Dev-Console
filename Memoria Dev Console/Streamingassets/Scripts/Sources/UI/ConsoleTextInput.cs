using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private Boolean IsTextEntryActive
        {
            get
            {
                return (_page == Page.BattleSceneEditor && battleEditing != null) ||
                    (_page == Page.WorldEntrances && worldEditing != null) ||
                    (_page == Page.SoundBrowser && (_soundMode == SoundBrowserMode.AliasEntry || _soundMode == SoundBrowserMode.FilterEntry)) ||
                    (_page == Page.FieldState && _fieldSaveSubMode == FieldSaveSubMode.TitleEntry) ||
                    (_page == Page.Warp && _warpEntryActive) || (_page == Page.AssetDumper && _assetIdEntryActive);
            }
        }
        private static Boolean TextModifier(Int32 key)
        { return key == 0x10 || key == 0x11 || key == 0x12 || key == 0x14 || key == 0x90 || key == 0x91 || (key >= 0xA0 && key <= 0xA5); }
        private static Boolean RepeatTextKey(Int32 key)
        {
            return key == VK_BACK || key == VK_DELETE || key == 0x20 ||
                (key >= 0x30 && key <= 0x5A) || (key >= 0x60 && key <= 0x6F) || (key >= 0xBA && key <= 0xE2);
        }
        private Boolean ModifierDown(Int32 generic, Int32 left, Int32 right)
        {
            return _keyboardDown[generic] || _keyboardDown[left] || _keyboardDown[right] ||
                (GetAsyncKeyState(generic) & 0x8000) != 0;
        }
        private String TypedText(Int32 key)
        {
            Byte[] state = new Byte[256];
            GetKeyboardState(state);
            Boolean shift = ModifierDown(0x10,0xA0,0xA1);
            state[0x10] = shift ? (Byte)0x80 : (Byte)0;
            state[0xA0] = state[0xA1] = state[0x10];
            state[0x14] = (Byte)(GetKeyState(0x14) & 1);
            state[key] |= 0x80;
            return TranslateText(key,state);
        }
        private static String TranslateText(Int32 key, Byte[] state)
        {
            StringBuilder text = new StringBuilder(16);
            Int32 count = ToUnicode((UInt32)key,MapVirtualKey((UInt32)key,0),state,text,text.Capacity,0);
            return count > 0 ? text.ToString(0,Math.Min(count,text.Length)) : "";
        }
        private String CurrentEntry(out Int32 limit, out Boolean numeric)
        {
            numeric = false;
            if (_page == Page.WorldEntrances) { limit = 512; return worldEntry; }
            if (_page == Page.SoundBrowser && _soundMode == SoundBrowserMode.AliasEntry) { limit = 80; return _soundAliasEntryText; }
            if (_page == Page.SoundBrowser) { limit = 60; return _soundFilterEntryText; }
            if (_page == Page.FieldState) { limit = 48; return _saveTitleEntryText; }
            numeric = true; limit = 8;
            if (_page == Page.Warp) return _warpEntryStage == 0 ? _warpFieldEntryText : _warpScenarioEntryText;
            return _assetIdEntryText;
        }
        private void SetCurrentEntry(String value)
        {
            if (_page == Page.WorldEntrances) { worldEntry = value; RenderWorldEntrances(); }
            else if (_page == Page.SoundBrowser)
            {
                if (_soundMode == SoundBrowserMode.AliasEntry) _soundAliasEntryText = value; else _soundFilterEntryText = value;
                RenderSoundBrowser();
            }
            else if (_page == Page.FieldState) { _saveTitleEntryText = value; RenderFieldState(); }
            else if (_page == Page.Warp)
            {
                if (_warpEntryStage == 0) _warpFieldEntryText = value; else _warpScenarioEntryText = value;
                RenderWarp();
            }
            else { _assetIdEntryText = value; RenderAssetDumper(); }
        }
        private Boolean HandleTextEntryKey(Int32 key)
        {
            Int32 limit; Boolean numeric;
            String current = CurrentEntry(out limit,out numeric);
            Boolean control = ModifierDown(0x11,0xA2,0xA3);
            if (key == 0x41 && control) SetCurrentEntry("");
            else if (key == VK_BACK || key == VK_DELETE)
            {
                if (current.Length > 0)
                {
                    Int32 remove = current.Length >= 2 && Char.IsLowSurrogate(current[current.Length - 1]) && Char.IsHighSurrogate(current[current.Length - 2]) ? 2 : 1;
                    SetCurrentEntry(current.Substring(0,current.Length - remove));
                }
            }
            else if (!control && !ModifierDown(0x12,0xA4,0xA5))
            {
                String typed = TypedText(key);
                foreach (Char ch in typed)
                    if (!Char.IsControl(ch) && (!numeric || (ch >= '0' && ch <= '9')) && current.Length < limit) current += ch;
                if (typed.Length > 0) SetCurrentEntry(current);
            }
            return true;
        }
        [DllImport("user32.dll")] private static extern Int16 GetAsyncKeyState(Int32 key);
        [DllImport("user32.dll")] private static extern UInt32 MapVirtualKey(UInt32 code, UInt32 mapType);
    }
}
