using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Memoria.BattleSceneLayouts.Json;

namespace Memoria.DevConsole
{
    public sealed partial class NativeConsoleWindow
    {
        private const UInt32 WM_BATTLE_VIEW = 0x8025;
        private volatile String battleView = "{}";
        private Int32 battleTab, battleRow;
        private Single battleStep = 100;
        private String battleEditing, battleEntry = "";
        private static readonly String[][] BattleRows = {
            new[] { "tab", "cameraEnabled", "freeze", "orbitLock", "orbitAngle", "orbitElevation", "cameraX", "cameraY", "cameraZ", "angleX", "angleY", "angleZ", "fov", "sfxZoom", "captureCamera", "resetCamera", "saveCamera", "release" },
            new[] { "tab", "unit", "unitX", "unitY", "unitZ", "unitRotationX", "unitRotationY", "unitRotationZ", "unitScale", "stageScale", "partyX", "partyY", "partyZ", "capture", "reset" },
            new[] { "tab", "name", "target", "preset", "captureInstall", "save", "load", "reset", "export" }
        };
        public event Action<String, String> BattleSceneAction;
        public Boolean IsBattleSceneEditorActive { get { return _page == Page.BattleSceneEditor && IsVisible; } }
        public void SetBattleSceneView(String json)
        { battleView = json; if (_windowHandle != IntPtr.Zero) PostMessage(_windowHandle, WM_BATTLE_VIEW, IntPtr.Zero, IntPtr.Zero); }
        private void BattleAction(String action, String value = "") { var handler = BattleSceneAction; if (handler != null) handler(action, value); }
        private void OpenBattleSceneEditor()
        { _page = Page.BattleSceneEditor; battleEditing = null; HideSaveThumbnail(); RenderBattleScene(); BattleAction("open"); }
        private Boolean HandleBattleMessage(UInt32 msg)
        { if (msg != WM_BATTLE_VIEW) return false; if (_page == Page.BattleSceneEditor) RenderBattleScene(); return true; }
        private Boolean HandleBattleKey(Int32 key)
        {
            if (key == 0x79) return false;
            if (battleEditing != null)
            {
                if (key == VK_ESCAPE) battleEditing = null;
                else if (key == VK_RETURN) { BattleAction(battleEditing, battleEntry); battleEditing = null; }
                else if (key == VK_BACK && battleEntry.Length > 0) battleEntry = battleEntry.Substring(0, battleEntry.Length - 1);
                else if (key == VK_A && ModifierDown(0x11, 0xA2, 0xA3)) battleEntry = "";
                else if (!ModifierDown(0x11, 0xA2, 0xA3) && !TextModifier(key))
                {
                    foreach (Char ch in TypedText(key)) if (!Char.IsControl(ch) && battleEntry.Length < 60) battleEntry += ch;
                }
                RenderBattleScene(); return true;
            }
            if (key == VK_ESCAPE) { RenderHome(); return true; }
            if (key == 0x09) { battleTab = (battleTab + 1) % 3; battleRow = 0; RenderBattleScene(); return true; }
            if (key == 0xBB || key == 0x6B) { battleStep = Math.Min(1000, battleStep * 10); RenderBattleScene(); return true; }
            if (key == 0xBD || key == 0x6D) { battleStep = Math.Max(0.1f, battleStep / 10); RenderBattleScene(); return true; }
            String[] rows = BattleRows[battleTab];
            if (key == VK_UP || key == VK_DOWN) { battleRow = WrapSelection(battleRow + (key == VK_UP ? -1 : 1), rows.Length); RenderBattleScene(); return true; }
            String field = rows[battleRow];
            if (field == "tab" && (key == VK_RETURN || key == VK_LEFT || key == VK_RIGHT))
            { battleTab = (battleTab + (key == VK_LEFT ? 2 : 1)) % 3; battleRow = 0; RenderBattleScene(); return true; }
            Boolean degrees = field == "orbitAngle" || field == "orbitElevation" || field == "fov" || field.StartsWith("angle") || field.StartsWith("unitRotation");
            Boolean numeric = degrees || field.StartsWith("camera") && field != "cameraEnabled" || field.StartsWith("party") || field == "unitX" || field == "unitY" || field == "unitZ" || field == "stageScale" || field == "unitScale" || field == "sfxZoom";
            if (key == VK_LEFT || key == VK_RIGHT)
            {
                if (field == "unit" || field == "preset" || field == "target") BattleAction(field + (key == VK_LEFT ? "Previous" : "Next"));
                else if (numeric) BattleAction("delta:" + field, ((key == VK_LEFT ? -1 : 1) * (degrees || field == "stageScale" || field == "unitScale" || field == "sfxZoom" ? Math.Min(5, battleStep) : battleStep)).ToString(CultureInfo.InvariantCulture));
                return true;
            }
            if (key == VK_RETURN)
            {
                if (numeric || field == "name") { battleEditing = field; battleEntry = BattleValue(field, JObject.Parse(battleView)); }
                else if (field == "unit") BattleAction("unitNext");
                else if (field == "preset") BattleAction("presetNext");
                else if (field == "target") BattleAction("targetNext");
                else BattleAction(field);
                RenderBattleScene(); return true;
            }
            return true;
        }
        private static String BattleValue(String field, JObject view)
        {
            if (field == "name") return (String)view["name"] ?? "Layout";
            if (field == "preset") return (String)view["preset"] ?? "None saved";
            if (field == "orbitAngle" || field == "orbitElevation") return view[field] == null ? "0" : view[field].ToString();
            if (field == "target") return (String)view["targetMod"] ?? "Select a mod";
            if (field.StartsWith("party")) return view["partyShift"] == null ? "0" : view["partyShift"][field[field.Length - 1] - 'X'].ToString();
            JObject layout = view["layout"] as JObject; if (layout == null) return "";
            JObject cam = layout["camera"] as JObject;
            if (field == "cameraEnabled") return (Boolean)cam["enabled"] && ((Boolean?)view["cameraBound"] ?? false) ? "Enabled" : "Native control";
            if (field == "freeze") return ((Boolean?)view["freeze"] ?? false) ? "ON (all camera phases)" : "OFF (idle views only)";
            if (field == "sfxZoom") return (((Single?)cam["sfxCameraZoom"] ?? 1f) * 100f).ToString("0.##", CultureInfo.InvariantCulture);
            if (field == "fov") return cam["verticalFov"].ToString();
            if (field.StartsWith("camera") || field.StartsWith("angle"))
                return cam[field.StartsWith("angle") ? "angleOffsets" : "position"][field[field.Length - 1] - 'X'].ToString();
            if (field == "stageScale") return (((Single?)layout["stageScale"] ?? 1f) * 100f).ToString("0.##", CultureInfo.InvariantCulture);
            if (!field.StartsWith("unit")) return "Enter";
            JArray units = layout["units"] as JArray; Int32 index = (Int32?)view["unitIndex"] ?? 0;
            if (units == null || index < 0 || index >= units.Count) return "No units captured";
            JToken unit = units[index];
            if (field.StartsWith("unitRotation"))
            {
                JToken angles = unit["rotation"] ?? view["unitRotation"];
                return angles == null ? "0" : angles[field[field.Length - 1] - 'X'].ToString();
            }
            if (field == "unitScale") return (((Single?)unit["scale"] ?? 1f) * 100f).ToString("0.##", CultureInfo.InvariantCulture);
            if (field == "unit") return ((Boolean)unit["party"] ? "Party " : "Enemy ") + unit["slot"] + " | " + (String)unit["name"];
            if (field.StartsWith("unit")) return unit["home"][field[field.Length - 1] - 'X'].ToString();
            return "Enter";
        }
        private void RenderBattleScene()
        {
            SetWindowText(_windowHandle, "Memoria Dev Console v1.4.1 - Battle Scene Editor");
            JObject view = JObject.Parse(battleView); JObject layout = view["layout"] as JObject;
            String[] tabs = { "CAMERA", "FORMATION", "PRESETS / EXPORT" };
            StringBuilder text = new StringBuilder();
            text.Append(DoubleRule()).Append(CyanText(" BATTLE SCENE EDITOR")).Append("\r\n").Append(DoubleRule());
            text.Append(layout == null ? "No active battle. Enter a battle and wait for it to finish loading.\r\n" : "Battle " + layout["battleId"] + " | Pattern " + layout["pattern"] + " | Camera freeze and formation home-position editor\r\n");
            String[] rows = BattleRows[battleTab];
            for (Int32 i = 0; i < rows.Length; i++)
            {
                String field = rows[i]; String value = field == "tab" ? tabs[battleTab] + "  [Tab / Left / Right]" : BattleValue(field, view);
                text.Append(MenuLine(battleRow == i, BattleLabel(field).PadRight(26) + value));
            }
            if (battleTab == 1 && layout != null)
            {
                text.Append("\r\nLIVE SLOTS (battle mask / visibility / HP)\r\n");
                JArray live = view["live"] as JArray;
                if (live != null) foreach (JToken unit in live) text.Append("  ").Append(unit["key"]).Append(" ").Append((String)unit["name"]).Append(" | ").Append(unit["battleMask"]).Append(" | ").Append((Boolean)unit["hidden"] ? "hidden" : "visible").Append(" | HP ").Append(unit["hp"]).Append("\r\n");
            }
            text.Append("\r\n").Append(Rule()).Append("Arrow step: ").Append(battleStep.ToString(CultureInfo.InvariantCulture)).Append("  [+/- changes step]\r\n");
            text.Append("XYZ uses battle world coordinates. Angles are offsets from the captured PSX basis.\r\n");
            text.Append("Whole-party XYZ translates all present members together, preserving formation.\r\n");
            if (battleTab == 2) text.Append("Capture & Install uses this battle ID/pattern and the selected target mod.\r\n");
            text.Append("STATUS: ").Append(StatusText((String)view["status"] ?? "Waiting for battle...")).Append("\r\n");
            if (battleEditing != null) text.Append("EDIT ").Append(BattleLabel(battleEditing)).Append(": ").Append(battleEntry).Append("_  Enter=apply Ctrl+A=clear Esc=cancel\r\n");
            else text.Append("Up/Down: Select | Left/Right: Adjust | Enter: Exact value / Run | Tab: Section\r\nEsc: Home | F10: Hide | Loaded presets stay active for this battle\r\n");
            SetConsoleText(text.ToString());
        }
        private static String BattleLabel(String field)
        {
            switch (field)
            {
                case "tab": return "Section"; case "cameraEnabled": return "Camera override"; case "freeze": return "Freeze Camera";
                case "orbitLock": return "Lock Orbit Center"; case "orbitAngle": return "Orbit Angle (degrees)";
                case "orbitElevation": return "Orbit Elevation (degrees)";
                case "cameraX": return "Camera X"; case "cameraY": return "Camera Y"; case "cameraZ": return "Camera Z";
                case "angleX": return "Pitch offset (degrees)"; case "angleY": return "Yaw offset (degrees)"; case "angleZ": return "Roll offset (degrees)";
                case "sfxZoom": return "SFX Camera Zoom Out (%)"; case "fov": return "Vertical FOV (degrees)"; case "unit": return "Selected unit";
                case "unitX": return "Home X"; case "unitY": return "Home Y"; case "unitZ": return "Home Z";
                case "stageScale": return "Stage Scale (%)";
                case "unitRotationX": return "Model Pitch X (degrees)";
                case "unitRotationY": return "Model Facing Y (degrees)";
                case "unitRotationZ": return "Model Roll Z (degrees)";
                case "unitScale": return "Model Scale (%)";
                case "captureCamera": return "Capture Current Camera"; case "resetCamera": return "Reset Camera"; case "saveCamera": return "Save Camera Preset";
                case "release": return "Release Camera"; case "capture": return "Refresh Live Layout"; case "reset": return "Reset to Original";
                case "partyX": return "Move Whole Party X"; case "partyY": return "Move Whole Party Y"; case "partyZ": return "Move Whole Party Z";
                case "target": return "Target Mod"; case "captureInstall": return "Capture & Install to Mod";
                case "name": return "Preset name"; case "preset": return "Select saved preset"; case "save": return "Save Layout"; case "load": return "Load Layout"; case "export": return "Export Standalone Mod";
            }
            return field;
        }
    }
}
