using System.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    // Reads the real field's post-script state. It never initializes an event,
    // evaluates a condition or changes the live effect's animation frame.
    internal static class FieldSpsCapture
    {
        internal sealed class Effect
        {
            internal Int32 Slot, Id, Frame, Frames, Rate, Scale, Fade, Depth, Duration;
            internal Byte Attributes, Blend;
            internal String Map;
            internal Vector3 Position, Rotation, Offset;
            internal Byte[] Binary;
            internal Texture2D Texture;
        }
        internal sealed class Snapshot
        {
            internal Int32 Field;
            internal String Map;
            internal readonly List<Effect> Effects = new List<Effect>();
        }
        private static readonly Dictionary<Int32, Snapshot> Cache = new Dictionary<Int32, Snapshot>();
        private static readonly Queue<Int32> Order = new Queue<Int32>();
        private static readonly Dictionary<Int32, Dictionary<String, Texture2D>> CapturedTextures = new Dictionary<Int32, Dictionary<String, Texture2D>>();
        private static Single _nextCapture;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static Snapshot Get(Int32 id)
        {
            Snapshot result;
            return Cache.TryGetValue(id, out result) ? result : null;
        }

        internal static void Capture(Boolean force)
        {
            if (!force && Time.realtimeSinceStartup < _nextCapture) return;
            _nextCapture = Time.realtimeSinceStartup + .25f;
            try
            {
                FieldSPSSystem system = UnityEngine.Object.FindObjectOfType<FieldSPSSystem>();
                if (system == null) return;
                FieldMap map = typeof(FieldSPSSystem).GetField("_fieldMap", Flags).GetValue(system) as FieldMap;
                CommonSPSSystem utility = typeof(FieldSPSSystem).GetField("Utility", Flags).GetValue(system) as CommonSPSSystem;
                if (map == null || utility == null || map.scene == null) return;
                Int32 id = FF9StateSystem.Common.FF9.fldMapNo;
                var lookup = (Dictionary<Int32, String>)typeof(EventEngine).Assembly.GetType("EventEngineUtils")
                    .GetField("eventIDToFBGID", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
                String resource;
                if (!lookup.TryGetValue(id, out resource) || !String.Equals(resource, map.mapName, StringComparison.OrdinalIgnoreCase)) return;
                Snapshot snapshot = new Snapshot { Field = id, Map = resource };
                foreach (SPSEffect effect in utility.SpsList)
                {
                    if (effect == null || effect.spsBin == null || (effect.attr & SPSConst.ATTR_VISIBLE) == 0) continue;
                    snapshot.Effects.Add(new Effect {
                        Slot = effect.spsIndex, Id = effect.spsId, Map = effect.mapName,
                        Binary = effect.spsBin, Texture = CaptureTexture(id, effect),
                        Position = effect.charTran != null && effect.boneTran != null ? effect.boneTran.position : effect.pos,
                        Rotation = effect.rot, Offset = effect.posOffset,
                        Scale = effect.scale, Fade = effect.fade, Blend = effect.abr,
                        Attributes = effect.attr, Frame = effect.curFrame, Frames = effect.frameCount,
                        Rate = effect.frameRate, Depth = effect.depthOffset, Duration = effect.duration
                    });
                }
                if (!Cache.ContainsKey(id))
                {
                    Order.Enqueue(id);
                    while (Order.Count > 32)
                    {
                        Int32 old = Order.Dequeue(); Cache.Remove(old);
                        Dictionary<String, Texture2D> textures;
                        if (CapturedTextures.TryGetValue(old, out textures))
                        {
                            foreach (Texture2D texture in textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
                            CapturedTextures.Remove(old);
                        }
                    }
                }
                Cache[id] = snapshot;
            }
            catch (Exception ex) { ViewerDiagnostics.Warning("[Field SPS Capture] " + ex.Message); }
        }

        private static Texture2D CaptureTexture(Int32 field, SPSEffect source)
        {
            if (source.pngTexture != null) return source.pngTexture;
            Dictionary<String, Texture2D> textures;
            if (!CapturedTextures.TryGetValue(field, out textures)) CapturedTextures[field] = textures = new Dictionary<String, Texture2D>();
            String key = source.mapName + "/" + source.spsId;
            Texture2D result;
            if (textures.TryGetValue(key, out result) && result != null) return result;
            // Read the texture from the current field's VRAM using an independent
            // decoder, so scanning frames doesn't touch the live SPS's work data.
            GameObject holder = new GameObject("MDC_SpsTextureCapture");
            holder.SetActive(false);
            SPSEffect decoder = null;
            try
            {
                decoder = holder.AddComponent<SPSEffect>(); decoder.Init(0);
                decoder.spsBin = source.spsBin; decoder.frameCount = source.frameCount;
                result = decoder.GetTextureFromCurrentTCB();
                if (result != null) textures[key] = result;
                return result;
            }
            finally
            {
                if (decoder != null && decoder.materials != null)
                    foreach (Material material in decoder.materials) if (material != null) UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(holder);
            }
        }
    }

    internal sealed partial class FieldAssetViewer
    {
        private sealed class CapturedSps
        {
            internal SPSEffect Effect;
            internal FieldSpsCapture.Effect Source;
            internal Boolean Visible = true; internal bool Reference; internal int DisplaySlot;
        }
        private readonly List<CapturedSps> _capturedSps = new List<CapturedSps>();
        private GameObject _projectionHolder;
        private FieldMap _projectionField;

        private void LoadCapturedSps()
        {
            FieldSpsCapture.Snapshot snapshot = FieldSpsCapture.Get(_selected.Id);
            if (snapshot == null || !String.Equals(snapshot.Map, _selected.Map, StringComparison.OrdinalIgnoreCase))
            {
                _status = "No live SPS capture for this field. Visit it in-game, then reopen the viewer.";
                return;
            }
            _projectionHolder = new GameObject("MDC_InactiveSpsProjection");
            _projectionHolder.SetActive(false);
            _projectionField = _projectionHolder.AddComponent<FieldMap>();
            _projectionField.scene = _scene;
            _projectionField.mapName = _selected.Map;
            UpdateCapturedCamera();
            foreach (FieldSpsCapture.Effect source in snapshot.Effects)
            {
                if (source.Texture == null)
                {
                    // Native TCB-only effects need a captured texture, not the
                    // VRAM belonging to the unrelated currently loaded field.
                    ViewerDiagnostics.Warning("[Field SPS Capture] No detached texture for slot " + source.Slot + " in field " + _selected.Id);
                    continue;
                }
                GameObject go = new GameObject("MDC_LiveSPS_" + source.Slot + "_" + source.Id);
                go.transform.parent = _root.transform;
                go.layer = Layer;
                SPSEffect effect = go.AddComponent<SPSEffect>();
                effect.Init(1);
                effect.spsTransform = go.transform;
                effect.meshRenderer = go.AddComponent<MeshRenderer>();
                effect.meshFilter = go.AddComponent<MeshFilter>();
                effect.fieldMap = _projectionField;
                effect.spsIndex = source.Slot; effect.spsId = source.Id; effect.mapName = source.Map;
                effect.spsBin = source.Binary; effect.pngTexture = source.Texture;
                effect.pos = source.Position; effect.rot = source.Rotation; effect.posOffset = source.Offset;
                effect.scale = source.Scale; effect.fade = source.Fade; effect.abr = source.Blend;
                effect.attr = source.Attributes; effect.frameCount = source.Frames;
                effect.frameRate = source.Rate; effect.depthOffset = source.Depth;
                _capturedSps.Add(new CapturedSps { Visible = _effectsPlaying, Effect = effect, Source = source });
            }
            _status = _capturedSps.Count + " live SPS initialized from the last in-game state. Actor attachments stay at their captured positions.";
        }

        private void UpdateCapturedCamera()
        {
            if (_projectionField == null || _scene == null || _scene.cameraList.Count == 0) return;
            _projectionField.camIdx = _cameraIndex;
            typeof(FieldMap).GetField("curCamIdx", Flags).SetValue(_projectionField, _cameraIndex);
            BGCAM_DEF camera = _scene.cameraList[_cameraIndex];
            Vector2 center = camera.GetCenterOffset();
            // Derive the native half-screen dimensions from the background origin;
            // this also works with builds that don't expose HalfFieldWidth/Height.
            Vector3 origin = _root.transform.Find("Background").localPosition;
            _projectionField.offset = new Vector2(center.x + camera.w / 2 - _scene.curX + origin.x,
                -center.y - camera.h / 2 + _scene.curY + origin.y);
        }

        internal static Int32 CapturedFrame(Int32 initial, Int32 rate, Int32 count, Int32 ticks)
        {
            if (count <= 0) return 0;
            Int64 frame = ((Int64)initial + (Int64)rate * ticks) % count;
            return (Int32)(frame < 0 ? frame + count : frame);
        }

        private void UpdateCapturedSps()
        {
            Int32 ticks = (Int32)(_time * 30f);
            foreach (CapturedSps item in _capturedSps)
            {
                SPSEffect effect = item.Effect;
                FieldSpsCapture.Effect source = item.Source;
                Boolean finished = source.Duration >= 0 && ticks >= source.Duration;
                if ((source.Attributes & SPSConst.ATTR_UNLOAD_ON_FINISH) != 0 && source.Rate > 0 &&
                    (Int64)source.Frame + (Int64)ticks * source.Rate >= source.Frames) finished = true;
                effect.meshRenderer.enabled = !finished;
                if (finished || !item.Visible) { effect.meshRenderer.enabled = false; continue; }
                effect.curFrame = CapturedFrame(source.Frame, source.Rate, source.Frames, ticks);
                effect.GenerateSPS();
            }
        }

        private static bool StateButton(string text, bool enabled, string tooltip = null)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = enabled ? Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f) : Color.white;
            bool pressed = GUILayout.Button(new GUIContent((enabled ? "ON   " : "OFF   ") + text, tooltip ?? ""), GUILayout.MinHeight(34));
            GUI.backgroundColor = previous;
            GUILayout.Space(5);
            return pressed ? !enabled : enabled;
        }
        private void DrawCapturedSpsList()
        {
            GUILayout.BeginHorizontal();
            Color actionColor = GUI.backgroundColor;
            Color actionText = GUI.contentColor;
            GUI.contentColor = ConsoleTheme.UnityAccent;
            var actionStyle = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.backgroundColor = _effectsPlaying ? Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f) : Color.white;
            if (GUILayout.Button(new GUIContent("PLAY ALL", "Enable and restart effects in both Background SPS and Object SPS lists."), actionStyle, GUILayout.Height(48))) { _effectsPlaying = true; _animate = true; foreach (var item in _capturedSps) { item.Visible = true; item.Effect.curFrame = 0; } }
            GUI.backgroundColor = !_effectsPlaying ? Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f) : Color.white;
            if (GUILayout.Button(new GUIContent("STOP ALL", "Hide effects in both lists. Effects stay stopped when you change fields until you enable them again."), actionStyle, GUILayout.Height(48))) { _effectsPlaying = false; foreach (var item in _capturedSps) item.Visible = false; ClearEffect(); }
            GUI.backgroundColor = actionColor;
            GUI.contentColor = actionText;
            GUILayout.EndHorizontal();
            GUILayout.Space(16);
            bool scenarios = StateButton("All scenarios", _allScenarios, "Show effects and background animations from different story states together, even when they would not normally appear at the same time. Some placements are approximate.");
            if (scenarios != _allScenarios) { _allScenarios = scenarios; _pendingId = _selected.Id; }
            _loopAllSps = StateButton("Loop SPS effects", _loopAllSps, "Keep particle effects looping and visible instead of letting one-shot effects end.");
            _loopTileAnimations = StateButton("Loop tiles — 37.5% speed", _loopTileAnimations, "Animate background tiles at 37.5% of normal speed. Turn off to pause them while keeping the current tiles visible.");
            GUILayout.Space(8);
            float available = Math.Max(130, Screen.height - 578);
            int backgroundCount = _capturedSps.Count(e => e.Effect.spsBin != null && !_scriptAttachments.ContainsKey(e.Source.Slot));
            int objectCount = _capturedSps.Count(e => e.Effect.spsBin != null && _scriptAttachments.ContainsKey(e.Source.Slot));
            float backgroundHeight = objectCount == 0 ? available - 65 : backgroundCount == 0 ? 65 : available / 2;
            float objectHeight = available - backgroundHeight;
            GUILayout.Space(8);
            Color headingColor = GUI.contentColor;
            GUI.contentColor = ConsoleTheme.UnityAccent;
            GUILayout.Label(new GUIContent("BACKGROUND SPS" + (_rainStrength > 0 ? " + Weather rain" : ""), "Effects placed in the field rather than attached to an object. Weather rain is included when available."));
            GUI.contentColor = headingColor;
            GUILayout.Space(6);
            _backgroundListScroll = GUILayout.BeginScrollView(_backgroundListScroll, GUILayout.Height(backgroundHeight));
            DrawSpsGroup(false);
            GUILayout.EndScrollView();
            GUILayout.Space(16);
            GUI.contentColor = ConsoleTheme.UnityAccent;
            GUILayout.Label(new GUIContent("OBJECT SPS", "Effects attached to field objects. Preview positions use approximate dummy attachments; full object and bone movement is not simulated."));
            GUI.contentColor = headingColor;
            GUILayout.Space(6);
            _objectListScroll = GUILayout.BeginScrollView(_objectListScroll, GUILayout.Height(objectHeight));
            DrawSpsGroup(true);
            GUILayout.EndScrollView();
        }
        private void DrawSpsGroup(bool objects)
        {
            int count = 0;
            foreach (var item in _capturedSps)
            {
                if (_scriptAttachments.ContainsKey(item.Source.Slot) != objects || item.Effect.spsBin == null) continue;
                string title = item.Source.Id + ".sps | Slot " + (item.Reference ? item.DisplaySlot : item.Source.Slot);
                if (item.Reference) title += " | Variant";
                if (objects) title += " | Object " + _scriptAttachments[item.Source.Slot];
                if (title.IndexOf(_assetQuery, StringComparison.OrdinalIgnoreCase) < 0) continue;
                count++;
                bool enabled = StateButton(title, item.Visible, "Toggle this effect. Turning it on restarts it. Slot identifies the script effect instance; Variant indicates a story-state reference. Source: " + item.Source.Map);
                if (enabled && !item.Visible) { _effectsPlaying = true; item.Effect.curFrame = 0; }
                item.Visible = enabled;
            }
            if (count == 0) GUILayout.Label("No matching effects.");
        }
        private void ClearCapturedSps()
        {
            foreach (CapturedSps item in _capturedSps)
            {
                SPSEffect effect = item.Effect;
                if (effect == null) continue;
                effect.gameObject.SetActive(false);
                effect.transform.parent = null;
                if (effect.meshFilter.sharedMesh != null) UnityEngine.Object.Destroy(effect.meshFilter.sharedMesh);
                foreach (Material material in effect.materials) if (material != null) UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(effect.gameObject);
            }
            _capturedSps.Clear();
            if (_projectionHolder != null) UnityEngine.Object.Destroy(_projectionHolder);
            _projectionHolder = null; _projectionField = null;
        }
    }
}
