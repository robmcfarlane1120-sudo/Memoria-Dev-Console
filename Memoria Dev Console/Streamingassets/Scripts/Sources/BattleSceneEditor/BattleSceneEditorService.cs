using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Memoria.BattleSceneLayouts;
using Memoria.BattleSceneLayouts.Json;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed class BattleSceneEditorService : IDisposable
    {
        private readonly NativeConsoleWindow window;
        private readonly Queue<Action> requests = new Queue<Action>();
        private readonly String root = Path.GetFullPath("Memoria Dev Console/BattleSceneEditor");
        private BattleLayout original, layout;
        private BattleLayoutCameraHook hook;
        private BTL_SCENE scene;
        private Int32 pattern = -1;
        private Boolean pending, wasOpen, keepLoadedPreset;
        private readonly HashSet<String> pendingUnits = new HashSet<String>();
        private Boolean pendingPartyGroup;
        private Vector3 partyShift;
        private LayoutCamera orbitPose;
        private Matrix4x4 orbitBase;
        private Vector3 orbitCenter;
        private Single orbitAngle;
        private Single orbitElevation;
        private Single orbitFov;
        private String targetMod;
        private String status = "Open during an active battle.", presetName = "Layout";
        private Int32 unitIndex, presetIndex;
        private Single nextRefresh;
        public BattleSceneEditorService(NativeConsoleWindow window)
        {
            this.window = window; window.BattleSceneAction += Enqueue;
            BattleLayoutRuntime.InstallHooks();
            String[] mods = WorldEntranceInstaller.Mods();
            targetMod = mods.Contains("The Lost Chapters") ? "The Lost Chapters" : mods.FirstOrDefault();
        }
        private void Enqueue(String action, String value) { lock (requests) requests.Enqueue(() => Process(action, value)); }
        public void Dispose() { window.BattleSceneAction -= Enqueue; if (hook != null) hook.Release(); }
        public void Update()
        {
            try { Tick(); }
            catch (Exception ex)
            {
                if (hook != null) hook.Release();
                status = "Camera released after editor error: " + ex.Message;
                window.SetBattleSceneView(new JObject(new JProperty("ready", false), new JProperty("status", status)).ToString());
            }
        }
        private void Tick()
        {
            Boolean ready = BattleLayoutRuntime.Ready();
            if (!ready || scene != FF9StateSystem.Battle.FF9Battle.btl_scene || pattern != FF9StateSystem.Battle.FF9Battle.btl_scene.PatNum)
            {
                if (hook != null) hook.Release();
                hook = null; original = layout = null; scene = ready ? FF9StateSystem.Battle.FF9Battle.btl_scene : null;
                pattern = ready ? scene.PatNum : -1; keepLoadedPreset = false; pending = false; pendingUnits.Clear(); pendingPartyGroup = false; partyShift = Vector3.zero; unitIndex = 0;
            }
            Boolean open = window.IsBattleSceneEditorActive;
            if (open && ready && hook == null)
            {
                GameObject go = GameObject.Find("Battle Camera");
                Camera camera = go == null ? null : go.GetComponent<Camera>();
                if (camera != null) { hook = BattleLayoutCameraHook.Attach(camera); hook.TakeEditorOwnership(); }
            }
            if (open && layout == null && hook != null && hook.HasNative)
            {
                layout = Capture(); original = layout.Clone();
                hook.ModelUnits = layout.Units;
                status = "Layout captured. Select a value: arrows adjust; Enter types an exact number.";
            }
            if (!open && wasOpen && hook != null && !keepLoadedPreset)
            {
                hook.Release();
                status = "Camera released. Formation edits remain until reset or battle exit.";
            }
            if (open && !wasOpen && layout != null && hook != null) BindCamera();
            wasOpen = open;
            Action request = null; lock (requests) if (requests.Count != 0) request = requests.Dequeue();
            if (request != null) { try { request(); } catch (Exception ex) { status = ex.Message; } Publish(); }
            if (pending && layout != null && BattleLayoutRuntime.Matches(layout))
            {
                Boolean partyBlocked = pendingPartyGroup && BattleLayoutRuntime.Units().Any(b => b.bi.player != 0 && !BattleLayoutRuntime.CanMoveHome(b));
                foreach (String key in pendingUnits.ToArray())
                {
                    if (partyBlocked && key.StartsWith("P", StringComparison.Ordinal)) continue;
                    LayoutUnit desired = layout.Units.FirstOrDefault(u => BattleLayoutRuntime.Key(u) == key);
                    BTL_DATA actor = BattleLayoutRuntime.Units().FirstOrDefault(b => BattleLayoutRuntime.Key(b) == key);
                    if (desired == null || actor == null || BattleLayoutRuntime.ApplyUnit(actor, desired)) pendingUnits.Remove(key);
                }
                pending = pendingUnits.Count != 0;
                if (!pendingUnits.Any(k => k.StartsWith("P", StringComparison.Ordinal))) pendingPartyGroup = false;
                if (!pending) { status = "Formation applied to home positions."; Publish(); }
            }
            if (open && Time.realtimeSinceStartup >= nextRefresh) { nextRefresh = Time.realtimeSinceStartup + 0.25f; Publish(); }
        }
        private BattleLayout Capture()
        {
            BattleLayout result = new BattleLayout { BattleId = FF9StateSystem.Battle.battleMapIndex, Pattern = scene.PatNum, Camera = hook.CaptureCurrent(), StageScale = layout?.StageScale ?? hook.StageScale };
            foreach (BTL_DATA b in BattleLayoutRuntime.Units())
            {
                LayoutUnit unit = BattleLayoutRuntime.Capture(b);
                LayoutUnit existing = (layout?.Units ?? hook.ModelUnits)?.FirstOrDefault(u => u.Party == unit.Party && u.Slot == unit.Slot);
                if (existing != null)
                {
                    unit.Scale = existing.Scale;
                    if (existing.HasRotation) { unit.HasRotation = true; unit.Rotation = existing.Rotation; }
                    if (pendingUnits.Contains(BattleLayoutRuntime.Key(unit)))
                        unit.Home = existing.Home;
                }
                result.Units.Add(unit);
            }
            return result;
        }
        private String FileName { get { return "Battle_" + layout.BattleId + "_P" + layout.Pattern + "_" + presetName + ".json"; } }
        private String[] Presets()
        {
            String directory = Path.Combine(root, "Presets");
            return layout == null || !Directory.Exists(directory) ? new String[0] : Directory.GetFiles(directory, "Battle_" + layout.BattleId + "_P" + layout.Pattern + "_*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        private LayoutUnit Selected { get { return layout == null || layout.Units.Count == 0 ? null : layout.Units[Math.Max(0, Math.Min(unitIndex, layout.Units.Count - 1))]; } }
        private void BindCamera() { hook.Settings = layout.Camera; hook.StageScale = layout.StageScale; hook.ModelUnits = layout.Units; hook.Editing = true; }
        private void LockOrbitCenter()
        {
            LayoutCamera camera = hook.Settings == null || !layout.Camera.Enabled ? hook.Capture() : layout.Camera;
            Matrix4x4 view = BattleLayoutCameraHook.View(camera);
            Matrix4x4 projection = hook.NativeProjection;
            if (layout.Units.Count == 0 || Math.Abs(projection.m00) < 0.001f || Math.Abs(projection.m11) < 0.001f)
                throw new InvalidOperationException("Wait for a battle formation and camera projection.");

            Vector3 center = Vector3.zero;
            foreach (LayoutUnit unit in layout.Units) center += unit.Home;
            Single depth = -view.MultiplyPoint3x4(center / layout.Units.Count).z;
            if (depth < 100f) throw new InvalidOperationException("Aim toward the formation before locking an orbit center.");

            Single zoom = Mathf.Tan(BattleLayoutCameraHook.Fov(projection) * Mathf.Deg2Rad * 0.5f) /
                Mathf.Tan(camera.Fov * Mathf.Deg2Rad * 0.5f);
            // The native projection is off-center. Use the actual screen-center ray.
            Vector3 centerRay = new Vector3(projection.m02 / (projection.m00 * zoom),
                projection.m12 / (projection.m11 * zoom), -1f);
            orbitCenter = view.inverse.MultiplyPoint3x4(centerRay * depth);
            orbitBase = view;
            orbitPose = layout.Camera;
            orbitAngle = 0f;
            orbitElevation = 0f;
            orbitFov = camera.Fov;
            status = "Orbit center locked. Adjust Orbit Angle to move around it.";
        }

        private void SetOrbitAngles(Single angle, Single elevation)
        {
            Vector3 right = orbitBase.inverse.MultiplyVector(Vector3.right).normalized;
            Quaternion rotation = Quaternion.AngleAxis(-elevation, right) *
                Quaternion.AngleAxis(-angle, Vector3.up);
            Matrix4x4 view = orbitBase *
                Matrix4x4.TRS(orbitCenter, rotation, Vector3.one) *
                Matrix4x4.TRS(-orbitCenter, Quaternion.identity, Vector3.one);
            LayoutCamera camera = layout.Camera.Clone();
            camera.Basis = view;
            camera.Position = view.inverse.MultiplyPoint3x4(Vector3.zero);
            camera.Angles = Vector3.zero;
            camera.Fov = orbitFov;
            camera.Enabled = true;
            layout.Camera = orbitPose = camera;
            orbitAngle = angle;
            orbitElevation = elevation;
            BindCamera();
            status = "Orbit angle updated. The locked center stays in frame.";
        }
        private void QueueFormation()
        { pendingUnits.Clear(); foreach (LayoutUnit unit in layout.Units) pendingUnits.Add(BattleLayoutRuntime.Key(unit)); pending = pendingUnits.Count != 0; }
        private void Process(String action, String value)
        {
            if (action == "open") { Publish(); return; }
            if (action == "targetNext" || action == "targetPrevious")
            {
                String[] mods = WorldEntranceInstaller.Mods(); if (mods.Length == 0) throw new InvalidOperationException("No installed target mods found.");
                Int32 index = Array.IndexOf(mods, targetMod); targetMod = mods[(index + (action == "targetNext" ? 1 : mods.Length - 1) + mods.Length) % mods.Length]; return;
            }
            if (layout == null || hook == null || !BattleLayoutRuntime.Matches(layout)) throw new InvalidOperationException("Wait for an active battle and its first rendered frame.");
            if (action == "orbitLock") { LockOrbitCenter(); return; }
            if (action == "unitPrevious" || action == "unitNext")
            { if (layout.Units.Count > 0) unitIndex = (unitIndex + (action == "unitPrevious" ? layout.Units.Count - 1 : 1)) % layout.Units.Count; return; }
            if (action == "presetPrevious" || action == "presetNext")
            { String[] files = Presets(); if (files.Length > 0) presetIndex = (presetIndex + (action == "presetPrevious" ? files.Length - 1 : 1)) % files.Length; return; }
            if (action == "name")
            { if (String.IsNullOrEmpty(value) || value.Length > 60 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..")) throw new ArgumentException("Use a short preset name without path characters."); presetName = value; return; }
            if (action == "freeze") { hook.Freeze = !hook.Freeze; BindCamera(); return; }
            if (action == "cameraEnabled") { layout.Camera.Enabled = !layout.Camera.Enabled; BindCamera(); return; }
            if (action == "release") { keepLoadedPreset = false; orbitPose = null; hook.Release(); status = "Native camera control restored."; return; }
            if (action == "captureCamera") { layout.Camera = hook.CaptureCurrent(); BindCamera(); status = "Captured the current visible camera; angle offsets reset."; return; }
            if (action == "resetCamera") { keepLoadedPreset = false; layout.Camera = original.Camera.Clone(); hook.Release(); hook.StageScale = layout.StageScale; hook.Editing = true; status = "Camera reset and native control restored. Edit a camera value to enable again."; return; }
            if (action == "capture")
            { Boolean bound = hook.Settings != null; LayoutCamera current = bound ? layout.Camera.Clone() : hook.Capture(); layout = Capture(); layout.Camera = current; partyShift = Vector3.zero; hook.ModelUnits = layout.Units; if (bound) BindCamera(); status = "Refreshed live layout; nothing installed."; return; }
            if (action == "captureInstall")
            {
                Process("capture", "");
                Boolean compile = BattleLayoutInstaller.Install(targetMod, layout, Path.Combine(root, "Templates"));
                BattleLayout.Save(Path.Combine(Path.Combine(root, "Presets"), FileName), layout);
                status = "Installed battle " + layout.BattleId + " / pattern " + layout.Pattern + " into " + targetMod + (compile ? ". Runtime sources added/updated: use Compile + Restart." : ". JSON updated; re-enter the battle to load it."); return;
            }
            if (action == "reset") { keepLoadedPreset = false; layout = original.Clone(); partyShift = Vector3.zero; QueueFormation(); hook.Release(); hook.StageScale = layout.StageScale; hook.Editing = true; status = "Restoring original formation; camera released."; return; }
            if (action == "save" || action == "saveCamera")
            {
                BattleLayout saved = layout.Clone(); if (action == "saveCamera") { saved.Units.Clear(); saved.StageScale = 1f; }
                String path = Path.Combine(Path.Combine(root, "Presets"), FileName);
                if (action == "saveCamera") path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_Camera.json");
                BattleLayout.Save(path, saved); status = "Saved: " + path; return;
            }
            if (action == "load")
            {
                String[] files = Presets(); if (files.Length == 0) throw new FileNotFoundException("No presets saved for this battle and pattern.");
                String path = files[presetIndex % files.Length]; BattleLayout loaded = BattleLayout.Parse(File.ReadAllText(path));
                if (!BattleLayoutRuntime.Matches(loaded)) throw new InvalidOperationException("Preset belongs to a different battle or pattern.");
                if (loaded.Units.Count == 0) foreach (BTL_DATA b in BattleLayoutRuntime.Units()) loaded.Units.Add(BattleLayoutRuntime.Capture(b));
                layout = loaded; QueueFormation(); BindCamera(); keepLoadedPreset = true; status = "Loaded for this battle: " + Path.GetFileName(path); return;
            }
            if (action == "export") { Export(); return; }
            Single number;
            if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || Single.IsNaN(number) || Single.IsInfinity(number)) throw new ArgumentException("Enter a finite number using a decimal point.");
            Boolean delta = action.StartsWith("delta:", StringComparison.Ordinal); if (delta) action = action.Substring(6);
            if (action == "sfxZoom")
            {
                Single percent = delta ? layout.Camera.SfxZoom * 100f + number : number;
                layout.Camera.SfxZoom = Mathf.Clamp(percent, 100f, 300f) / 100f;
                BindCamera();
                status = "SFX camera zoom updated; 100% is native framing.";
                return;
            }
            if (action == "stageScale")
            {
                Single percent = delta ? layout.StageScale * 100f + number : number;
                hook.StageScale = layout.StageScale = Mathf.Clamp(percent, 10f, 500f) / 100f;
                status = "Stage scale updated.";
                return;
            }
            if (action.StartsWith("unitRotation", StringComparison.Ordinal) && Selected != null)
            {
                Int32 axis = action[action.Length - 1] - 'X';
                if (axis < 0 || axis > 2) throw new ArgumentException("Invalid model rotation axis.");
                BTL_DATA actor = BattleLayoutRuntime.Units().FirstOrDefault(
                    b => BattleLayoutRuntime.Key(b) == BattleLayoutRuntime.Key(Selected));
                Vector3 rotation = Selected.HasRotation ? Selected.Rotation :
                    actor == null ? Vector3.zero : actor.evt.rotBattle.eulerAngles;
                rotation[axis] = Mathf.Clamp(delta ? rotation[axis] + number : number, -360f, 360f);
                Selected.Rotation = rotation;
                Selected.HasRotation = true;
                pendingUnits.Add(BattleLayoutRuntime.Key(Selected));
                pending = true;
                status = "Model rotation queued until the actor is free of attack movement.";
                return;
            }
            if (action == "unitScale" && Selected != null)
            {
                Single percent = delta ? Selected.Scale * 100f + number : number;
                Selected.Scale = Mathf.Clamp(percent, 10f, 500f) / 100f;
                hook.ModelUnits = layout.Units;
                status = "Model scale updated relative to its battle size.";
                return;
            }
            if (action == "orbitAngle" || action == "orbitElevation")
            {
                if (!ReferenceEquals(orbitPose, layout.Camera)) LockOrbitCenter();
                if (action == "orbitAngle")
                    SetOrbitAngles(Mathf.Repeat(delta ? orbitAngle + number : number, 360f), orbitElevation);
                else
                    SetOrbitAngles(orbitAngle, Mathf.Clamp(delta ? orbitElevation + number : number, -89f, 89f));
                return;
            }
            if (action.StartsWith("party", StringComparison.Ordinal))
            {
                Int32 axis = action[action.Length - 1] - 'X'; if (axis < 0 || axis > 2) throw new ArgumentException("Invalid party axis.");
                Single desired = Mathf.Clamp(delta ? partyShift[axis] + number : number, -20000, 20000);
                Single shift = desired - partyShift[axis];
                foreach (LayoutUnit unit in layout.Units.Where(u => u.Party))
                    if (Math.Abs(unit.Home[axis] + shift) > 30000) throw new ArgumentException("Party move would exceed coordinate range.");
                foreach (LayoutUnit unit in layout.Units.Where(u => u.Party))
                { Vector3 home = unit.Home; home[axis] += shift; unit.Home = home; pendingUnits.Add(BattleLayoutRuntime.Key(unit)); }
                partyShift[axis] = desired; pending = pendingUnits.Count != 0; pendingPartyGroup = pending;
                status = "Whole-party move queued. All present members move together when free of attack movement."; return;
            }
            if (action == "fov")
            {
                orbitPose = null;
                layout.Camera.Fov = Mathf.Clamp(delta ? layout.Camera.Fov + number : number, 5, 120);
            }
            else if (action.StartsWith("camera", StringComparison.Ordinal) || action.StartsWith("angle", StringComparison.Ordinal))
            {
                Boolean angle = action.StartsWith("angle", StringComparison.Ordinal); Int32 axis = action[action.Length - 1] - 'X';
                if (axis < 0 || axis > 2) throw new ArgumentException("Invalid camera axis.");
                orbitPose = null;
                Vector3 v = angle ? layout.Camera.Angles : layout.Camera.Position; v[axis] = Mathf.Clamp(delta ? v[axis] + number : number, angle ? -360 : -30000, angle ? 360 : 30000);
                if (angle) layout.Camera.Angles = v; else layout.Camera.Position = v;
            }
            else if (action.StartsWith("unit", StringComparison.Ordinal) && Selected != null)
            {
                Int32 axis = action[action.Length - 1] - 'X'; if (axis < 0 || axis > 2) throw new ArgumentException("Invalid unit axis.");
                Vector3 home = Selected.Home; home[axis] = Mathf.Clamp(delta ? home[axis] + number : number, -30000, 30000); Selected.Home = home;
                pendingUnits.Add(BattleLayoutRuntime.Key(Selected)); pending = true; status = "Formation edit queued; applies when the actor is free of attack movement."; return;
            }
            else throw new ArgumentException("Unknown editor control.");
            layout.Camera.Enabled = true; BindCamera(); status = "Camera updated. Freeze holds it through scripted cameras; release restores native control.";
        }
        private void Export()
        {
            String directory = Path.Combine(Path.Combine(root, "Exports"), Path.GetFileNameWithoutExtension(FileName));
            BattleLayoutInstaller.IncludeCameraHooks(directory);
            String data = Path.Combine(directory, "FF9_Data/BattleSceneLayouts");
            BattleLayout.Save(Path.Combine(data, "Battle_" + layout.BattleId + "_P" + layout.Pattern + ".json"), layout);
            String sources = Path.Combine(directory, "StreamingAssets/Scripts/Sources/BattleSceneLayouts"); Directory.CreateDirectory(sources);
            String templates = Path.Combine(root, "Templates");
            foreach (String name in new[] { "BattleLayoutData.cs", "BattleLayoutRuntime.cs", "LayoutJson.cs", "BattleLayoutBootstrap.cs" })
                File.Copy(Path.Combine(templates, name + ".txt"), Path.Combine(sources, name), true);
            File.Copy(Path.Combine(root, "RuntimeIntegration.md"), Path.Combine(directory, "README.md"), true);
            status = "Exported standalone mod package: " + directory;
        }
        private void Publish()
        {
            if (layout != null && BattleLayoutRuntime.Matches(layout))
                foreach (BTL_DATA b in BattleLayoutRuntime.Units())
                {
                    String key = BattleLayoutRuntime.Key(b); LayoutUnit existing = layout.Units.FirstOrDefault(u => BattleLayoutRuntime.Key(u) == key);
                    if (existing == null) layout.Units.Add(BattleLayoutRuntime.Capture(b));
                    else if (!pendingUnits.Contains(key)) { existing.Home = b.base_pos; existing.Name = BattleLayoutRuntime.Capture(b).Name; }
                }
            JObject view = new JObject(new JProperty("ready", layout != null && hook != null), new JProperty("status", status), new JProperty("name", presetName), new JProperty("targetMod", targetMod ?? "No installed mods"), new JProperty("partyShift", BattleLayout.Vector(partyShift)), new JProperty("pending", pending));
            if (layout != null && hook != null)
            {
                view["layout"] = layout.ToJson(); view["unitIndex"] = unitIndex; view["freeze"] = hook.Freeze;
                BTL_DATA selectedActor = Selected == null ? null : BattleLayoutRuntime.Units().FirstOrDefault(
                    b => BattleLayoutRuntime.Key(b) == BattleLayoutRuntime.Key(Selected));
                view["unitRotation"] = BattleLayout.Vector(Selected != null && Selected.HasRotation ?
                    Selected.Rotation : selectedActor == null ? Vector3.zero : selectedActor.evt.rotBattle.eulerAngles);
                view["orbitAngle"] = ReferenceEquals(orbitPose, layout.Camera) ? orbitAngle : 0f;
                view["orbitElevation"] = ReferenceEquals(orbitPose, layout.Camera) ? orbitElevation : 0f;
                view["cameraBound"] = hook.Settings != null; String[] files = Presets(); view["preset"] = files.Length == 0 ? "None saved" : Path.GetFileName(files[presetIndex % files.Length]);
                JArray live = new JArray(); foreach (BTL_DATA b in BattleLayoutRuntime.Units()) live.Add(new JObject(new JProperty("key", BattleLayoutRuntime.Key(b)), new JProperty("name", BattleLayoutRuntime.Capture(b).Name), new JProperty("battleMask", b.btl_id), new JProperty("hidden", b.bi.disappear != 0), new JProperty("hp", b.cur.hp)));
                view["live"] = live;
            }
            window.SetBattleSceneView(view.ToString());
        }
    }
}
