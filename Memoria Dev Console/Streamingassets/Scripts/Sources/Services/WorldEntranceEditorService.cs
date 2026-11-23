using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Memoria.DevConsole.WorldEntrances;

using Memoria.DevConsole.EntranceJson;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed class WorldEntranceEditorService
    {
        private const String ConfigPath = "Memoria Dev Console/FF9_Data/EntranceEditor/WorldEntranceEditor.json";
        internal const String HwsReturnBlock = "if ( MemoriaDictionary(988, 0) == 1 ) {\r\n    set MemoriaDictionary(988, 0) = 0\r\n    return\r\n}";
        private readonly NativeConsoleWindow window;
        private readonly Queue<Action> requests = new Queue<Action>();
        private readonly List<JObject> definitions = new List<JObject>();
        private readonly HashSet<String> saved = new HashSet<String>();
        private readonly HashSet<String> testIds = new HashSet<String>();
        private readonly HashSet<JObject> dirty = new HashSet<JObject>();
        private Int32 selection;
        private String exportDirectory = Path.GetFullPath("Memoria Dev Console/FF9_Data/EntranceEditor/Tests");
        private KeyCode placeKey = KeyCode.F11;
        private String status = "Create at your current on-foot overworld position, then configure a destination.";
        private String targetMod;
        private Single nextRefresh;
        public static readonly String[] Fields = { "id", "worldX", "worldZ", "radius", "destinationField", "destinationEntrance", "worldMap", "scenarioMin", "scenarioMax", "condition", "enabled", "deadPepperGate", "entranceTitle", "prompt" };

        public WorldEntranceEditorService(NativeConsoleWindow window)
        {
            this.window = window;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
                    if (config["exportDirectory"] != null)
                    {
                        String configured = Path.GetFullPath((String)config["exportDirectory"]);
                        String legacy = Path.GetFullPath("Memoria Dev Console/FF9_Data/CustomWorldMapEntrances");
                        // Keep intentional custom folders; retire only the old MDC default.
                        if (!String.Equals(configured.TrimEnd(Path.DirectorySeparatorChar), legacy.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                            exportDirectory = configured;
                    }
                    if (config["placeKey"] != null) placeKey = ParseKey((String)config["placeKey"]);
                }
            }
            catch (Exception ex) { status = "Editor configuration: " + ex.Message; }
            window.WorldEntranceAction += Enqueue;
        }

        private static KeyCode ParseKey(String name)
        {
            KeyCode key = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
            if (key < KeyCode.F1 || key > KeyCode.F12 || key == KeyCode.F10 || key == KeyCode.F12) throw new ArgumentException("Use F1–F9 or F11; F10 is MDC and F12 is the test return key.");
            return key;
        }

        private void Enqueue(String action, String value)
        { lock (requests) requests.Enqueue(() => Process(action, value)); }

        public void Update()
        {
            Action action = null;
            lock (requests) if (requests.Count != 0) action = requests.Dequeue();
            if (action != null) { try { action(); } catch (Exception ex) { status = ex.Message; } Publish(); }
            if (!window.IsVisible && Input.GetKeyDown(placeKey) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                try { Create(); window.ShowWorldEntrances(); } catch (Exception ex) { status = ex.Message; window.ShowWorldEntrances(); }
                Publish();
            }
            if (window.IsWorldEntrancesActive && Time.realtimeSinceStartup >= nextRefresh)
            { nextRefresh = Time.realtimeSinceStartup + 0.25f; Publish(); }
        }

        private JObject Current { get { return definitions.Count == 0 ? null : definitions[selection]; } }
        private String FilePath { get { return Path.Combine(exportDirectory, "WorldEntrances.json"); } }
        private static Vector3 Position()
        {
            String reason = WorldEntranceController.CaptureBlockReason();
            if (reason != null) throw new InvalidOperationException("Capture blocked: " + reason);
            return ff9.GetControlChar().RealPosition;
        }

        private void Create()
        {
            Vector3 p = Position();
            String id = "Entrance_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            definitions.Add(new JObject(new JProperty("id", id), new JProperty("worldX", p.x), new JProperty("worldZ", p.z), new JProperty("radius", 4),
                new JProperty("destinationField", 2800), new JProperty("destinationEntrance", 9999), new JProperty("worldMap", FF9StateSystem.Common.FF9.wldMapNo),
                new JProperty("scenarioMin", null), new JProperty("scenarioMax", null), new JProperty("condition", ""), new JProperty("enabled", true), new JProperty("deadPepperGate", false), new JProperty("entranceTitle", ""), new JProperty("prompt", "Enter")));
            selection = definitions.Count - 1;
            dirty.Add(Current);
            status = "Temporary definition captured. Test applies it to MDC's standalone runtime.";
        }

        private void Process(String action, String value)
        {
            if (action == "copyHwsReturn")
            {
                GUIUtility.systemCopyBuffer = HwsReturnBlock;
                status = "HWS block copied. Paste into the Region_Range function for your desired field exit, after its movement guard and BEFORE vanilla exit actions. Keep original code below.";
                return;
            }
            if (action == "open") { Publish(); return; }
            if (WorldEntranceTransition.IsBusy) throw new InvalidOperationException("Wait for the entrance transition to finish.");
            if (action == "create") { Create(); return; }
            if (action == "capture" && Current == null) { Create(); return; }
            if (action == "target") { WorldEntranceInstaller.Target(value); targetMod=value; status="Selected target mod: "+value+". No files written. I installs the selected entrance after confirmation."; return; }
            if (action == "directory")
            {
                String path = Path.GetFullPath(value);
                if (File.Exists(path)) throw new ArgumentException("Choose a directory, not a file.");
                exportDirectory = path; PersistConfig(); saved.Clear(); status = "Export directory changed. Load to read its definitions, or Save to merge the selected entrance."; return;
            }
            if (action == "hotkey") { placeKey = ParseKey(value); PersistConfig(); status = "Placement hotkey: Ctrl+Shift+" + placeKey; return; }
            if (action == "load")
            {
                JObject root = EntranceJsonStore.Read(FilePath);
                var loaded = ((JArray)root["entrances"]).OfType<JObject>().Select(j => (JObject)j.DeepClone()).ToList();
                foreach (JObject j in loaded) Validate(j, false);
                ClearTests(); definitions.Clear(); definitions.AddRange(loaded); saved.Clear(); dirty.Clear(); origins.Clear(); foreach (var j in loaded) { saved.Add((String)j["id"]); origins[j] = (String)j["id"]; }
                selection = 0; status = "Loaded exported definitions. Temporary tests cleared."; return;
            }
            if (action == "selectIndex")
            {
                Int32 requested;
                if (Int32.TryParse(value, out requested) && requested >= 0 && requested < definitions.Count) selection = requested;
                return;
            }
            if (action == "previous" || action == "next")
            { if (definitions.Count > 0) selection = (selection + definitions.Count + (action == "next" ? 1 : -1)) % definitions.Count; return; }
            if (Current == null) throw new InvalidOperationException("Create or load an entrance first.");
            String id = (String)Current["id"];
            if (action == "install")
            {
                if(String.IsNullOrEmpty(targetMod))throw new InvalidOperationException("Choose a target mod with M before installing.");
                Boolean needsCompile=WorldEntranceInstaller.Install(targetMod,Current);
                status=needsCompile ? "Installed entrance JSON and runtime sources into "+targetMod+". Use Home > Compile + Restart to build and load them. No DLL was changed; live tests remain available." : "Updated "+targetMod+" entrance JSON; existing runtime will reload it. No rebuild needed.";
                return;
            }
            if (action.StartsWith("set:", StringComparison.Ordinal))
            {
                String field = action.Substring(4);
                if (!Fields.Contains(field)) throw new ArgumentException("Unknown property.");
                JObject changed = (JObject)Current.DeepClone();
                if (field == "worldX" || field == "worldZ" || field == "radius") changed[field] = Single.Parse(value, CultureInfo.InvariantCulture);
                else if (field == "enabled" || field == "deadPepperGate") changed[field] = Boolean.Parse(value);
                else if (field == "destinationField" || field == "destinationEntrance" || field == "worldMap") changed[field] = Int32.Parse(value, CultureInfo.InvariantCulture);
                else if (field == "scenarioMin" || field == "scenarioMax") changed[field] = String.IsNullOrEmpty((value ?? "").Trim()) ? JValue.CreateNull() : new JValue(Int32.Parse(value, CultureInfo.InvariantCulture));
                else changed[field] = value;
                Validate(changed, false);
                if (definitions.Where((j, i) => i != selection).Any(j => (String)j["id"] == (String)changed["id"])) throw new ArgumentException("Duplicate entrance ID.");
                String origin = origins.ContainsKey(Current) ? origins[Current] : id;
                RemoveTest(id); definitions[selection] = changed;
                // Track saved origin separately so an ID rename replaces only its own record.
                origins[changed] = origin;
                dirty.Add(changed);
                status = "Edited; Test or Save to apply. Existing test removed."; return;
            }
            if (action == "capture")
            { Vector3 p = Position(); RemoveTest(id); Current["worldX"] = p.x; Current["worldZ"] = p.z; Current["worldMap"] = FF9StateSystem.Common.FF9.wldMapNo; dirty.Add(Current); status = "Position updated; Test to apply."; return; }
            if (action == "test") { Test(Current); status = "MDC test active. Use Confirm or Ctrl+Shift+F12 in range; same shortcut returns from the destination entrance screen."; window.Hide(); return; }
            if (action == "reload") { foreach (JObject j in definitions) Validate(j, true); ClearTests(); foreach (JObject j in definitions) Test(j); status = "All editor definitions reloaded into MDC's test runtime."; return; }
            if (action == "save")
            {
                Validate(Current, false);
                JObject root = EntranceJsonStore.Read(FilePath);
                String original = origins.ContainsKey(Current) ? origins[Current] : id;
                EntranceJsonStore.Merge(root, Current, saved.Contains(original) ? original : null);
                Directory.CreateDirectory(exportDirectory);
                EntranceJsonStore.Write(FilePath, root);
                saved.Remove(original); saved.Add(id); origins[Current] = id;
                dirty.Remove(Current);
                status = "Exported selected entrance to " + FilePath; return;
            }
            if (action == "delete" || action == "deleteSaved")
            {
                if (action == "deleteSaved")
                {
                    JObject root = EntranceJsonStore.Read(FilePath);
                    String original = origins.ContainsKey(Current) ? origins[Current] : id;
                    if (!saved.Contains(original)) throw new InvalidOperationException("This selection has no loaded saved definition. Load the file before deleting from disk.");
                    foreach (var j in ((JArray)root["entrances"]).OfType<JObject>().Where(j => (String)j["id"] == original).ToArray()) j.Remove();
                    EntranceJsonStore.Write(FilePath, root); saved.Remove(original);
                }
                RemoveTest(id); definitions.RemoveAt(selection); selection = Math.Max(0, Math.Min(selection, definitions.Count - 1)); status = "Entrance removed."; return;
            }
            if (action == "copy") { GUIUtility.systemCopyBuffer = Current.ToString(Formatting.Indented); status = "Definition copied."; return; }
            if (action == "coordinates") { GUIUtility.systemCopyBuffer = Current["worldX"] + ", " + Current["worldZ"]; status = "Coordinates copied."; return; }
        }

        private readonly Dictionary<JObject, String> origins = new Dictionary<JObject, String>();
        private void PersistConfig()
        {
            EntranceJsonStore.Write(ConfigPath, new JObject(new JProperty("exportDirectory", exportDirectory), new JProperty("placeKey", placeKey.ToString())));
        }
        private void RemoveTest(String id) { WorldEntranceRegistry.Unregister(id); testIds.Remove(id); }
        private void ClearTests() { foreach (String id in testIds) WorldEntranceRegistry.Unregister(id); testIds.Clear(); }
        private void Test(JObject j)
        {
            Validate(j, true);
            if (WorldEntranceController.Instance == null)
            {
                var host = new GameObject("MDC_WorldEntranceTests"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<WorldEntranceController>();
            }
            String id = (String)j["id"]; JObject d = (JObject)j.DeepClone();
            var entrance = new WorldEntrance { Id = id, WorldX = (Single)d["worldX"], WorldZ = (Single)d["worldZ"], InteractionRadius = (Single)d["radius"],
                WorldMap = (Int16?)d["worldMap"] ?? 0, DestinationField = (Int16)d["destinationField"], DestinationEntrance = (Int32?)d["destinationEntrance"] ?? 9999,
                Prompt = (String)d["prompt"] ?? "Enter", EntranceTitle = (String)d["entranceTitle"] ?? "", DeadPepperGate = (Boolean?)d["deadPepperGate"] ?? false, IsEditorTest = true };
            entrance.IsEnabled = () => ((Boolean?)d["enabled"] ?? true) &&
                (d["scenarioMin"] == null || d["scenarioMin"].Type == JTokenType.Null || FF9StateSystem.EventState.ScenarioCounter >= (Int32)d["scenarioMin"]) &&
                (d["scenarioMax"] == null || d["scenarioMax"].Type == JTokenType.Null || FF9StateSystem.EventState.ScenarioCounter <= (Int32)d["scenarioMax"]);
            WorldEntranceRegistry.ReplaceManaged(new[] { id }, new[] { entrance }); testIds.Add(id);
        }

        internal static void Validate(JObject j, Boolean test)
        {
            EntranceJsonStore.Validate(j);
            Int32 fieldId = (Int32)j["destinationField"];
            Type utils = typeof(EventEngine).Assembly.GetType("EventEngineUtils");
            var field = utils.GetField("eventIDToFBGID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var mapping = field.GetValue(null) as IDictionary;
            if (mapping == null || !mapping.Contains(fieldId)) throw new ArgumentException("Invalid destination field ID: " + fieldId);
            if (test && !String.IsNullOrEmpty((String)j["condition"])) throw new ArgumentException("Named mod conditions are portable metadata; MDC cannot evaluate them. Clear the condition for a temporary test.");
        }

        private void Publish()
        {
            var rows = new JArray();
            foreach (JObject d in definitions)
                rows.Add(new JObject(new JProperty("id", (String)d["id"]), new JProperty("saved", saved.Contains((String)d["id"]) && !dirty.Contains(d)), new JProperty("testing", testIds.Contains((String)d["id"]))));
            var view = new JObject(new JProperty("status", status), new JProperty("directory", exportDirectory), new JProperty("hotkey", placeKey.ToString()),
                new JProperty("entries", rows), new JProperty("selection", selection), new JProperty("definition", Current == null ? null : Current.DeepClone()));
            view["targetMod"]=targetMod;
            view["mods"]=new JArray(WorldEntranceInstaller.Mods());
            if (PersistenSingleton<FF9StateSystem>.Instance.mode == 3 && ff9.GetControlChar() != null)
            { Vector3 p = ff9.GetControlChar().RealPosition; view["live"] = String.Format(CultureInfo.InvariantCulture, "X {0:0.000}   Z {1:0.000}   World {2}", p.x, p.z, FF9StateSystem.Common.FF9.wldMapNo); }
            else view["live"] = "Not on the overworld.";
            if (PersistenSingleton<FF9StateSystem>.Instance.mode == 3 && ff9.GetControlChar() != null)
            {
                WMActor actor = ff9.GetControlChar();
                String reason = WorldEntranceController.CaptureBlockReason();
                String tag = actor.originalActor == null ? "unknown" : ff9.m_GetIDEvent(ff9.w_moveCHRStatus[actor.originalActor.index].id).ToString();
                view["live"] = (String)view["live"] + "\r\nCapture: " + (reason ?? "Ready") + "   Ground event tag: " + tag;
            }
            Int32 fieldId = Current == null ? -1 : (Int32)Current["destinationField"];
            view["fieldName"] = FF9DBAll.EventDB.ContainsKey(fieldId) ? FF9DBAll.EventDB[fieldId] : "Unknown field";
            window.SetWorldEntranceView(view.ToString(Formatting.None));
        }
    }

    internal static class EntranceJsonStore
    {
        internal static JObject Read(String path)
        {
            if (!File.Exists(path)) return new JObject(new JProperty("schemaVersion", 1), new JProperty("entrances", new JArray()));
            JObject root = JObject.Parse(File.ReadAllText(path));
            if ((Int32?)root["schemaVersion"] != 1 || !(root["entrances"] is JArray)) throw new ArgumentException("Expected schemaVersion 1 and an entrances array.");
            var ids = new HashSet<String>(StringComparer.Ordinal);
            foreach (JToken token in (JArray)root["entrances"])
            { if (!(token is JObject)) throw new ArgumentException("Each entrance must be an object."); Validate((JObject)token); if (!ids.Add((String)token["id"])) throw new ArgumentException("Duplicate ID in file."); }
            return root;
        }
        internal static void Validate(JObject j)
        {
            if (j["id"] == null || j["id"].Type != JTokenType.String) throw new ArgumentException("ID must be a string.");
            if (String.IsNullOrEmpty(((String)j["id"] ?? "").Trim()) || ((String)j["id"]).Length > 100) throw new ArgumentException("ID must contain 1–100 characters.");
            foreach (String property in new[] { "worldX", "worldZ", "radius" })
            { if (j[property] == null || (j[property].Type != JTokenType.Float && j[property].Type != JTokenType.Integer)) throw new ArgumentException("Missing or nonnumeric " + property); Single f = (Single)j[property]; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new ArgumentException("Nonfinite " + property); }
            foreach (String property in new[] { "destinationField", "destinationEntrance", "worldMap", "scenarioMin", "scenarioMax" })
                if (j[property] != null && j[property].Type != JTokenType.Null && j[property].Type != JTokenType.Integer) throw new ArgumentException(property + " must be an integer.");
            foreach (String property in new[] { "condition", "prompt", "entranceTitle" })
                if (j[property] != null && j[property].Type != JTokenType.Null && j[property].Type != JTokenType.String) throw new ArgumentException(property + " must be a string.");
            String title = (String)j["entranceTitle"] ?? "";
            if (title.Length > 80 || title.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0) throw new ArgumentException("Entrance title must be plain single-line text, at most 80 characters.");
            if ((Single)j["radius"] <= 0 || (Single)j["radius"] > 128) throw new ArgumentException("Radius must be greater than zero and at most 128.");
            Int32 field = (Int32?)j["destinationField"] ?? -1, entrance = (Int32?)j["destinationEntrance"] ?? 9999, world = (Int32?)j["worldMap"] ?? 0;
            if (field < 0 || field >= 9000 || entrance < 0 || entrance > 65535) throw new ArgumentException("Invalid field/entrance number.");
            if (entrance == 65534) throw new ArgumentException("65534 (-2) is reserved for field-script return requests; use an arrival parameter such as 9999.");
            if (world != 0 && (world < 9000 || world > 9012 || world == 9001 || world == 9004 || world == 9006 || world == 9012)) throw new ArgumentException("World must be 0 or a free-roam state in 9000–9012.");
            Int32? min = (Int32?)j["scenarioMin"], max = (Int32?)j["scenarioMax"];
            if (min < 0 || min > 65535 || max < 0 || max > 65535 || (min.HasValue && max.HasValue && min > max)) throw new ArgumentException("Invalid scenario limits.");
            if (j["enabled"] != null && j["enabled"].Type != JTokenType.Boolean) throw new ArgumentException("Enabled must be true or false.");
            if (j["deadPepperGate"] != null && j["deadPepperGate"].Type != JTokenType.Boolean) throw new ArgumentException("Dead Pepper Gate must be true or false.");
        }
        internal static void Merge(JObject root, JObject definition, String originalId)
        {
            Validate(definition); JArray entries = (JArray)root["entrances"];
            JObject old = entries.OfType<JObject>().FirstOrDefault(j => (String)j["id"] == originalId);
            if (entries.OfType<JObject>().Any(j => (String)j["id"] == (String)definition["id"] && j != old)) throw new ArgumentException("Duplicate saved entrance ID.");
            if (old == null) entries.Add(definition.DeepClone());
            else foreach (JProperty p in definition.Properties()) old[p.Name] = p.Value.DeepClone();
        }
        internal static void Write(String path, JObject root)
        {
            path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); String temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) { writer.Write(root.ToString(Formatting.Indented)); writer.Flush(); stream.Flush(); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
