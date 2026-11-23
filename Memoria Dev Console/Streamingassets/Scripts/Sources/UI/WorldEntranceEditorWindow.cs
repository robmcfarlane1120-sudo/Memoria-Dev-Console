using System;
using System.Runtime.InteropServices;
using System.Text;
using Memoria.DevConsole.EntranceJson;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private const UInt32 WM_WORLD_SHOW = 0x8020;
        private const UInt32 WM_WORLD_VIEW = 0x8021;
        private volatile String worldViewJson = "{}";
        private Int32 worldRow;
        private Boolean worldAdvanced;
        private Boolean worldControlsHelp;
        private static readonly String[] WorldBasicRows = { "entranceTitle", "enabled", "destinationField", "destinationEntrance", "hwsReturn", "capture", "worldX", "worldZ", "radius", "targetMod", "installMod", "advanced" };
        private static readonly String[] WorldExpandedRows = { "entranceTitle", "enabled", "destinationField", "destinationEntrance", "hwsReturn", "capture", "worldX", "worldZ", "radius", "targetMod", "installMod", "advanced", "id", "worldMap", "scenarioMin", "scenarioMax", "condition", "deadPepperGate" };
        private String[] WorldVisibleRows { get { return worldAdvanced ? WorldExpandedRows : WorldBasicRows; } }
        private String worldEditing, worldEntry = "", worldConfirm;
        private Boolean worldChoosingTarget;
        private Int32 worldTargetIndex;
        public event Action<String, String> WorldEntranceAction;
        public Boolean IsWorldEntrancesActive { get { return _page == Page.WorldEntrances && IsVisible; } }

        public void ShowWorldEntrances() { if (_windowHandle != IntPtr.Zero) PostMessage(_windowHandle, WM_WORLD_SHOW, IntPtr.Zero, IntPtr.Zero); }
        public void SetWorldEntranceView(String json)
        {
            worldViewJson = json;
            if (_windowHandle != IntPtr.Zero) PostMessage(_windowHandle, WM_WORLD_VIEW, IntPtr.Zero, IntPtr.Zero);
        }
        private void WorldAction(String action, String value = "") { var handler = WorldEntranceAction; if (handler != null) handler(action, value); }
        private void OpenWorldEntrances()
        {
            worldEditing = null; worldConfirm = null;
            worldChoosingTarget=false;
            _page = Page.WorldEntrances; HideSaveThumbnail(); RenderWorldEntrances(); WorldAction("open");
        }
        private Boolean HandleWorldMessage(IntPtr hwnd, UInt32 msg)
        {
            if (msg == WM_WORLD_SHOW)
            {
                OpenWorldEntrances(); ShowWindow(hwnd, SW_RESTORE); ShowWindow(hwnd, SW_SHOW); SetForegroundWindow(hwnd); SetFocus(hwnd); return true;
            }
            if (msg == WM_WORLD_VIEW) { if (_page == Page.WorldEntrances) RenderWorldEntrances(); return true; }
            return false;
        }
        private Boolean HandleWorldKey(Int32 key)
        {
            if (key == 0x79) return false; // Keep MDC's registered F10 toggle available.
            if (key == 0x70 && worldEditing == null && worldConfirm == null && !worldChoosingTarget)
            { worldControlsHelp = !worldControlsHelp; RenderWorldEntrances(); return true; }
            if(worldChoosingTarget)
            {
                JArray mods=JObject.Parse(worldViewJson)["mods"] as JArray;
                if(key==VK_ESCAPE)worldChoosingTarget=false;
                else if(mods!=null && mods.Count>0)
                {
                    if(key==VK_UP||key==VK_DOWN)worldTargetIndex=WrapSelection(worldTargetIndex+(key==VK_UP?-1:1),mods.Count);
                    if(key==VK_RETURN){WorldAction("target",(String)mods[worldTargetIndex]);worldChoosingTarget=false;}
                }
                RenderWorldEntrances();return true;
            }
            if (worldConfirm != null)
            {
                if (key == 0x59) { WorldAction(worldConfirm); worldConfirm = null; }
                else if (key == VK_ESCAPE || key == 0x4E) worldConfirm = null;
                RenderWorldEntrances(); return true;
            }
            if (worldEditing != null)
            {
                if (key == VK_ESCAPE) worldEditing = null;
                else if (key == VK_RETURN) { WorldAction(worldEditing, worldEntry); worldEditing = null; }
                else if (key == VK_BACK && worldEntry.Length > 0) worldEntry = worldEntry.Substring(0, worldEntry.Length - 1);
                else if (key == 0x41 && (GetKeyState(VK_CONTROL) & 0x8000) != 0) worldEntry = "";
                else if ((GetKeyState(VK_CONTROL) & 0x8000) == 0)
                {
                    Byte[] keys = new Byte[256]; StringBuilder text = new StringBuilder(8);
                    if (GetKeyboardState(keys) && ToUnicode((UInt32)key, 0, keys, text, text.Capacity, 0) > 0)
                        foreach (Char ch in text.ToString()) if (!Char.IsControl(ch) && worldEntry.Length < 512) worldEntry += ch;
                }
                RenderWorldEntrances(); return true;
            }
            if (key == VK_ESCAPE) { RenderHome(); return true; }
            if (key == VK_UP || key == VK_DOWN) { worldRow = WrapSelection(worldRow + (key == VK_UP ? -1 : 1), WorldVisibleRows.Length); RenderWorldEntrances(); return true; }
            if (key == VK_LEFT || key == VK_RIGHT) { WorldAction(key == VK_LEFT ? "previous" : "next"); return true; }
            JObject view = JObject.Parse(worldViewJson);
            if (key == VK_RETURN)
            {
                String field = WorldVisibleRows[worldRow];
                if (field == "hwsReturn") { WorldAction("copyHwsReturn"); return true; }
                if (field == "targetMod")
                { worldChoosingTarget = true; worldTargetIndex = 0; RenderWorldEntrances(); return true; }
                if (field == "installMod")
                {
                    if (String.IsNullOrEmpty((String)view["targetMod"]))
                    { worldChoosingTarget = true; worldTargetIndex = 0; }
                    else worldConfirm = "install";
                    RenderWorldEntrances(); return true;
                }
                if (field == "advanced") { worldAdvanced = !worldAdvanced; RenderWorldEntrances(); return true; }
                if (field == "capture") { WorldAction("capture"); return true; }
                if (view["definition"] == null || view["definition"].Type == JTokenType.Null)
                { WorldAction("create"); return true; }
                if (field == "enabled" || field == "deadPepperGate")
                {
                    Boolean enabled = (Boolean?)view["definition"][field] ?? (field == "enabled");
                    WorldAction("set:" + field, enabled ? "false" : "true"); return true;
                }
                worldEditing = "set:" + field;
                JToken definition = view["definition"];
                worldEntry = definition == null || definition.Type == JTokenType.Null || definition[field] == null || definition[field].Type == JTokenType.Null ? "" : definition[field].ToString();
            }
            else if (key == 0x4E) WorldAction("create");
            else if (key == 0x41) { worldAdvanced = !worldAdvanced; worldRow = Math.Min(worldRow, WorldVisibleRows.Length - 1); }
            else if (key == 0x50) WorldAction("capture");
            else if (key == 0x54) WorldAction("test");
            else if (key == 0x53) WorldAction("save");
            else if (key == 0x4C) worldConfirm = "load";
            else if (key == 0x52) WorldAction("reload");
            else if (key == 0x44 || key == 0x58) worldConfirm = key == 0x44 ? "delete" : "deleteSaved";
            else if (key == 0x4A) WorldAction("copy");
            else if (key == 0x4F) WorldAction("coordinates");
            else if (key == 0x45) { worldEditing = "directory"; worldEntry = (String)view["directory"] ?? ""; }
            else if (key == 0x48) { worldEditing = "hotkey"; worldEntry = (String)view["hotkey"] ?? "F11"; }
            else if (key == 0x4D) { worldChoosingTarget=true; worldTargetIndex=0; }
            else if (key == 0x49) worldConfirm="install";
            RenderWorldEntrances(); return true;
        }

        private void RenderWorldEntrances()
        {
            JObject view = JObject.Parse(worldViewJson);
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - World Entrance Creator");
            var text = new StringBuilder();
            text.Append(DoubleRule()).Append("WORLD ENTRANCE CREATOR\r\n").Append(Rule());
            text.Append((String)view["live"]).Append("\r\n");
            text.Append("[Enter] Edit / Run   [N] New   [P] Capture   [T] Test   [S] Save\r\n");
            text.Append("[Up/Down] Select   [Left/Right] Entrance   [F1] All controls   [Esc] Home\r\n");
            if (worldControlsHelp)
            {
                text.Append("\r\nALL CONTROLS\r\n").Append(Rule());
                text.Append("BUILD                         FILES\r\n");
                text.Append("N  New entrance                S  Save selected entrance to JSON\r\n");
                text.Append("P  Capture current position    L  Load entrances (asks before replacing edits)\r\n");
                text.Append("T  Test and resume game        E  Change export folder\r\n");
                text.Append("A  Expand/collapse advanced    J  Copy selected entrance JSON\r\n");
                text.Append("H  Change capture hotkey       O  Copy world coordinates\r\n\r\n");
                text.Append("INSTALL / MANAGE\r\n");
                text.Append("M  Choose target mod           I  Install into target mod\r\n");
                text.Append("R  Reload temporary tests      D  Delete temporary entrance\r\n");
                text.Append("X  Delete saved entrance       F1 Close this help\r\n");
                text.Append("\r\nExport file:\r\n").Append((String)view["directory"]).Append("\\WorldEntrances.json\r\n");
            }
            if(worldChoosingTarget)
            {
                text.Append(Rule()).Append("SELECT TARGET MOD — Up/Down, Enter to select, Esc cancels\r\n");
                JArray mods=view["mods"] as JArray;
                if(mods!=null)for(Int32 i=0;i<mods.Count;i++)text.Append(MenuLine(i==worldTargetIndex,(String)mods[i]));
                SetConsoleText(text.ToString());return;
            }
            JArray entries = view["entries"] as JArray; Int32 selected = (Int32?)view["selection"] ?? 0;
            if (entries != null)
                for (Int32 i = Math.Max(0, selected - 3); i < Math.Min(entries.Count, selected + 5); i++)
                    text.Append(EntranceMenuLine(i == selected, (String)entries[i]["id"] + ((Boolean?)entries[i]["saved"] == true ? " [saved]" : " [temporary/edited]") + ((Boolean?)entries[i]["testing"] == true ? " [testing]" : "")));
            JToken d = view["definition"];
            Boolean hasDefinition = d != null && d.Type == JTokenType.Object;
            if (!hasDefinition)
            {
                text.Append("\r\nNo entrance selected. N / P: Create at current position   L: Load JSON\r\n");
                text.Append("Settings below are available after creating or loading an entrance.\r\n");
            }
            String[] rows = WorldVisibleRows;
            worldRow = Math.Min(worldRow, rows.Length - 1);
            for (Int32 i = 0; i < rows.Length; i++)
            {
                String property = rows[i];
                if (property == "entranceTitle") text.Append("\r\n").Append(CyanText("ENTRANCE")).Append("\r\n");
                if (property == "destinationField") text.Append("\r\n").Append(CyanText("DESTINATION" + (hasDefinition ? " - " + (String)view["fieldName"] : ""))).Append("\r\n");
                if (property == "capture") text.Append("\r\n").Append(CyanText("WORLD POSITION")).Append("\r\n");
                if (property == "hwsReturn")
                {
                    text.Append(MenuLine(i == worldRow, "Copy HWS Return Block (Enter)")); continue;
                }
                if (property == "targetMod")
                {
                    text.Append("\r\n").Append(CyanText("DESTINATION MOD")).Append("\r\n");
                    text.Append(MenuLine(i == worldRow, "Choose Mod".PadRight(23) + ((String)view["targetMod"] ?? "Not selected")));
                    continue;
                }
                if (property == "installMod")
                {
                    text.Append(MenuLine(i == worldRow, "Install to Mod".PadRight(23) +
                        (String.IsNullOrEmpty((String)view["targetMod"]) ? "Choose a mod first" : (String)view["targetMod"])));
                    continue;
                }
                if (property == "advanced")
                {
                    String advanced = MenuLine(i == worldRow, "[" + (worldAdvanced ? "-" : "+") + "] ADVANCED - Enter to " + (worldAdvanced ? "collapse" : "expand"));
                    text.Append("\r\n").Append(i == worldRow ? advanced : CyanText(advanced));
                    continue;
                }
                if (property == "capture")
                {
                    text.Append(MenuLine(i == worldRow, hasDefinition ? "Capture Current Position (P)" : "Create at Current Position (P)")); continue;
                }
                String value = !hasDefinition ? "[no entrance selected]" : d[property] == null ? "" : d[property].ToString();
                String label = GetWorldEntrancePropertyLabel(property);
                if ((property == "enabled" || property == "deadPepperGate") && hasDefinition) value = ((Boolean?)d[property] ?? (property == "enabled")) ? "Enabled" : "Disabled";
                text.Append(MenuLine(i == worldRow, label.PadRight(23) + value));
            }
            String selectedProperty = rows[worldRow];
            text.Append(Rule()).Append("HELP: ").Append(GetWorldEntrancePropertyLabel(selectedProperty)).Append("\r\n");
            text.Append(GetWorldEntrancePropertyHelp(selectedProperty, (String)view["hotkey"] ?? "F11")).Append("\r\n\r\n");
            text.Append("STATUS: ").Append(StatusText((String)view["status"] ?? String.Empty)).Append("\r\n\r\n");
            if (worldConfirm != null) text.Append("Confirm ").Append(worldConfirm).Append(worldConfirm=="install" ? " into "+((String)view["targetMod"] ?? "[select a mod first]")+"? Writes entrance JSON/runtime sources with small backups. Use Compile + Restart if required. " : "? ").Append("Y=yes, N/Esc=cancel.\r\n");
            else if (worldEditing != null) text.Append("EDIT ").Append(worldEditing).Append(": ").Append(worldEntry).Append("_\r\nEnter=apply   Ctrl+A=clear   Esc=cancel\r\n");
            else text.Append("[Enter] Edit / Run   [F1] All controls   [Esc] Home\r\n");
            SetConsoleText(text.ToString());
        }

        private static String GetWorldEntrancePropertyLabel(String property)
        {
            switch (property)
            {
                case "entranceTitle": return "Entrance Name";
                case "enabled": return "Enabled";
                case "destinationField": return "Destination Field";
                case "destinationEntrance": return "Arrival Entrance";
                case "hwsReturn": return "Copy HWS Return Block";
                case "capture": return "Capture Current Position";
                case "worldX": return "Position X";
                case "worldZ": return "Position Z";
                case "radius": return "Activation Radius";
                case "advanced": return "Advanced";
                case "targetMod": return "Choose Mod";
                case "installMod": return "Install to Mod";
                case "id": return "Internal ID";
                case "worldMap": return "World Map State";
                case "scenarioMin": return "Story Progress From";
                case "scenarioMax": return "Story Progress Through";
                case "condition": return "Custom Condition";
                case "deadPepperGate": return "Dead Pepper Gate";
                default: return property;
            }
        }

        private static String GetWorldEntrancePropertyHelp(String property, String hotkey)
        {
            switch (property)
            {
                case "hwsReturn":
                    return "Press Enter to copy this block for Hades Workshop:\r\n\r\n" +
                        WorldEntranceEditorService.HwsReturnBlock + "\r\n\r\n" +
                        "In the destination field, find the Region*_Range function for your desired physical exit.\r\n" +
                        "Paste AFTER its IsMovementEnabled guard and BEFORE CalculateExitPosition, ExitField,\r\n" +
                        "fades, movement/menu locks, or Field/WorldMap transition commands. Keep vanilla code below.\r\n" +
                        "Do not put this in Main_Init or set the flag to 1 yourself; custom entry arms it.\r\n" +
                        "Normal visits keep the original exit. Save/export your HWS field changes separately.";
                case "deadPepperGate":
                    return "Enter toggles Enabled / Disabled. Requires Choco to use a Dead Pepper inside the activation radius.\r\nUse the game's normal pepper controls. The entrance unlocks after the pepper effect ends and control returns.\r\nUnlock is saved per entrance ID. Dismount to enter afterward. Disabled bypasses the gate.";
                case "targetMod":
                    return "Choose which installed mod receives this entrance and its runtime.\r\nPress Enter to open the mod list; use Up/Down and Enter to select. No files change until installed.";
                case "installMod":
                    return "Write this entrance's JSON and generated runtime sources into the destination mod.\r\nNo DLL is built here. Use Home > Compile + Restart when runtime sources change.\r\nLive tests work immediately through MDC. Current installed runtimes can reload JSON-only changes.";
                case "capture":
                    return "Capture your current on-foot world-map position into this entrance.\r\nUpdates Position X, Position Z and World Map State together. Press Enter or P.";
                case "advanced":
                    return "Expand to edit Internal ID, World Map State, Story Progress Range and Custom Condition.\r\nThese values are preserved when collapsed. Enter or A expands/collapses this section.";
                case "id":
                    return "Unique internal name for this entrance; generated automatically when created.\r\n" +
                        "With MDC hidden, Ctrl+Shift+" + hotkey + " creates an entrance at your on-foot world position.\r\n" +
                        "N creates from this editor; P updates the selected entrance's position. H changes the capture hotkey.";
                case "worldX":
                    return "Horizontal X position on the world map, in game world units.\r\nP captures X, Z and world-map state from your current on-foot position.";
                case "worldZ":
                    return "Horizontal Z position on the world map, in game world units.\r\nThis is the second ground-plane coordinate, not height. P captures your current position.";
                case "radius":
                    return "Activation distance around the entrance's X/Z position, in world units.\r\nMust be greater than zero. Larger values let the player activate it from farther away.";
                case "destinationField":
                    return "Numeric field ID loaded when the player activates this entrance.\r\nThe resolved field name appears above the settings. The field must exist in the game's field table.";
                case "destinationEntrance":
                    return "Arrival value passed to the destination field's script to choose its spawn route.\r\n9999 is the usual world-arrival value; other values depend on that field's script.\r\nThis is an entrance number, not a coordinate.";
                case "worldMap":
                    return "World-map state in which this entrance is available; 0 allows any free-roam state.\r\nThis is a world-map ID, not a disc number. Creating or capturing sets it automatically.";
                case "scenarioMin":
                    return "Earliest ScenarioCounter value at which this entrance is available (inclusive).\r\nLeave blank for no lower limit. ScenarioCounter measures story progress, not the disc number.";
                case "scenarioMax":
                    return "Latest ScenarioCounter value at which this entrance is available (inclusive).\r\nLeave blank for no upper limit. Must be at least scenarioMin when both are set.";
                case "condition":
                    return "Optional named condition for a mod to handle; blank means no custom condition.\r\nMDC cannot evaluate named conditions and requires this to be blank for temporary testing.";
                case "enabled":
                    return "On enables this entrance; Off disables it without deleting its definition.\r\nPress Enter to toggle. World-map and story-progress restrictions still apply when enabled.";
                case "entranceTitle":
                    return "Vanilla-style location nameplate shown while the player is within activation range.\r\nLeave blank to hide it. Plain single-line text, up to 80 characters; no square brackets.\r\nThis is the world-map nameplate, not an artwork title card inside the destination field.";
                case "prompt":
                    return "Interaction text saved with the entrance definition (default: Enter).\r\nThe current runtime does not display this text, so changing it has no visible effect.";
                default: return String.Empty;
            }
        }

        [DllImport("user32.dll")] private static extern Boolean GetKeyboardState(Byte[] keys);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern Int32 ToUnicode(UInt32 key, UInt32 scan, Byte[] state, StringBuilder text, Int32 size, UInt32 flags);
    }
}
