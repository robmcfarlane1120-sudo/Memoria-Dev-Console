using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal sealed partial class FieldAssetViewer
    {
        private bool _allScenarios = true, _referenceModeUsed, _loadingReferences;
        private bool _effectsPlaying = true;
        private bool _loopTileAnimations = true;
        private float _tileTime;
        private float _tileStepBudget;
        private int _rainStrength, _rainSpeed;
        private float _rainTime;
        private Vector2 _backgroundListScroll, _objectListScroll;
        private readonly Dictionary<int, int[]> _groundSps = new Dictionary<int, int[]>();
        private readonly HashSet<int> _referenceFallbackSlots = new HashSet<int>();
        private readonly Dictionary<int, string> _fieldNames = new Dictionary<int, string>();

        private static string ReferencePath(string file)
        {
            return Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Memoria Dev Console/FF9_Data/FieldViewer"), file);
        }
        private void ReadFieldNames()
        {
            string path = ReferencePath("FieldViewerNames.tsv");
            if (!File.Exists(path)) return;
            foreach (string row in File.ReadAllLines(path))
            {
                string[] parts = row.Split('\t'); int id;
                if (parts.Length == 2 && Int32.TryParse(parts[0], out id)) _fieldNames[id] = parts[1];
            }
        }
        private string FieldLabel(FieldEntry field)
        {
            string name;
            if (!_fieldNames.TryGetValue(field.Id, out name))
                name = field.Map.StartsWith("FBG_N") && field.Map.Contains("CUSTOM_FIELD")
                    ? "Custom Field" : field.Map;
            return field.Id.ToString("D4") + ": " + name;
        }
        private Vector3 ApproximateSpsPosition(int index)
        {
            BGCAM_DEF camera = _scene.cameraList[_cameraIndex];
            Vector2 offset = _projectionField.GetProjectionOffset();
            float depth = camera.GetMatrixRT().MultiplyPoint(Vector3.zero).z;
            var depths = _scriptPositions.Values.Select(v => camera.GetMatrixRT().MultiplyPoint(new Vector3(v.x, -v.y, v.z)).z).Where(d => d > 1).OrderBy(d => d).ToArray();
            if (depths.Length > 0) depth = depths[depths.Length / 2];
            if (depth <= 1) depth = Math.Max(1, camera.GetViewDistance()) * 8f;
            float x = _bounds.center.x + ((index * 37 % 101) / 100f - .5f) * _bounds.size.x * .7f;
            float y = _bounds.center.y + ((index * 61 % 101) / 100f - .5f) * _bounds.size.y * .6f;
            Vector3 p = camera.GetMatrixRT().inverse.MultiplyPoint(new Vector3(
                (x - offset.x) * depth / camera.GetViewDistance(),
                -(y - offset.y) * depth / camera.GetViewDistance(), depth));
            p.y = -p.y;
            return p;
        }
        internal static Vector3 GroundSpawnPosition(int[] ground, int cycle)
        {
            // Script constants are signed 16-bit world coordinates. Retain the
            // authored ground height; vary only the horizontal axes like the script.
            uint seed = unchecked((uint)(ground[0] * 747796405 + cycle * 2891336453L + 277803737));
            seed ^= seed >> 16; seed *= 2246822519u; seed ^= seed >> 13;
            float angle = (seed & 255) * (2f * Mathf.PI / 256f);
            float radius = (seed >> 8) & 255;
            return new Vector3((short)ground[1] + Mathf.Sin(angle) * radius * 4096f / (1 << ground[4]),
                -(short)ground[2], (short)ground[3] + Mathf.Cos(angle) * radius * 4096f / (1 << ground[5]));
        }
        private void LoadScenarioReferences()
        {
            _referenceModeUsed = false;
            _referenceFallbackSlots.Clear();
            if (!_allScenarios) return;
            string path = ReferencePath("FieldViewerSpsPresets.tsv");
            if (!File.Exists(path)) return;
            _groundSps.Clear();
            var groundSlots = new Dictionary<int, int[]>();
            string groundPath = ReferencePath("FieldViewerSpsGround.tsv");
            if (File.Exists(groundPath))
                foreach (string row in File.ReadAllLines(groundPath))
                {
                    string[] parts = row.Split('\t');
                    if (parts.Length != 2 || parts[0] != _selected.Id.ToString()) continue;
                    int[] values = parts[1].Split(',').Select(Int32.Parse).ToArray();
                    if (values.Length == 6 && values[4] >= 0 && values[4] <= 20 && values[5] >= 0 && values[5] <= 20)
                        groundSlots[values[0]] = values;
                }
            int slot = 256;
            _loadingReferences = true;
            try
            {
                foreach (string row in File.ReadAllLines(path))
                {
                    string[] parts = row.Split('\t');
                    if (parts.Length != 2 || parts[0] != _selected.Id.ToString()) continue;
                    if (slot >= 512) break;
                    string[] commands = parts[1].Split(';');
                    int[] load = commands[0].Split(',').Select(Int32.Parse).ToArray();
                    var origin = _fields.FirstOrDefault(f => f.Id == load[0]);
                    if (origin == null && load[3] == 0) continue;
                    _spsOrigin = origin == null ? _selected.Map : origin.Map;
                    int current = slot++;
                    try
                    {
                        PreviewSpsCommand(new[] { current, 130, load[2], load[3], 0 });
                        var item = _capturedSps.First(e => e.Source.Slot == current);
                        item.Reference = true;
                        item.DisplaySlot = load[1];
                        item.Effect.scale = 4096;
                        item.Effect.fade = 128;
                        item.Effect.frameRate = 16;
                        bool positioned = false;
                        foreach (string command in commands.Skip(1))
                        {
                            int[] c = command.Split(',').Select(Int32.Parse).ToArray();
                            if (c[0] == 135 || c[0] == 165)
                                for (int axis = 1; axis <= 3; axis++) c[axis] = (short)c[axis];
                            PreviewSpsCommand(new[] { current, c[0], c[1], c[2], c[3] });
                            if (c[0] == 135) positioned = c[1] != 0 || c[2] != 0 || c[3] != 0;
                        }
                        int[] ground;
                        if (groundSlots.TryGetValue(load[1], out ground))
                        {
                            _groundSps[current] = ground;
                            positioned = true;
                            item.Effect.curFrame = CapturedFrame(0, 16, item.Effect.frameCount, load[1] * 7);
                        }
                        if (!positioned) _referenceFallbackSlots.Add(current);
                        if (item.Effect.scale <= 0) item.Effect.scale = 4096;
                        if (item.Effect.fade == 0) item.Effect.fade = 128;
                        item.Effect.attr |= SPSConst.ATTR_VISIBLE | SPSConst.ATTR_UPDATE_ANY_FRAME;
                        _referenceModeUsed = true;
                    }
                    catch (Exception ex) { _status = "Reference SPS skipped: " + ex.Message; ViewerDiagnostics.Warning("[Field Viewer] " + _selected.Id + ": " + _status); }
                }
                string weather = ReferencePath("FieldViewerWeather.tsv");
                if (File.Exists(weather))
                    foreach (string row in File.ReadAllLines(weather))
                    {
                        string[] p = row.Split('\t');
                        if (p.Length != 2 || p[0] != _selected.Id.ToString()) continue;
                        int[] w = p[1].Split(',').Select(Int32.Parse).ToArray();
                        if (w[0] > _rainStrength) { _rainStrength = w[0]; _rainSpeed = w[1]; }
                    }
            }
            finally { _loadingReferences = false; _spsOrigin = _selected.Map; }
        }
        private readonly HashSet<int> _previewAnimationTargets = new HashSet<int>();
        private void InitializeReferenceAnimations()
        {
            _previewAnimationTargets.Clear();
            if (!_allScenarios) return;
            string path = ReferencePath("FieldViewerAnimations.tsv");
            if (File.Exists(path))
            {
                foreach (string row in File.ReadAllLines(path))
                {
                    string[] parts = row.Split('\t');
                    if (parts.Length != 2 || parts[0] != _selected.Id.ToString()) continue;
                    string[] c = parts[1].Split(',');
                    int index, value;
                    if (c.Length < 3 || !Int32.TryParse(c[1], out index) || !Int32.TryParse(c[2], out value) || index < 0 || index >= _scene.animList.Count) continue;
                    if (c[0] == "SetTileAnimationSpeed" && value != 0)
                        _projectionField.EBG_animSetFrameRate(index, value);
                    else if (c[0] == "SetTileAnimationFlags")
                        _projectionField.EBG_animSetFlags(index, value);
                    else if (c[0] == "SetTileAnimationFrameWait" && c.Length > 3)
                    {
                        int wait;
                        if (value >= 0 && value < _scene.animList[index].frameList.Count && Int32.TryParse(c[3], out wait) && wait >= 0)
                            _projectionField.EBG_animSetFrameWait(index, value, wait);
                    }
                }
            }
            foreach (BGANIM_DEF anim in _scene.animList)
                foreach (BGANIMFRAME_DEF frame in anim.frameList)
                    if (frame.target < _scene.overlayList.Count) _previewAnimationTargets.Add(frame.target);
            if (_selected.Id == 3006 && _scene.overlayList.Count > 20)
                _projectionField.EBG_overlayMove(20, 0, 43, 0);
            PrepareReferenceAnimations();
        }
        private void PrepareReferenceAnimations()
        {
            if (!_allScenarios) return;
            for (int i = 0; i < _scene.animList.Count; i++)
            {
                BGANIM_DEF anim = _scene.animList[i];
                bool valid = anim.frameCount > 0 && anim.frameCount <= anim.frameList.Count &&
                    anim.frameList.Take(anim.frameCount).All(f => f.target < _scene.overlayList.Count);
                _projectionField.animIdx[i] = valid && _effectsPlaying;
                if (!valid) continue;
                if (anim.curFrame < 0 || (anim.curFrame >> 8) >= anim.frameCount) anim.curFrame = 0;
                if (anim.frameRate == 0) anim.frameRate = 256;
                anim.flags |= BGANIM_DEF.ANIM_FLAG.Animate | BGANIM_DEF.ANIM_FLAG.Loop;
                // A script's one-shot end marker must not terminate the preview loop.
                foreach (BGANIMFRAME_DEF frame in anim.frameList)
                    if (frame.value < 0) frame.value = 0;
            }
        }
        private void UpdateTileAnimations()
        {
            PrepareReferenceAnimations();
            if (_loopTileAnimations && _effectsPlaying)
            {
                _tileStepBudget += .375f;
                if (_tileStepBudget < 1f) return;
                _tileStepBudget -= 1f;
                _tileTime += 1f / 30f;
                _projectionField.BgAnimationService();
            }
            else if (_allScenarios)
            {
                // Pause on the current frame, including immediately after field load.
                for (int i = 0; i < _scene.animList.Count; i++)
                {
                    BGANIM_DEF anim = _scene.animList[i];
                    if (anim.camNdx != _cameraIndex || anim.frameCount <= 0 || anim.frameCount > anim.frameList.Count ||
                        anim.frameList.Take(anim.frameCount).Any(f => f.target >= _scene.overlayList.Count)) continue;
                    var flags = anim.flags;
                    _projectionField.EBG_animShowFrame(i, Math.Max(0, Math.Min(anim.frameCount - 1, anim.curFrame >> 8)));
                    anim.flags = flags;
                }
            }
        }
        private void UpdateReferenceOverlays()
        {
            // Pandemonium Generator: the vanilla Code1/Code2 loops pulse
            // ShowTile/SetTileColor groups. These lights are not SPS assets.
            if (!_allScenarios) return;
            // Cinematic init hides these until a later event request; the asset
            // preview has no movie playback to issue that request.
            if (_selected.Id == 2933)
                for (int i = 0; i < Math.Min(3, _scene.overlayList.Count); i++)
                    _projectionField.EBG_overlaySetActive(i, true);
            if (_selected.Id == 763 && _scene.overlayList.Count > 28)
            {
                // Overlay 28 is the ending-only cover. Lightning overlays 15..20
                // need their scripted brightness/visibility, not permanent display.
                _projectionField.EBG_overlaySetActive(28, false);
                float phase = Mathf.Repeat(_tileTime * 30f, 150f);
                int variant = (int)(_tileTime / 5f) % 3;
                for (int i = 15; i <= 20; i++)
                {
                    bool flash = i % 3 == variant && phase < (i < 18 ? 5 : 14);
                    byte shade = (byte)Math.Max(0, (i < 18 ? 104 : 144) - (int)phase * (i < 18 ? 16 : 10));
                    _projectionField.EBG_overlaySetActive(i, flash);
                    _projectionField.EBG_overlaySetShadeColor(i, shade, shade, shade);
                }
            }
            if (_selected.Id != 2709) return;
            int[][] groups = {
                new[] {4,10,16,22}, new[] {2,8,12,20}, new[] {0,6,14,18,24},
                new[] {5,9,19,21}, new[] {3,11,15,23}, new[] {1,7,13,17,25}
            };
            for (int g = 0; g < groups.Length; g++)
            {
                float phase = Mathf.Repeat(_tileTime * 30f - g * 49f, 330f);
                bool active = phase < 64f;
                byte shade = (byte)Mathf.Clamp(Mathf.Sin(phase / 64f * Mathf.PI) * 228f, 0, 255);
                foreach (int tile in groups[g])
                {
                    if (tile >= _scene.overlayList.Count) continue;
                    _projectionField.EBG_overlaySetActive(tile, active);
                    _projectionField.EBG_overlaySetShadeColor(tile, shade, shade, shade);
                }
            }
        }
        private void DrawPreviewRain(Rect area)
        {
            if (_rainStrength <= 0 || !_effectsPlaying || _image == null) return;
            // SetWeather is native screen-space rain, not an SPS file.
            // Keep this approximation inside the fitted preview image only.
            float aspect = (float)_image.width / _image.height;
            float width = Math.Min(area.width, area.height * aspect);
            float height = width / aspect;
            Rect rect = new Rect(area.center.x - width / 2, area.center.y - height / 2, width, height);
            Color old = GUI.color;
            GUI.BeginGroup(rect);
            GUI.color = new Color(.65f, .8f, 1f, .45f);
            int count = Math.Min(222, _rainStrength * 4);
            for (int i = 0; i < count; i++)
            {
                float phase = Mathf.Repeat(i * .618034f + _rainTime * Math.Max(1, _rainSpeed) / 70f, 1f);
                float x = Mathf.Repeat(i * .414214f - phase * .08f, 1f) * rect.width;
                GUI.DrawTexture(new Rect(x, phase * rect.height, 1.5f, 9 + _rainSpeed * .2f), Texture2D.whiteTexture);
            }
            GUI.color = old; GUI.EndGroup();
        }
    }
}