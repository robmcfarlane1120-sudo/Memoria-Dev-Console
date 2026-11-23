using System;
using System.Collections.Generic;
using System.Linq;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal sealed partial class FieldAssetViewer
    {
        private FieldPreviewScript _previewScript;
        private CommonSPSSystem _previewSps;
        private string _spsOrigin;
        private float _scriptClock;
        private bool _loopAllSps = true;
        private readonly Dictionary<int, GameObject> _dummyActors = new Dictionary<int, GameObject>();

        private int _previewPlayerUid = -1;
        private int ResolvePreviewActor(int actor) { return actor == 250 && _previewPlayerUid >= 0 ? _previewPlayerUid : actor; }
        private Vector3 DummyActorPosition(int actor)
        {
            actor = ResolvePreviewActor(actor);
            GameObject dummy;
            if (!_dummyActors.TryGetValue(actor, out dummy))
            {
                dummy = new GameObject("MDC_SPS_Dummy_" + actor);
                dummy.transform.parent = _root.transform;
                _dummyActors[actor] = dummy;
            }
            Vector3 position;
            if (!_scriptPositions.TryGetValue(actor, out position))
                position = _scriptPositions.Count > 0 ? _scriptPositions.Values.First() : Vector3.zero;
            dummy.transform.localPosition = position;
            return position;
        }
        private readonly List<Texture2D> _scriptTextures = new List<Texture2D>();
        private readonly Dictionary<string, Texture2D> _scriptTextureCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<int, Vector3> _scriptPositions = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, int> _scriptAttachments = new Dictionary<int, int>();

        private void InitializeFieldScripts()
        {
            _scriptClock = 0;
            _spsOrigin = _selected.Map;
            _previewSps = new CommonSPSSystem();
            _projectionHolder = new GameObject("MDC_InactiveScriptProjection");
            _projectionHolder.SetActive(false);
            _projectionField = _projectionHolder.AddComponent<FieldMap>();
            _projectionField.scene = _scene; _projectionField.mapName = _selected.Map;
            _projectionField.animIdx = Enumerable.Repeat(true, _scene.animList.Count).ToArray();
            UpdateCapturedCamera();
            LoadScenarioReferences();
            InitializeReferenceAnimations();
            try
            {
                _previewScript = FieldPreviewScript.Load(_selected.Id, PreviewCommand);
                // Initialization begins immediately; longer waits progress during Update.
                if (_previewScript != null) _previewScript.Tick();
                _status = "Initializing this field's scripts in the preview.";
            }
            catch (Exception ex)
            {
                _status = "Field script initialization failed: " + ex.GetBaseException().Message;
                Log.Warning("[Field Viewer Scripts] " + ex);
            }
        }

        private void PreviewCommand(int op, int[] args, int uid)
        {
            if (op == 0xB3 || op == 0xDA) { if (!_referenceModeUsed) PreviewSpsCommand(args); return; }
            if (op == 0xD8) { if (!_allScenarios || args[0] > _rainStrength) { _rainStrength = args[0]; _rainSpeed = args[1]; } return; }
            if (op == 0x7E) { if (args[0] >= 0 && args[0] < _scene.cameraList.Count) SelectCamera(args[0]); return; }
            if (op == 0xEA)
            {
                short x = 0, y = 0; _projectionField.EBG_sceneGetVRP(ref x, ref y);
                _previewScript.ScreenX = x; _previewScript.ScreenY = y; return;
            }
            if (_allScenarios && ((op >= 0x5F && op <= 0x64) || op == 0xE7 || (op == 0x5B && _previewAnimationTargets.Contains(args[0])))) return;
            // These native helpers only modify our detached scene's animation data.
            if (op == 0x5B) { if (args[0] < _scene.overlayList.Count) _projectionField.EBG_overlaySetActive(args[0], args[1] != 0); return; }
            if (op == 0x5F) { _projectionField.EBG_animAnimate(args[0], args[1]); return; }
            if (op == 0x60) { _projectionField.EBG_animSetActive(args[0], args[1] != 0); return; }
            if (op == 0x61) { _projectionField.EBG_animSetFrameRate(args[0], args[1]); return; }
            if (op == 0x63) { _projectionField.EBG_animSetFrameWait(args[0], args[1], args[2]); return; }
            if (op == 0x64) { _projectionField.EBG_animSetFlags(args[0], args[1]); return; }
            if (op == 0xE7) { _projectionField.EBG_animShowFrame(args[0], args[1]); return; }
            if (op == 0x2C) _previewPlayerUid = uid;
            if (op == 0xA1) _scriptPositions[uid] = new Vector3(args[0], -args[1], args[2]);
            else if (op == 0xAD) _scriptPositions[args[0]] = new Vector3(args[1], -args[2], args[3]);
            else if (op == 0x1D) _scriptPositions[uid] = new Vector3(args[0], 0, args[1]);
            else if (op == 0xBF) _scriptPositions[args[0]] = new Vector3(args[1], 0, args[2]);
            else if (op >= 0xC5 && op <= 0xC8)
            {
                int command = args[0] & 65535, id = args[1] & 65535;
                // Only playback commands; loading/fading/stopping global game voices
                // is intentionally never forwarded to FF9Snd.
                if (command == 0)
                {
                    DevSoundEntry track = _musicEntries.FirstOrDefault(s => s.Id == id);
                    if (track != null && _previewMusicPath != track.Path)
                    {
                        PlayPreviewMusic(track.Path); _selectedAudioPath = track.Path;
                        _audioStatus = "Field script BGM " + id;
                    }
                }
                else if (command == FF9Snd.FF9SOUND_SONG_STOP)
                {
                    _music.Stop(); _selectedAudioPath = null; _previewMusicPath = null;
                }
                else if (command == FF9Snd.FF9SOUND_SNDEFFECT_PLAY)
                {
                    DevSoundEntry sound = _soundEntries.FirstOrDefault(s => s.Id == id);
                    if (sound != null && !_mutePreviewBgm) _sound.Play(sound.Path);
                }
                else if (command == FF9Snd.FF9SOUND_SNDEFFECT_STOP) _sound.Stop();
            }
        }

        private void PreviewSpsCommand(int[] args)
        {
            int slot = args[0], operation = args[1], x = args[2], y = args[3], z = args[4];
            if (operation == SPSConst.OPERATION_CHANGE_FIELD)
            {
                var origin = _fields.FirstOrDefault(f => f.Id == x);
                if (origin != null) _spsOrigin = origin.Map;
                else if (x >= 9000) _spsOrigin = "WorldMap";
                else throw new InvalidOperationException("Unknown SPS origin field " + x);
                return;
            }
            if (slot < 0 || slot >= (_loadingReferences ? 512 : 128)) throw new InvalidOperationException("SPS slot exceeds preview limit: " + slot);
            CapturedSps item = _capturedSps.FirstOrDefault(e => e.Source.Slot == slot);
            if (item == null)
            {
                GameObject go = new GameObject("MDC_ScriptSPS_" + slot);
                go.transform.parent = _root.transform; go.layer = Layer;
                SPSEffect created = go.AddComponent<SPSEffect>(); created.Init(1);
                created.spsTransform = go.transform; created.fieldMap = _projectionField;
                created.meshRenderer = go.AddComponent<MeshRenderer>(); created.meshFilter = go.AddComponent<MeshFilter>();
                created.spsIndex = slot;
                item = new CapturedSps { Visible = _effectsPlaying, Effect = created, Source = new FieldSpsCapture.Effect { Slot = slot } };
                _capturedSps.Add(item);
            }
            SPSEffect effect = item.Effect;
            switch (operation)
            {
                case SPSConst.OPERATION_LOAD:
                    if (x == -1 || x == 65535) { if (_loopAllSps) return; effect.Unload(); _scriptAttachments.Remove(slot); return; }
                    string map = y == 0 ? _spsOrigin : "FromPrototype";
                    byte[] binary;
                    if (!_previewSps.LoadSPSBin(new KeyValuePair<string, int>(map, x), out binary))
                        throw new InvalidOperationException("Missing SPS " + map + "/" + x);
                    effect.mapName = map; effect.spsId = x; effect.spsBin = binary;
                    effect.frameCount = CommonSPSSystem.GetSpsFrameCount(binary);
                    effect.curFrame = 0; effect.lastFrame = -1; effect.attr |= SPSConst.ATTR_UPDATE_ANY_FRAME;
                    effect.pngTexture = LoadPreviewSpsTexture(effect);
                    item.Source.Map = map; item.Source.Id = x;
                    break;
                case SPSConst.OPERATION_ATTR:
                    effect.attr = y == 0 ? (byte)(effect.attr & ~x) : (byte)(effect.attr | x); break;
                case SPSConst.OPERATION_POS: effect.pos = new Vector3(x, -y, z); break;
                case SPSConst.OPERATION_ROT: effect.rot = new Vector3(x, y, z) * (360f / 4096f); break;
                case SPSConst.OPERATION_SCALE: effect.scale = x; break;
                case SPSConst.OPERATION_CHAR:
                    // Approximate attachment roots; bone animation is not needed for audition.
                    // Bone animation is not simulated; report that distinction.
                    _scriptAttachments[slot] = x; break;
                case SPSConst.OPERATION_FADE: effect.fade = (byte)x; break;
                case SPSConst.OPERATION_ABR: effect.abr = (byte)x; break;
                case SPSConst.OPERATION_FRAMERATE: effect.frameRate = x; break;
                case SPSConst.OPERATION_FRAME: effect.curFrame = x << 4; break;
                case SPSConst.OPERATION_POSOFFSET: effect.posOffset = new Vector3(x, -y, z); break;
                case SPSConst.OPERATION_DEPTHOFFSET: effect.depthOffset = x; break;
                default: throw new NotSupportedException("SPS operation " + operation);
            }
        }

        private Texture2D LoadPreviewSpsTexture(SPSEffect effect)
        {
            string key = effect.mapName + "/" + effect.spsId;
            Texture2D texture;
            if (_scriptTextureCache.TryGetValue(key, out texture)) return texture;
            UInt16[] original = PSXTextureMgr.originalVram;
            UInt16[] backup = original == null ? null : (UInt16[])original.Clone();
            try
            {
                effect.pngTexture = null;
                if (effect.mapName == "FromPrototype" || effect.mapName == "WorldMap") _previewSps.SetupSPSTexture(effect);
                else
                {
                    string path = SPSConst.GetFieldSPSTexture(effect.spsId);
                    if (path != null) effect.pngTexture = AssetManager.Load<Texture2D>("FieldMaps/FieldSPS/" + path.Split('/')[1] + ".png", true);
                }
                if (effect.pngTexture == null)
                {
                    _previewSps.IsTCBReady = false;
                    _previewSps.LoadMapTextureInVram(effect.mapName);
                    if (_previewSps.IsTCBReady)
                    {
                        effect.pngTexture = effect.GetTextureFromCurrentTCB();
                        if (effect.pngTexture != null) _scriptTextures.Add(effect.pngTexture);
                    }
                }
                if (effect.pngTexture == null) throw new InvalidOperationException("Missing SPS texture " + key);
                _scriptTextureCache[key] = effect.pngTexture;
                return effect.pngTexture;
            }
            finally
            {
                if (backup != null) Array.Copy(backup, original, backup.Length);
                PSXTextureMgr.originalVram = original; PSXTextureMgr.Reset();
            }
        }

        private void UpdateFieldScripts()
        {
            if (_previewSps == null) return;
            if (_animate) { _scriptClock += Math.Min(Time.unscaledDeltaTime, .1f); _rainTime += Math.Min(Time.unscaledDeltaTime, .1f); }
            int steps = 0;
            while (_scriptClock >= 1f / 30f && steps++ < 3)
            {
                _scriptClock -= 1f / 30f;
                if (_previewScript != null) _previewScript.Tick();
                UpdateTileAnimations();
                foreach (CapturedSps item in _capturedSps)
                {
                    SPSEffect e = item.Effect;
                    bool loopObject = _loopAllSps;
                    if (e.spsBin == null || (!loopObject && (e.attr & SPSConst.ATTR_VISIBLE) == 0) || e.lastFrame == -1) continue;
                    if (!loopObject && (e.attr & SPSConst.ATTR_UNLOAD_ON_FINISH) != 0 && (long)e.curFrame + e.frameRate >= e.frameCount) { e.Unload(); continue; }
                    e.curFrame = CapturedFrame(e.curFrame, loopObject && e.frameRate == 0 ? 16 : e.frameRate, e.frameCount, 1);
                }
            }
            foreach (CapturedSps item in _capturedSps)
            {
                SPSEffect e = item.Effect;
                bool visible = item.Visible && e.spsBin != null && e.pngTexture != null && (e.attr & SPSConst.ATTR_VISIBLE) != 0 &&
                    (e.attr & (SPSConst.ATTR_UPDATE_ANY_FRAME | SPSConst.ATTR_UPDATE_THIS_FRAME)) != 0;
                int actor;
                if (_scriptAttachments.TryGetValue(item.Source.Slot, out actor))
                {
                    e.pos = DummyActorPosition(actor);
                    if (_loopAllSps)
                    {
                        visible = item.Visible && e.spsBin != null && e.pngTexture != null;
                        if (e.fade == 0) e.fade = 128;
                    }
                }
                if (_referenceFallbackSlots.Contains(item.Source.Slot) && (!_scriptAttachments.TryGetValue(item.Source.Slot, out actor) || !_scriptPositions.ContainsKey(ResolvePreviewActor(actor))))
                    e.pos = ApproximateSpsPosition(item.Source.Slot - 256);
                int[] ground;
                if (_groundSps.TryGetValue(item.Source.Slot, out ground))
                {
                    int cycle = (int)((_rainTime * 30f * Math.Max(1, Math.Abs(e.frameRate)) + item.DisplaySlot * 112) / Math.Max(16, e.frameCount));
                    e.pos = GroundSpawnPosition(ground, cycle);
                }
                if (_loopAllSps)
                {
                    visible = item.Visible && e.spsBin != null && e.pngTexture != null;
                    if (e.fade == 0) e.fade = 128;
                    if (e.scale <= 0) e.scale = 4096;
                }
                visible = visible && _effectsPlaying;
                e.meshRenderer.enabled = visible;
                if (visible)
                {
                    if (item.Reference && _selected.Id != 2929)
                    {
                        BGCAM_DEF cam = _scene.cameraList[_cameraIndex];
                        Vector3 p = e.pos + e.posOffset;
                        Vector3 projected = cam.GetMatrixRT().MultiplyPoint(new Vector3(p.x, -p.y, p.z));
                        Vector2 offset = _projectionField.GetProjectionOffset();
                        float px = projected.x * cam.GetViewDistance() / Math.Max(1, projected.z) + offset.x;
                        float py = -projected.y * cam.GetViewDistance() / Math.Max(1, projected.z) + offset.y;
                        if (_referenceFallbackSlots.Contains(item.Source.Slot) && (projected.z <= 1 || px < _bounds.min.x || px > _bounds.max.x || py < _bounds.min.y || py > _bounds.max.y))
                            e.pos = ApproximateSpsPosition(item.Source.Slot - 256) - e.posOffset;
                    }
                    // Native SPS includes field-specific projection (notably 2929).
                    // Supply the viewed field only for this synchronous mesh call.
                    short liveField = FF9StateSystem.Common.FF9.fldMapNo;
                    try { FF9StateSystem.Common.FF9.fldMapNo = (short)_selected.Id; e.GenerateSPS(); }
                    finally { FF9StateSystem.Common.FF9.fldMapNo = liveField; }
                    // Audition all variants over the background, including effects whose
                    // original depth belongs to a hidden actor or a different scenario.
                    if (item.Reference)
                    {
                        Vector3 p = e.transform.localPosition;
                        p.z = _bounds.min.z - 10f - (item.Source.Slot - 256) * .01f;
                        e.transform.localPosition = p;
                        // Preview-owned materials: keep SPS after animated background tiles.
                        foreach (Material material in e.materials)
                            if (material != null) material.renderQueue = 4000;
                    }
                    e.lastFrame = e.curFrame;
                    e.attr &= unchecked((byte)~SPSConst.ATTR_UPDATE_THIS_FRAME);
                }
            }
            UpdateReferenceOverlays();
            _status = _capturedSps.Count(e => e.Effect.spsBin != null) + (_referenceModeUsed ? " SPS variants from the vanilla script reference (approximate placement)." : " SPS initialized by this field scripts.");
            if (_allScenarios && _selected.Id == 2709) _status = "Generator lights: preview of the script-controlled background tile pulses (not SPS).";
            if (_allScenarios && _scene.animList.Count > 0) _status += " Background tile animations: " + _scene.animList.Count + " (all scenarios).";
            if (_scriptAttachments.Count > 0) _status += " Object SPS use approximate dummy positions; bone motion is not simulated.";
            if (_previewScript != null && _previewScript.Diagnostics.Count > 0)
                _status += "\nPartial preview: " + String.Join(" | ", _previewScript.Diagnostics.Take(3).ToArray());
        }

        private void ClearFieldScripts()
        {
            _previewScript = null; _previewSps = null; _scriptClock = 0; _rainStrength = 0; _rainSpeed = 0; _rainTime = 0; _referenceModeUsed = false; _referenceFallbackSlots.Clear();
            _tileTime = 0; _tileStepBudget = 0;
            _groundSps.Clear();
            _previewAnimationTargets.Clear();
            _scriptPositions.Clear(); _scriptAttachments.Clear(); _previewPlayerUid = -1;
            foreach (GameObject dummy in _dummyActors.Values)
                if (dummy != null) UnityEngine.Object.Destroy(dummy);
            _dummyActors.Clear();
            foreach (Texture2D texture in _scriptTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
            _scriptTextures.Clear(); _scriptTextureCache.Clear();
        }
    }
}
