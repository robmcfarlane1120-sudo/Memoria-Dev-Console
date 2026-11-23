using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FF9;
using Memoria.Assets;
using Memoria.Data;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private ViewerSfxPreview _sfxPreview;
        private Boolean _sfxEscapePending, _openSfxPreview;

        private Boolean HandleSfxPreview()
        {
            if (_openSfxPreview || Input.GetKeyDown(KeyCode.F12))
            {
                _openSfxPreview = false;
                if (_sfxPreview != null) { ReturnHomeRequested = true; }
                else
                {
                    _spsSearchOpen = false;
                    _sfxPreview = new ViewerSfxPreview(_viewerCameraObject.GetComponent<Camera>());
                    _sfxPreview.Open();
                }
                return true;
            }
            if (_sfxPreview == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _sfxEscapePending = true; ReturnHomeRequested = true;
                return true;
            }
            // Disposal restores the model camera; defer it until service teardown.
            if (ReturnHomeRequested) return true;
            _sfxPreview.Update();
            return true;
        }

        private void CloseSfxPreview()
        {
            _openSfxPreview = false;
            _sfxEscapePending = false;
            if (_sfxPreview == null) return;
            try { _sfxPreview.Dispose(); }
            catch (Exception ex) { Log.Error("[SFX Preview] Cleanup failed: " + ex.Message); }
            finally { _sfxPreview = null; _lastFramedModel = null; _frameDelay = 3; }
        }

        public void DrawSfxPreview()
        {
            if (_sfxPreview != null) _sfxPreview.Draw();
        }
    }

    // Owns only dummy actors. The live battle is installed again in a finally block
    // after every native call; previews never queue a command or execute a battle SEQ.
    internal sealed class ViewerSfxPreview : IDisposable
    {
        internal sealed class Entry
        {
            internal Int32 Id;
            internal String Name;
        }
        private readonly List<Entry> _enemyModels = new List<Entry>();
        private readonly List<Entry> _arenas = new List<Entry>();
        private GameObject _arena, _floor;
        private bool _showTarget = true, _showArena = true, _hitRequested;
        private int _enemyIndex, _arenaIndex, _enemyDelta, _arenaDelta;
        private const string ArenaPreference = "MDC.SfxPreview.LastArena";
        private string _enemyId = "152", _arenaId = "1", _targetPrefix = "ANH_MON_B3_001_";
        private bool _loadEnemy, _loadArena;
        private Vector2 _panelScroll;
        private string _targetIdle = "ANH_MON_B3_001_000", _targetHit = "ANH_MON_B3_001_002";
        private readonly Dictionary<int, SB2_MON_PARM> _motionTables = new Dictionary<int, SB2_MON_PARM>();
        private IEnumerator<KeyValuePair<string, int>> _sceneScan;
        private bool _sceneScanDone;
        private int _targetGeometry = 152;
        private bool _motionResolved;
        private const float _cameraZoom = .5f;


        private void ResolveTargetMotions()
        {
            if (_motionResolved) return;
            if (_sceneScan == null) _sceneScan = FF9BattleDB.SceneData.GetEnumerator();
            // Detached scene objects only: never replace the live battle or run AI.
            // Bound asset work per update; reuse all encountered motion tables.
            for (int i = 0; i < 4 && !_sceneScanDone && !_motionTables.ContainsKey(_targetGeometry); i++)
            {
                if (!_sceneScan.MoveNext()) { _sceneScanDone = true; break; }
                try
                {
                    // SceneData also contains camera-only entries with no monster data.
                    // Probe quietly before using the normal scene reader, which logs missing files.
                    string sceneName = _sceneScan.Current.Key.Substring(4);
                    if (AssetManager.LoadBytes("BattleMap/BattleScene/EVT_BATTLE_" + sceneName + "/dbfile0000.raw16", true) == null)
                        continue;
                    var scene = new BTL_SCENE();
                    scene.ReadBattleScene(sceneName);
                    if (scene.MonAddr != null) foreach (SB2_MON_PARM entry in scene.MonAddr)
                        if (!_motionTables.ContainsKey(entry.Geo)) _motionTables.Add(entry.Geo, entry);
                }
                catch (Exception ex) { ViewerDiagnostics.Warning("[SFX Preview motions] " + ex.Message); }
            }
            SB2_MON_PARM monster;
            if (!_motionTables.TryGetValue(_targetGeometry, out monster)) return;
            string idle = null, hit = null;
            FF9BattleDB.Animation.TryGetValue(monster.Mot[0], out idle);
            FF9BattleDB.Animation.TryGetValue(monster.Mot[(int)BattlePlayerCharacter.PlayerMotionIndex.MP_DAMAGE1], out hit);
            if (idle != null) AnimationFactory.AddAnimWithAnimatioName(_target.gameObject, idle);
            if (hit != null) AnimationFactory.AddAnimWithAnimatioName(_target.gameObject, hit);
            Animation animation = _target.gameObject.GetComponent<Animation>();
            if (idle != null && animation.GetClip(idle) != null) _targetIdle = idle;
            if (hit != null && animation.GetClip(hit) != null) _targetHit = hit;
            // Use a declared alternate idle when the default asset is missing or
            // only a single pose. Never substitute an attack or death animation.
            if (!HasMovingClip(animation, _targetIdle))
            {
                string alternate;
                if (FF9BattleDB.Animation.TryGetValue(monster.Mot[1], out alternate))
                {
                    AnimationFactory.AddAnimWithAnimatioName(_target.gameObject, alternate);
                    if (HasMovingClip(animation, alternate)) _targetIdle = alternate;
                }
            }
            _motionResolved = true;
            _targetPoseTime = 0; _hitRemaining = 0;
            ViewerDiagnostics.Message("[SFX Preview motions] " + _targetGeometry + " idle=" + _targetIdle + " hit=" + _targetHit);
        }

        private static bool HasMovingClip(Animation animation, string name)
        {
            AnimationClip clip = animation == null ? null : animation.GetClip(name);
            return clip != null && clip.length > 1f / Math.Max(1f, clip.frameRate);
        }

        private void FindLoadedIdle()
        {
            Animation animation = _target.gameObject.GetComponent<Animation>();
            if (HasMovingClip(animation, _targetIdle)) return;
            string best = null;
            foreach (AnimationState state in animation)
            {
                if (!(state.name.EndsWith("_000", StringComparison.Ordinal) || state.name.EndsWith("_001", StringComparison.Ordinal))) continue;
                if (!HasMovingClip(animation, state.name)) continue;
                if (best == null || String.CompareOrdinal(state.name, best) < 0) best = state.name;
            }
            if (best != null) _targetIdle = best;
        }
        private const float PlaybackSpeed = 1.2f;
        private float _hitRemaining, _lastVisualTime, _targetPoseTime, _effectStartAt;
        private int _lastHitFrame = -1, _lastFigureFrame = -1;
        private string _pendingFigure;
        private sealed class DamagePopup { internal string Text; internal float Age; internal UILabel Label; }
        private readonly List<DamagePopup> _damagePopups = new List<DamagePopup>();

        internal static string PreviewDamageText(int roll)
        {
            return roll == 0 ? "Miss" : Math.Max(0, Math.Min(9999, roll - 1)).ToString();
        }
        private void ShowDamagePopup()
        {
            if (_damagePopups.Count >= 12) { DestroyPopup(_damagePopups[0]); _damagePopups.RemoveAt(0); }
            var popup = new DamagePopup { Text = _pendingFigure ?? PreviewDamageText(UnityEngine.Random.Range(0, 10001)) }; CreateDamageLabel(popup); _damagePopups.Add(popup); DrawDamagePopups();
            _pendingFigure = null;
        }
        private void UpdateHitVisuals()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = _lastVisualTime == 0 ? 0 : Math.Min(0.1f, now - _lastVisualTime);
            _lastVisualTime = now;
            if (_paused) return;
            if (_hitRemaining > 0)
            {
                _hitRemaining -= elapsed;
                if (_hitRemaining <= 0) _targetPoseTime = 0;
            }
            SampleTargetPose(elapsed);
            DrawDamagePopups();
            for (int i = _damagePopups.Count - 1; i >= 0; i--)
            {
                _damagePopups[i].Age += elapsed;
                if (_damagePopups[i].Age > 1.3f) { DestroyPopup(_damagePopups[i]); _damagePopups.RemoveAt(i); }
            }
        }
        private void SampleTargetPose(float elapsed)
        {
            if (_target == null) return;
            Animation animation = _target.gameObject.GetComponent<Animation>();
            string clip = _hitRemaining > 0 ? _targetHit : _targetIdle;
            if (animation == null || animation.GetClip(clip) == null) return;
            _targetPoseTime += elapsed * PlaybackSpeed;
            // Match btl_mot.PlayAnim: keep enabled, start only on a motion
            // change, freeze the automatic clock, and sample explicitly.
            animation.enabled = true;
            if (!animation.IsPlaying(clip)) animation.Play(clip);
            AnimationState state = animation[clip];
            state.enabled = true;
            state.weight = 1;
            state.speed = 0;
            state.wrapMode = _hitRemaining > 0 ? WrapMode.ClampForever : WrapMode.Loop;
            state.time = _hitRemaining > 0 ? Math.Min(_targetPoseTime, state.length) : Mathf.Repeat(_targetPoseTime, Math.Max(.001f, state.length));
            animation.Sample();
            _target.currentAnimationName = clip;
            _target.gameObject.transform.position = _target.base_pos;
            _target.gameObject.transform.rotation = _target.rot;
        }

        private GameObject _damageRoot;
        private Camera _damageCamera;
        private UIPanel _damagePanel;
        private UILabel _damageTemplate;
        private Material _damageTextMaterial;

        private static void DestroyPopup(DamagePopup popup)
        {
            if (popup.Label != null) UnityEngine.Object.Destroy(popup.Label.gameObject);
        }

        private void CreateDamageLabel(DamagePopup popup)
        {
            if (_damageTemplate == null)
                foreach (HUDMessage hud in Resources.FindObjectsOfTypeAll<HUDMessage>())
                    if (hud.prototype != null) { var labels = hud.prototype.GetComponentsInChildren<UILabel>(true); _damageTemplate = labels.Length > 0 ? labels[0] : null; if (_damageTemplate != null) break; }
            if (_damageTemplate == null) { ViewerDiagnostics.Warning("[SFX Preview] Native HUD damage-label template unavailable."); return; }
            if (_damageRoot == null)
            {
                _damageRoot = new GameObject("Preview native damage HUD");
                _damageRoot.layer = 30;
                _damageRoot.transform.position = Vector3.zero; // Isolated by layer 30; keep UI geometry near the origin.

                var cameraObject = new GameObject("Preview damage camera");
                cameraObject.transform.parent = _damageRoot.transform;
                cameraObject.transform.localPosition = new Vector3(0, 0, -10);
                _damageCamera = cameraObject.AddComponent<Camera>();
                _damageCamera.orthographic = true;
                _damageCamera.orthographicSize = Screen.height / 2f;
                _damageCamera.nearClipPlane = .1f; _damageCamera.farClipPlane = 20;
                _damageCamera.cullingMask = 1 << 30;
                _damageCamera.clearFlags = CameraClearFlags.Depth;
                _damageCamera.depth = _camera.depth + 10;
                _damagePanel = _damageRoot.AddComponent<UIPanel>();
                // Only the clipped alpha-text shader is supplied in Memoria's
                // shader overrides. An unclipped panel falls back to RGB texture
                // multiplication, which renders this font's black RGB atlas black.
                _damagePanel.clipping = UIDrawCall.Clipping.SoftClip;
                _damagePanel.baseClipRegion = new Vector4(0, 0, Screen.width + 200, Screen.height + 200);
                _damagePanel.clipSoftness = Vector2.zero;
                ViewerDiagnostics.Message("[SFX Preview] Native HUD font=" + _damageTemplate.ambigiousFont + " size=" + _damageTemplate.fontSize);
            }
            var labelObject = new GameObject("Preview damage number");
            labelObject.layer = 30;
            labelObject.transform.parent = _damageRoot.transform;
            labelObject.transform.localPosition = Vector3.zero;
            labelObject.transform.localScale = Vector3.one;
            UILabel label = labelObject.AddComponent<UILabel>();
            if (_damageTemplate.bitmapFont != null) label.bitmapFont = _damageTemplate.bitmapFont;
            else label.trueTypeFont = _damageTemplate.trueTypeFont ?? EncryptFontManager.SetDefaultFont();
            label.fontSize = _damageTemplate.fontSize;
            label.fontStyle = _damageTemplate.fontStyle;
            // The font atlas contains an alpha mask; use the unlit text shader,
            // rather than inheriting a GUI/font material's colour/depth state.
            if (_damageTextMaterial == null)
            {
                Shader textShader = Shader.Find("GUI/Text Shader");
                _damageTextMaterial = new Material(textShader != null ? textShader : label.material.shader);
                _damageTextMaterial.mainTexture = label.material.mainTexture;
                if (_damageTextMaterial.HasProperty("_Color")) _damageTextMaterial.SetColor("_Color", Color.white);
            }
            label.material = _damageTextMaterial;
            label.effectStyle = UILabel.Effect.None;
            label.onPostFill = (widget, offset, vertices, uvs, colors) =>
            {
                if (label.bitmapFont == null && label.trueTypeFont != null)
                    _damageTextMaterial.mainTexture = label.trueTypeFont.material.mainTexture;
                // Keep opacity from native text layout, but make the foreground
                // white independently of parser tint or inherited shadow colours.
                for (int i = offset; i < colors.size; i++)
                    colors[i] = new Color32(255, 255, 255, colors[i].a);
            };
            label.effectColor = Color.black;
            label.effectDistance = Vector2.one;
            label.pivot = UIWidget.Pivot.Center;
            label.overflowMethod = UILabel.Overflow.ResizeFreely;
            label.color = Color.white;
            label.DefaultTextColor = Color.white;
            label.applyGradient = false;
            label.gradientTop = label.gradientBottom = Color.white;
            label.rawText = "[FFFFFF]" + (popup.Text == "Miss" ? Localization.Get("Miss") : popup.Text) + "[-]";
            popup.Label = label;
            ViewerDiagnostics.Message("[SFX Preview damage] resolved font=" + label.ambigiousFont + " text=" + popup.Text);
        }

        private void DrawDamagePopups()
        {
            if (_target == null || _damagePopups.Count == 0) return;
            Vector3 anchor = _target.gameObject.transform.position;
            Renderer renderer = _target.gameObject.GetComponentInChildren<Renderer>();
            if (renderer != null) anchor = renderer.bounds.center;
            Vector3 screen = _camera.WorldToScreenPoint(anchor);
            if (_damagePanel != null) _damagePanel.baseClipRegion = new Vector4(0, 0, Screen.width + 200, Screen.height + 200);
            if (_damageCamera != null) { _damageCamera.orthographicSize = Screen.height / 2f; _damageCamera.aspect = (float)Screen.width / Screen.height; }
            foreach (DamagePopup popup in _damagePopups)
            {
                if (popup.Label == null) continue;
                popup.Label.enabled = screen.z > 0;
                popup.Label.transform.localPosition = new Vector3(screen.x - Screen.width / 2f, screen.y - Screen.height / 2f + 70 + popup.Age * 35, 0);
            }
        }
        private bool _audioEnabled = true;
        private int _savedTargetFps;
        private readonly HashSet<int> _previewSounds = new HashSet<int>();

        private void BuildStageCatalogs()
        {
            foreach (var pair in FF9BattleDB.GEO)
                if (pair.Value.StartsWith("GEO_MON_", StringComparison.Ordinal))
                    _enemyModels.Add(new Entry { Id = pair.Key, Name = pair.Value });
            _enemyModels.Sort((a,b) => a.Id.CompareTo(b.Id));
            var field = typeof(ModelViewerScene).GetField("geoList", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var list = field.GetValue(null) as System.Collections.IEnumerable;
            var seen = new HashSet<int>();
            if (list != null) foreach (object model in list)
            {
                string name = (string)model.GetType().GetField("Name").GetValue(model);
                int id;
                if (name.StartsWith("BBG_B") && Int32.TryParse(name.Substring(5), out id) && seen.Add(id))
                    _arenas.Add(new Entry { Id = id, Name = name });
            }
            _arenas.Sort((a,b) => a.Id.CompareTo(b.Id));
            _enemyIndex = Math.Max(0, _enemyModels.FindIndex(e => e.Id == 152));
        }

        private void ApplyStageVisibility()
        {
            // Retain bones while hidden so native effects can still target them.
            if (_target != null) foreach (Renderer r in _target.gameObject.GetComponentsInChildren<Renderer>(true)) r.enabled = _showTarget;
            if (_arena != null) _arena.SetActive(_showArena);
            if (_floor != null) _floor.SetActive(!_showArena || _arena == null);
        }

        private void StopForStageChange()
        {
            StopPreviewAudio();
            _playing = _warming = false;
            if (SFXRender.commandBuffer != null) SFXRender.commandBuffer.Clear();
            ResetStageCamera();
        }

        private void ChangeTarget(Entry entry)
        {
            StopForStageChange();
            string prefix = entry.Name.Replace("GEO_", "ANH_") + "_";
            BTL_DATA replacement = CreateActor(entry.Name, prefix, 16, false, new Vector3(0f, 0f, 1200f));
            BTL_DATA old = _target;
            _target = replacement; _targetPrefix = prefix;
            _targetGeometry = entry.Id; _targetIdle = prefix + "000"; _targetHit = prefix + "002";
            _motionResolved = false; FindLoadedIdle();
            _caster.next = replacement; _state.btl_data[4] = replacement;
            if (old != null)
            {
                GameObject shadow;
                if (_state.map.shadowArray.TryGetValue(old, out shadow)) { UnityEngine.Object.Destroy(shadow); _state.map.shadowArray.Remove(old); }
                UnityEngine.Object.Destroy(old.gameObject);
            }
            _enemyId = entry.Id.ToString();
            ResetActors(); ApplyStageVisibility();
            _status = "Target: " + entry.Id + " — " + entry.Name;
        }

        private void ChangeArena(Entry entry)
        {
            StopForStageChange();
            string path = "BattleMap/BattleModel/battleMap_all/" + entry.Name + "/" + entry.Name;
            GameObject stage = ModelFactory.CreateModel(path, true, true, Configuration.Graphics.BattleSmoothTexture);
            if (stage == null) throw new InvalidOperationException("Could not load arena " + entry.Id);
            stage.transform.parent = _root.transform;
            stage.transform.localPosition = Vector3.zero;
            stage.transform.localRotation = Quaternion.identity; // Same basis as HonoluluBattleMain.
            stage.transform.localScale = Vector3.one;
            foreach (Transform child in stage.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            int previous = battlebg.nf_BbgNumber;
            try { battlebg.nf_BbgNumber = entry.Id; battlebg.SetDefaultShader(stage); }
            catch { UnityEngine.Object.Destroy(stage); throw; }
            finally { battlebg.nf_BbgNumber = previous; }
            if (_arena != null) UnityEngine.Object.Destroy(_arena);
            _arena = stage; _state.map.btlBGPtr = stage; _arenaId = entry.Id.ToString();
            PlayerPrefs.SetInt(ArenaPreference, entry.Id);
            PlayerPrefs.Save();
            ApplyStageVisibility();
            _status = "Arena: " + entry.Id;
        }

        private void PlayTargetHit()
        {
            Animation anim = _target.gameObject.GetComponent<Animation>();
            string hit = _targetHit;
            if (anim == null || anim.GetClip(hit) == null) { _status = "No loaded damage clip for this model: " + hit; return; }
            if (_hitRemaining > 0) return;
            _targetPoseTime = 0;
            _hitRemaining = Math.Max(0.05f, anim[hit].length / PlaybackSpeed);
            ViewerDiagnostics.Message("[SFX Preview hit] model=" + _targetGeometry + " clip=" + hit + " seconds=" + anim[hit].length + " playback=" + _hitRemaining);
            SampleTargetPose(0);
        }

        private void UpdateStageRequests()
        {
            if (_loadEnemy || _enemyDelta != 0)
            {
                int id, next = -1;
                int direction = Math.Sign(_enemyDelta);
                if (_loadEnemy) { if (Int32.TryParse(_enemyId, out id)) next = _enemyModels.FindIndex(e => e.Id == id); }
                else if (_enemyModels.Count > 0) next = (_enemyIndex + _enemyDelta % _enemyModels.Count + _enemyModels.Count) % _enemyModels.Count;
                _loadEnemy = false; _enemyDelta = 0;
                if (next >= 0)
                {
                    // Some catalog entries have no installed asset. Do not trap
                    // both arrow buttons behind the same failed model forever.
                    for (int attempt = 0; attempt < _enemyModels.Count; attempt++)
                    {
                        _enemyIndex = next;
                        try { ChangeTarget(_enemyModels[next]); break; }
                        catch (Exception ex)
                        {
                            _status = "Unavailable target " + _enemyModels[next].Id + ": " + ex.Message;
                            ViewerDiagnostics.Warning("[SFX Preview target] " + _status);
                            if (direction == 0) break;
                            next = (next + direction + _enemyModels.Count) % _enemyModels.Count;
                        }
                    }
                }
                else _status = "Unknown enemy model ID.";
            }
            if (_loadArena || _arenaDelta != 0)
            {
                int id, next = -1;
                if (_loadArena) { if (Int32.TryParse(_arenaId, out id)) next = _arenas.FindIndex(e => e.Id == id); }
                else if (_arenas.Count > 0) next = (_arenaIndex + _arenaDelta % _arenas.Count + _arenas.Count) % _arenas.Count;
                _loadArena = false; _arenaDelta = 0;
                if (next >= 0) { _arenaIndex = next; ChangeArena(_arenas[next]); }
                else _status = "Unknown arena ID.";
            }
            ApplyStageVisibility();
            if (_hitRequested) { _hitRequested = false; if (_paused) TogglePause(); PlayTargetHit(); ShowDamagePopup(); }
        }

        private static bool DrawStageCheckbox(bool value, string text, string help)
        {
            GUILayout.BeginHorizontal();
            Color old = GUI.backgroundColor;
            if (value) GUI.backgroundColor = Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f);
            if (GUILayout.Button(new GUIContent(value ? "✓" : "", help), GUILayout.Width(30), GUILayout.Height(30))) value = !value;
            GUI.backgroundColor = old;
            GUILayout.Space(8);
            if (GUILayout.Button(new GUIContent(text, help), GUI.skin.label, GUILayout.Height(30))) value = !value;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            return value;
        }
        private void DrawStageControls()
        {
            GUILayout.Space(18);
            GUILayout.Label("TARGET MODEL");
            _showTarget = DrawStageCheckbox(_showTarget, "Show enemy model", "Show or hide the target model. Effects still target its position and bones while it is hidden.");
            GUILayout.Label(_enemyModels.Count > 0 ? _enemyModels[_enemyIndex].Name : "No enemy models");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("< Enemy", "Load the previous available enemy model. Changing the target stops the current SFX."))) _enemyDelta--;
            _enemyId = GUILayout.TextField(_enemyId, 8);
            ViewerHoverHelp.Last("Enter an enemy geometry ID, then click Load ID. Example: 152 is GEO_MON_B3_001.");
            if (GUILayout.Button(new GUIContent("Load ID", "Load the enemy geometry ID entered here. This is the geometry database ID, not the numeric suffix in its model name."))) _loadEnemy = true;
            if (GUILayout.Button(new GUIContent("Enemy >", "Load the next available enemy model. Missing assets are skipped."))) _enemyDelta++;
            GUILayout.EndHorizontal();
            if (GUILayout.Button(new GUIContent("Preview hit reaction", "Play the target's damage animation and show a random damage result without playing an SFX. Not every model has a hit animation."))) _hitRequested = true;
            GUILayout.Space(14);
            GUILayout.Label("BATTLE ARENA");
            _showArena = DrawStageCheckbox(_showArena, "Show battle background", "Show the selected battle arena, or hide it to use the simple preview floor.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("< Arena", "Load the previous battle background. Changing arenas stops the current SFX."))) _arenaDelta--;
            _arenaId = GUILayout.TextField(_arenaId, 8);
            ViewerHoverHelp.Last("Enter a battle background ID, then click Load ID.");
            if (GUILayout.Button(new GUIContent("Load ID", "Load the exact battle arena ID entered here."))) _loadArena = true;
            if (GUILayout.Button(new GUIContent("Arena >", "Load the next battle background. The last successfully selected arena is remembered."))) _arenaDelta++;
            GUILayout.EndHorizontal();
            GUILayout.Label("Changing models or arenas stops the effect. Press Play to restart.");
        }

        private readonly Camera _camera;
        private readonly List<Entry> _catalog = BuildCatalog();
        private readonly List<Entry> _matches = new List<Entry>();
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private readonly List<UIWidget> _labels = new List<UIWidget>();
        private readonly List<Camera> _mainCameras = new List<Camera>();
        private readonly Dictionary<FieldInfo, System.Object> _savedSfx = new Dictionary<FieldInfo, System.Object>();
        private readonly List<FieldInfo> _layoutCameras = new List<FieldInfo>();
        private UInt16[] _savedVram;
        private Int32[,] _savedChannels;
        private FF9StateBattleSystem _state;
        private GameObject _root;
        private BTL_DATA _caster, _target;
        private Matrix4x4 _savedView, _savedProjection, _stageView, _stageProjection;
        private Int32 _savedCullingMask;
        private Boolean _skipBlankLeadIn = true, _warming;
        private Int32 _skippedFrames;
        private const Int32 PreviewLayer = 31, MaxBlankFrames = 180;
        private Vector3 _savedCameraPosition, _savedCameraScale;
        private Quaternion _savedCameraRotation;
        private String _savedCameraTag;
        private Color _savedBackground;
        private SFX_INIT_PARAM _restoreInit;
        private Boolean _restoreNative, _initialized, _playing, _paused, _disposed, _startedCommon, _nativeChanged;
        private Boolean _playRequested, _stopRequested;
        private Int32 _selected, _pendingDelta, _frame;
        private Single _nextFrame;
        private String _query = "", _lastQuery, _status = "Choose an effect, then Play. SFX audio is enabled.";


        internal ViewerSfxPreview(Camera camera) { _camera = camera; }

        internal static List<Entry> BuildCatalog()
        {
            var entries = new List<Entry>();
            var used = new HashSet<Int32>();
            foreach (SpecialEffect effect in Enum.GetValues(typeof(SpecialEffect)))
            {
                Int32 id = (Int32)effect;
                if (id < 0 || id >= SFX.playParam.Length || !used.Add(id)) continue;
                entries.Add(new Entry { Id = id, Name = effect.ToString().Replace("__", " / ").Replace('_', ' ') });
            }
            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            return entries;
        }

        internal static Boolean Matches(Entry entry, String query)
        {
            query = (query ?? "").Trim();
            Int32 id;
            return query.Length == 0 || (Int32.TryParse(query, out id)
                ? entry.Id == id : entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal unsafe void Open()
        {
            _savedTargetFps = Application.targetFrameRate;
            Application.targetFrameRate = Math.Max(60, _savedTargetFps);
            _savedCullingMask = _camera.cullingMask;
            _savedView = _camera.worldToCameraMatrix;
            _savedProjection = _camera.projectionMatrix;
            _savedBackground = _camera.backgroundColor;
            _savedCameraPosition = _camera.transform.position;
            _savedCameraScale = _camera.transform.localScale;
            _savedCameraRotation = _camera.transform.rotation;
            _savedCameraTag = _camera.gameObject.tag;
            try
            {
                // The native plugin has one effect engine. Do not replace an effect
                // suspended halfway through a real command by opening the viewer.
                if (SFX.isRunning || SFXData.LoadCur != null || UnifiedBattleSequencer.runningActions.Count != 0)
                    throw new InvalidOperationException("Return to the game and open the viewer between attacks; an SFX is still suspended.");
                _restoreNative = SFX.isSystemRun;
                SaveNativeState();
                if (_restoreNative) _restoreInit = CaptureLiveInit();
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    foreach (String typeName in new[] { "Memoria.DevConsole.BattleLayoutRuntime", "Memoria.DevConsole.BattleSceneEditor.BattleLayoutRuntime", "LostChapters.BattleSceneLayouts.BattleLayoutRuntime", "Memoria.BattleSceneLayouts.BattleLayoutRuntime" })
                    {
                        Type type = assembly.GetType(typeName, false);
                        FieldInfo field = type == null ? null : type.GetField("ActiveCamera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (field != null && !field.IsInitOnly) _layoutCameras.Add(field);
                    }
                _state = new FF9StateBattleSystem();
                BuildStageCatalogs();
                _root = new GameObject("MDC_SFX_Preview");
                _caster = CreateActor("GEO_MAIN_B0_000", "ANH_MAIN_B0_000_", 1, true, new Vector3(0f, 0f, -1200f));
                _target = CreateActor("GEO_MON_B3_001", _targetPrefix, 16, false, new Vector3(0f, 0f, 1200f));
                _state.btl_list.next = _caster; _caster.next = _target;
                FindLoadedIdle();
                _state.btl_data[0] = _caster; _state.btl_data[4] = _target;
                _state.btl_cnt = 2; _state.btl_phase = FF9StateBattleSystem.PHASE_NORMAL;
                HideViewerObjects();
                foreach (Camera camera in UnityEngine.Object.FindObjectsOfType<Camera>())
                    if (camera != _camera && camera.CompareTag("MainCamera"))
                    { _mainCameras.Add(camera); camera.gameObject.tag = "Untagged"; }
                _camera.gameObject.tag = "MainCamera";
                _camera.cullingMask = 1 << PreviewLayer;
                _camera.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
                _camera.transform.position = new Vector3(-3800f, 1800f, -6000f);
                _camera.transform.LookAt(new Vector3(0f, 300f, 0f));
                _camera.transform.localScale = new Vector3(1f, -1f, 1f);
                _camera.ResetWorldToCameraMatrix();
                _camera.projectionMatrix = PsxCamera.PsxProj2UnityProj(450f, 65535f);
                _stageView = _camera.worldToCameraMatrix; _stageProjection = _camera.projectionMatrix;
                CreateFloor();
                _showArena = true;
                int preferredArena = PlayerPrefs.GetInt(ArenaPreference, 1);
                var startupArenas = new List<Entry>(_arenas);
                startupArenas.Sort((a, b) => (a.Id == preferredArena ? -2 : a.Id == 1 ? -1 : a.Id).CompareTo(b.Id == preferredArena ? -2 : b.Id == 1 ? -1 : b.Id));
                foreach (Entry arena in startupArenas)
                {
                    if (arena.Id == 0) continue; // Native blank/debug arena.
                    try { ChangeArena(arena); _arenaIndex = _arenas.IndexOf(arena); break; }
                    catch (Exception ex) { ViewerDiagnostics.Warning("[SFX Preview arena] " + ex); }
                }
                WithContext(() => {
                    if (!_restoreNative) { if (PSXTextureMgr.originalVram == null) PSXTextureMgr.InitOnce(); SFX.StartCommon(false); _startedCommon = true; }
                    SFX.hijackedCallback = PreviewCallback;
                    SFX.isDebugMode = false;
                    _nativeChanged = true;
                    InitializeNative(BuildPreviewInit());
                });
                _initialized = true;
                Camera.onPostRender += Render;
                ResetActors();
            }
            catch (Exception ex)
            {
                _status = "Preview unavailable: " + ex.Message;
                ViewerDiagnostics.Warning("[Dev Console][SFX Preview] " + ex);
                try { RestoreNativeState(); }
                finally { ReleaseScene(); }
            }
        }

        private BTL_DATA CreateActor(String model, String prefix, UInt16 id, Boolean player, Vector3 position)
        {
            GameObject go = ModelFactory.CreateModel(model, false, true, Configuration.Graphics.ElementsSmoothTexture);
            if (go == null) throw new InvalidOperationException("Could not load " + model);
            go.transform.parent = _root.transform;
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            go.layer = PreviewLayer;
            go.SetActive(true);
            go.transform.localScale = Vector3.one;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
                SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                if (skin != null) skin.updateWhenOffscreen = true;
            }
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, player ? 180f : 0f, 180f);
            var actor = new BTL_DATA { gameObject = go, btl_id = id, base_pos = position,
                original_pos = position, pos = position, rot = go.transform.rotation, mot = new String[34],
                geo_scale_x = 4096, geo_scale_y = 4096, geo_scale_z = 4096, flags = geo.GEO_FLAGS_RENDER };
            actor.bi.player = player ? (Byte)1 : (Byte)0;
            actor.bi.slot_no = 0; actor.bi.target = 1;
            actor.cur.hp = actor.max.hp = 9999;
            actor.evt.rotBattle = actor.rot; actor.evt.posBattle = position;
            actor.tar_bone = go.transform.GetChildByName("bone007") != null ? (Byte)7 : (Byte)0;
            foreach (var animation in FF9DBAll.AnimationDB)
                if (animation.Value.StartsWith(prefix, StringComparison.Ordinal))
                    AnimationFactory.AddAnimWithAnimatioName(go, animation.Value);
            // Include the native model animation mapping (including shared rigs),
            // not only clips whose names happen to match the geometry prefix.
            AnimationFactory.AddAnimToGameObject(go, model, false);
            for (Int32 i = 0; i < actor.mot.Length; i++) actor.mot[i] = prefix + (player ? BattlePlayerCharacter.PlayerMotionNum[i] : "000");
            _state.map.shadowArray[actor] = new GameObject("PreviewShadow");
            _state.map.shadowArray[actor].transform.parent = _root.transform;
            return actor;
        }

        private void CreateFloor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floor = floor;
            floor.name = "Preview floor"; floor.transform.parent = _root.transform;
            floor.layer = PreviewLayer; floor.transform.localScale = new Vector3(700f, 1f, 700f);
            var material = new Material(Shader.Find("Unlit/Color") ?? floor.GetComponent<Renderer>().sharedMaterial.shader);
            material.color = new Color(0.13f, 0.16f, 0.21f);
            floor.GetComponent<Renderer>().material = material;
            UnityEngine.Object.Destroy(floor.GetComponent<Collider>());
            _state.map.btlBGPtr = _root;
        }

        private void HideViewerObjects()
        {
            foreach (String name in new[] { "currentModel", "currentWeaponModel", "currentFloorModel", "backgroundGo" })
            {
                FieldInfo field = typeof(ModelViewerScene).GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var go = field == null ? null : field.GetValue(null) as GameObject;
                if (go != null && go.activeSelf) { _hidden.Add(go); go.SetActive(false); }
            }
            foreach (String name in new[] { "controlLabel", "infoLabel", "extraInfoLabel" })
            {
                FieldInfo field = typeof(ModelViewerScene).GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var widget = field == null ? null : field.GetValue(null) as UIWidget;
                if (widget != null && widget.enabled) { _labels.Add(widget); widget.enabled = false; }
            }
            foreach (String name in new[] { "boneModels", "boneConnectModels" })
            {
                FieldInfo field = typeof(ModelViewerScene).GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var objects = field == null ? null : field.GetValue(null) as System.Collections.IEnumerable;
                if (objects == null) continue;
                foreach (System.Object item in objects)
                {
                    GameObject go = item as GameObject;
                    if (go != null && go.activeSelf) { _hidden.Add(go); go.SetActive(false); }
                }
            }
        }

        private void SaveNativeState()
        {
            foreach (FieldInfo field in typeof(SFX).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (!field.IsInitOnly && !field.IsLiteral && (field.FieldType.IsValueType || typeof(Delegate).IsAssignableFrom(field.FieldType)))
                    _savedSfx[field] = field.GetValue(null);
            if (PSXTextureMgr.originalVram != null) _savedVram = (UInt16[])PSXTextureMgr.originalVram.Clone();
            if (SFX.channel != null) _savedChannels = (Int32[,])SFX.channel.Clone();
        }

        private SFX_INIT_PARAM CaptureLiveInit()
        {
            var init = new SFX_INIT_PARAM();
            Int32 index = 0;
            for (BTL_DATA actor = FF9StateSystem.Battle.FF9Battle.btl_list.next; actor != null && index < 8; actor = actor.next)
                SFX.InitBattleCharacter(ref init, index++, actor);
            Single[] native = new Single[13];
            Marshal.Copy(SFX.SFX_UpdateCamera(0), native, 0, native.Length);
            SetInitCamera(ref init, PsxCamera.PsxMatrix2UnityMatrix(native, 0f), (Int16)native[12]);
            return init;
        }

        private SFX_INIT_PARAM BuildPreviewInit()
        {
            var init = new SFX_INIT_PARAM();
            init.btl_data_init0 = new BTL_DATA_INIT { btl_id = 1, bi_player = 1, tar_bone = _caster.tar_bone,
                player_serial_no = 0, enemy_radius = 200, geo_radius = 200, geo_height = 600 };
            init.btl_data_init4 = new BTL_DATA_INIT { btl_id = 16, tar_bone = _target.tar_bone,
                enemy_radius = 300, geo_radius = 300, geo_height = 700,
                enemy_cam_bone0 = _target.tar_bone, enemy_cam_bone1 = _target.tar_bone, enemy_cam_bone2 = _target.tar_bone };
            // Match the native debug room's complete slot metadata. Some effects
            // reference other party/enemy slots even with a single selected target.
            init.btl_data_init1 = PreviewSlot(init.btl_data_init0, 1, true);
            init.btl_data_init2 = PreviewSlot(init.btl_data_init0, 2, true);
            init.btl_data_init3 = PreviewSlot(init.btl_data_init0, 3, true);
            init.btl_data_init5 = PreviewSlot(init.btl_data_init4, 5, false);
            init.btl_data_init6 = PreviewSlot(init.btl_data_init4, 6, false);
            init.btl_data_init7 = PreviewSlot(init.btl_data_init4, 7, false);
            SetInitCamera(ref init, _camera.worldToCameraMatrix, 450);
            return init;
        }

        internal static BTL_DATA_INIT PreviewSlot(BTL_DATA_INIT source, Int32 slot, Boolean player)
        {
            source.btl_id = (UInt16)(1 << slot);
            source.bi_player = player ? (Byte)1 : (Byte)0;
            source.bi_slot_no = (Byte)(slot % 4);
            source.bi_line_no = (Byte)slot;
            return source;
        }

        private static unsafe void SetInitCamera(ref SFX_INIT_PARAM init, Matrix4x4 view, Int16 projection)
        {
            Int16[,] rotation = new Int16[3, 3]; Int32[] translation = new Int32[3];
            PsxCamera.UnityMatrix2PsxMatrix(view, rotation, translation);
            fixed (Int16* r = init.camMat) for (Int32 i = 0; i < 9; i++) r[i] = rotation[i / 3, i % 3];
            fixed (Int32* t = init.camTrans) for (Int32 i = 0; i < 3; i++) t[i] = translation[i];
            init.camProj = projection;
        }

        private static void InitializeNative(SFX_INIT_PARAM init)
        {
            GCHandle handle = GCHandle.Alloc(init, GCHandleType.Pinned);
            try { SFX.SFX_InitBattle(handle.AddrOfPinnedObject()); }
            finally { handle.Free(); }
        }

        private unsafe void WithContext(Action action)
        {
            FF9StateBattleSystem live = FF9StateSystem.Battle.FF9Battle;
            SFX.Callback callback = SFX.hijackedCallback;
            var cameras = new System.Object[_layoutCameras.Count];
            try
            {
                // Live formation corrections must not reproject dummy preview actors.
                for (Int32 i = 0; i < cameras.Length; i++) { cameras[i] = _layoutCameras[i].GetValue(null); _layoutCameras[i].SetValue(null, null); }
                FF9StateSystem.Battle.FF9Battle = _state;
                SFX.hijackedCallback = PreviewCallback;
                action();
            }
            finally
            {
                SFX.hijackedCallback = callback; FF9StateSystem.Battle.FF9Battle = live;
                for (Int32 i = 0; i < cameras.Length; i++) _layoutCameras[i].SetValue(null, cameras[i]);
            }
        }

        private void StopPreviewAudio()
        {
            foreach (int sound in _previewSounds) SoundLib.StopSfxSound(sound);
            _previewSounds.Clear();
        }
        private void PreviewSound(int operation, int sound, int attr, int sequence)
        {
            if (!_audioEnabled) return;
            if (operation == 0) SFX.SoundPlay(sound, attr, sequence);
            else if (operation == 1) SFX.SoundPlayChant(sound, attr, sequence);
            else if (operation == 2) SFX.SoundStop(sound, attr);
            else if (operation == 3) SFX.StreamPlay(sound);
            if (SFX.channel != null)
                for (int i = 0; i < SFX.channel.GetLength(0); i++)
                    if (SFX.channel[i, 0] != -1) _previewSounds.Add(SFX.channel[i, 1]);
        }
        private void TogglePause()
        {
            _paused = !_paused;
            _nextFrame = Time.realtimeSinceStartup;
            if (_paused) SoundLib.SfxSoundPlayer.PauseAllSounds();
            else SoundLib.SfxSoundPlayer.ResumeAllSounds();
        }

        private unsafe Int32 PreviewCallback(Int32 fullCode, Int32 arg0, Int32 arg1, Int32 arg2, Int32 arg3, void* p)
        {
            Int32 code = fullCode >> 24;
            if (code == 32) { PreviewSound(arg0, arg1, arg2, arg3); return 0; }
            if (code == 125) { SFX.soundFPS = arg0; return 0; }
            // Deliberately do not forward damage, statuses, variables,
            // camera-phase commands, actor visibility or scene-transition callbacks.
            // No delegation to the game's callbacks: even their read paths can
            // sample animations or be patched by other mods against live actors.
            if (code == 100) { PSXTextureMgr.LoadImage(arg0, arg1, arg2, arg3, (UInt16*)p); return 0; }
            if (code == 101) { PSXTextureMgr.StoreImage(arg0, arg1, arg2, arg3, (UInt16*)p); return 0; }
            if (code == 102) { PSXTextureMgr.MoveImage(arg0, arg1, (Int16*)p); return 0; }
            BTL_DATA actor = IsPreviewPlayerId(fullCode & 255) ? _caster : _target;
            if (actor == null) return 0;
            if (code == 1)
            {
                Vector3 pos = arg0 == 1 || arg0 == 3 ? actor.base_pos : actor.pos;
                Int16* dst = (Int16*)p; dst[0] = (Int16)pos.x;
                if (arg0 < 2) dst[1] = (Int16)(-pos.y);
                dst[2] = (Int16)pos.z; return 0;
            }
            if (code == 3)
            {
                Vector3 angle = arg0 == 0 ? Vector3.zero : actor.rot.eulerAngles;
                Int16* dst = (Int16*)p;
                dst[0] = (Int16)(angle.x * 11.3777781f);
                dst[1] = (Int16)(angle.y * 11.3777781f);
                dst[2] = (Int16)((angle.z - (arg0 == 0 ? 0f : 180f)) * 11.3777781f);
                return 0;
            }
            if (code == 5) { Int32* dst = (Int32*)p; dst[0] = dst[1] = dst[2] = 4096; return 0; }
            if (code == 7) return actor.flags;
            if (code == 9 || code == 10)
            {
                Animation animation = actor.gameObject.GetComponent<Animation>();
                if (animation != null) foreach (AnimationState state in animation)
                    if (animation.IsPlaying(state.name))
                        return code == 9 ? Math.Max(1, (Int32)(state.length * 15f)) : (Int32)(state.time * 15f);
                return code == 9 ? 1 : 0;
            }
            if (code == 12)
            {
                if (actor == _caster && arg0 >= 0 && arg0 < actor.mot.Length) PlayAnimation(actor, actor.mot[arg0], true);
                return 0;
            }
            if (code == 14)
            {
                Transform bone = actor.gameObject.transform.GetChildByName("bone" + arg1.ToString("D3")) ?? actor.gameObject.transform;
                Matrix4x4 matrix = bone.localToWorldMatrix;
                if (arg0 == 0) { Int16* dst = (Int16*)p; dst[0] = (Int16)matrix.m03; dst[1] = (Int16)(-matrix.m13); dst[2] = (Int16)matrix.m23; }
                else if (arg0 == 1) return (Int32)(-matrix.m13);
                else if (arg0 == 2)
                {
                    SFX.PSXMAT* dst = (SFX.PSXMAT*)p;
                    dst->pad = 0;
                    for (Int32 i = 0; i < 9; i++)
                    {
                        Int32 row = i / 3, col = i % 3;
                        Single sign = BoneMatrixSign(row, col);
                        dst->r[i] = (Int16)(matrix[row, col] * sign * 4096f);
                    }
                    dst->t[0] = (Int32)matrix.m03; dst->t[1] = (Int32)(-matrix.m13); dst->t[2] = (Int32)matrix.m23;
                }
                return 0;
            }
            if (code == 15 || code == 115 || code == 123) return 1;
            if (code == 117) return 128;
            if (code == 121) return 1;
            if (code == 23 && _lastHitFrame != _frame)
            {
                _lastHitFrame = _frame;
                _pendingFigure = PreviewDamageText(UnityEngine.Random.Range(0, 10001));
                if (_pendingFigure != "Miss") PlayTargetHit();
            }
            if (code == 24 && _lastFigureFrame != _frame)
            {
                _lastFigureFrame = _frame;
                if (_lastHitFrame < 0) PlayTargetHit();
                ShowDamagePopup();
            }
            return 0;
        }

        internal static Boolean IsPreviewPlayerId(Int32 id) { return id != 0 && (id & 15) != 0 && (id & 240) == 0; }

        internal static Single BoneMatrixSign(Int32 row, Int32 col)
        {
            // Match SFXData.BattleCallbackReader exactly, including its third row.
            return row == 2 ? 1f : row == 1 ? (col == 1 ? 1f : -1f) : (col == 2 ? 1f : -1f);
        }
        internal void Update()
        {
            if (!_initialized || _disposed) return;
            ResolveTargetMotions();
            UpdateHitVisuals();
            foreach (BTL_DATA actor in new[] { _caster, _target })
            {
                Animation animation = actor.gameObject.GetComponent<Animation>();
                if (animation != null && actor == _caster) animation.enabled = !_paused;
            }
            if (GUIUtility.keyboardControl == 0)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) _pendingDelta--;
                if (Input.GetKeyDown(KeyCode.RightArrow)) _pendingDelta++;
                if (Input.GetKeyDown(KeyCode.Space)) _playRequested = true;
            }
            try
            {
                UpdateStageRequests();
                if (_pendingDelta != 0)
                {
                    _selected = (_selected + _pendingDelta % _catalog.Count + _catalog.Count) % _catalog.Count;
                    _pendingDelta = 0; _playRequested = true; _revealSelection = true;
                }
                if (_stopRequested) { StopPreviewAudio(); _stopRequested = false; _playing = false; _warming = false; SFXRender.commandBuffer.Clear(); ResetStageCamera(); ResetActors(); _status = "Stopped."; }
                if (_playRequested) { _playRequested = false; Play(); }
                if (Time.realtimeSinceStartup < _effectStartAt) return;
                if (!_playing || _paused || (!_warming && Time.realtimeSinceStartup < _nextFrame)) return;
                // Advance from the previous deadline, avoiding cumulative frame drift.
                if (_nextFrame < Time.realtimeSinceStartup - 0.3f) _nextFrame = Time.realtimeSinceStartup;
                Single deadline = Time.realtimeSinceStartup + 0.008f;
                Int32 steps = 0;
                do
                {
                    WithContext(StepNative);
                    steps++;
                    if (!_warming)
                    {
                        _nextFrame += 1f / (15f * PlaybackSpeed);
                        if (steps >= 4 || Time.realtimeSinceStartup < _nextFrame || !_playing) break;
                        continue;
                    }
                    if (!NeedsLeadInAdvance(HasVisibleGeometry(), _playing, _skippedFrames))
                        _warming = false;
                    else _skippedFrames++;
                }
                while ((_warming ? steps < 12 : steps < 4) && Time.realtimeSinceStartup < deadline);
                if (!_playing)
                {
                    _warming = false; SFXRender.commandBuffer.Clear(); ResetStageCamera();
                    if (_hitRemaining <= 0) _targetPoseTime = 0;
                    PlayAnimation(_caster, "ANH_MAIN_B0_000_000", true);
                    _status = "Finished. Space or Play to replay.";
                }
            }
            catch (Exception ex) { _playing = false; ResetStageCamera(); _status = "Effect failed: " + ex.Message; ViewerDiagnostics.Warning("[Dev Console][SFX Preview] " + ex); }
        }

        internal static Matrix4x4 ZoomProjection(Matrix4x4 projection, float zoom)
        {
            projection.m00 *= zoom; projection.m11 *= zoom;
            return projection;
        }

        private void ResetStageCamera()
        {
            _camera.worldToCameraMatrix = _stageView;

            _camera.projectionMatrix = _stageProjection;
        }
        internal static Boolean NeedsLeadInAdvance(Boolean visible, Boolean playing, Int32 skipped)
        {
            return !visible && playing && skipped < MaxBlankFrames;
        }
        internal static Boolean HasVisibleGeometry()
        {
            foreach (SFXMeshBase command in SFXRender.commandBuffer)
            {
                SFXMesh mesh = command as SFXMesh;
                if (mesh != null && mesh.VbOffset > 0 && mesh.IbOffset > 0) return true;
            }
            return false;
        }
        private void StepNative()
        {
            SFX.UpdateScreenSize();
            SFX.isRunning = SFX.SFX_Update(ref _frame);
            SFX.frameIndex = _frame;
            _playing = SFX.isRunning;
            if (!_playing) return;
            SFX.SFX_LateUpdate();
            SFXRender.Update();
            Single[] native = new Single[13];
            Marshal.Copy(SFX.SFX_UpdateCamera(0), native, 0, native.Length);

            _camera.worldToCameraMatrix = PsxCamera.PsxMatrix2UnityMatrix(native, 0f);
            _camera.projectionMatrix = ZoomProjection(PsxCamera.PsxProj2UnityProj(Math.Max(1f, native[12]), 65535f), _cameraZoom);
            SFX.fxNearZ = Math.Max(1f, native[12]); SFX.fxFarZ = 65535f;
        }
        private unsafe void Play()
        {
            StopPreviewAudio();
            SoundLib.SfxSoundPlayer.ResumeAllSounds();
            RecordLastEffect();
            _playing = false;
            ResetStageCamera(); ResetActors(); _paused = false; _frame = 0;
            _skippedFrames = 0; _warming = _skipBlankLeadIn; SFXRender.commandBuffer.Clear();
            WithContext(() => {
                SFX.SFX_InitSystem(SFX.BattleCallback);
                InitializeNative(BuildPreviewInit());
                SFX.request = default(S_Eff_Req_Org);
                SFX.isDebugMode = false;
                SFX.Begin(1, 0, new[] { _caster.tar_bone, _target.tar_bone },
                    new PSX_LIBGTE.VECTOR { vx = (Int32)_target.pos.x, vy = 0, vz = (Int32)_target.pos.z });
                SFX.SetExe(_caster); SFX.SetMExe(_caster); SFX.SetTrg(_target);
                SFX.Play((SpecialEffect)_catalog[_selected].Id);
            });
            PlayAnimation(_caster, "ANH_MAIN_B0_000_202", false);
            _playing = true; _effectStartAt = Time.realtimeSinceStartup + 6f / (15f * PlaybackSpeed); _nextFrame = _effectStartAt;
            _status = "Playing " + _catalog[_selected].Id + " — " + _catalog[_selected].Name;
        }

        private void RecordLastEffect()
        {
            try
            {
                String folder = Path.GetFullPath("Memoria Dev Console/Asset Dump/SfxPreview");
                Directory.CreateDirectory(folder);
                Entry effect = _catalog[_selected];
                File.WriteAllText(Path.Combine(folder, "LastEffect.txt"),
                    "SFX " + effect.Id + " - " + effect.Name + Environment.NewLine +
                    "Preview started: " + DateTime.Now.ToString("s") + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void PlayAnimation(BTL_DATA actor, String name, Boolean loop)
        {
            Animation animation = actor.gameObject.GetComponent<Animation>();
            if (animation == null || animation.GetClip(name) == null) return;
            animation.enabled = true;
            animation[name].wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            animation[name].speed = PlaybackSpeed;
            animation.Play(name);
        }

        private void ResetActors()
        {
            _targetPoseTime = 0; _hitRemaining = 0; _lastHitFrame = _lastFigureFrame = -1; _pendingFigure = null; foreach (var popup in _damagePopups) DestroyPopup(popup); _damagePopups.Clear();
            foreach (BTL_DATA actor in new[] { _caster, _target })
            {
                actor.pos = actor.base_pos;
                actor.gameObject.transform.position = actor.pos;
                actor.gameObject.transform.rotation = actor.rot;
                actor.gameObject.SetActive(true);
            }
            PlayAnimation(_caster, "ANH_MAIN_B0_000_000", true);
            SampleTargetPose(0);
            ApplyStageVisibility();
        }

        private void Render(Camera camera)
        {
            if (camera != _camera || !_playing || !_initialized || _disposed) return;
            try
            {
                GL.PushMatrix();
                try { WithContext(() => { SFX.ResetViewPort(); SFXRender.Render(); }); }
                finally { GL.PopMatrix(); }
            }
            catch (Exception ex) { _playing = false; _status = "Render failed: " + ex.Message; }
        }

        private readonly ViewerHoverHelp _hoverHelp = new ViewerHoverHelp();
        private GUISkin _sfxSkin;
        private Vector2 _sfxListScroll;
        private bool _revealSelection;

        private void MoveSfxSelection(int delta)
        {
            if (_matches.Count == 0) return;
            int index = _matches.IndexOf(_catalog[_selected]);
            index = index < 0 ? (delta > 0 ? 0 : _matches.Count - 1) : (index + delta + _matches.Count) % _matches.Count;
            _selected = _catalog.IndexOf(_matches[index]);
            _revealSelection = true;
        }

        internal void Draw()
        {
            int depth = GUI.depth;
            GUISkin oldSkin = GUI.skin;
            Color oldContent = GUI.contentColor, oldBackground = GUI.backgroundColor;
            GUI.depth = -10000;
            if (_sfxSkin == null)
            {
                _sfxSkin = UnityEngine.Object.Instantiate(oldSkin) as GUISkin;
                foreach (GUIStyle style in new[] { _sfxSkin.label, _sfxSkin.button, _sfxSkin.toggle, _sfxSkin.textField })
                {
                    style.fontSize = 17; style.fixedHeight = 0;
                    style.padding = new RectOffset(8, 8, 5, 5);
                    style.alignment = TextAnchor.MiddleLeft;
                }
                _sfxSkin.label.wordWrap = true;
                _sfxSkin.button.wordWrap = true;
            }
            GUI.skin = _sfxSkin;
            try
            {
                float listWidth = Math.Min(350, Screen.width * .24f);
                float controlsWidth = Math.Min(460, Screen.width * .31f);
                GUILayout.BeginArea(new Rect(12, 12, listWidth, Screen.height - 24), GUI.skin.box);
                GUI.contentColor = ConsoleTheme.UnityAccent;
                GUILayout.Label("SFX — ID / NAME");
                GUI.contentColor = oldContent;
                GUI.SetNextControlName("MDC_SFX_Search");
                _query = GUILayout.TextField(_query, 80);
                ViewerHoverHelp.Last("Search by exact SFX ID or effect name. Enter selects and plays the first match.");
                if (_query != _lastQuery)
                {
                    _lastQuery = _query; _matches.Clear(); _sfxListScroll = Vector2.zero;
                    foreach (Entry entry in _catalog) if (Matches(entry, _query)) _matches.Add(entry);
                }
                GUILayout.Label("↑ / ↓ select · Enter / Space play");
                GUILayout.Space(8);
                Event e = Event.current;
                if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() != "MDC_SFX_Search" && GUIUtility.keyboardControl == 0)
                {
                    if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
                    { MoveSfxSelection(e.keyCode == KeyCode.UpArrow ? -1 : 1); e.Use(); }
                    else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    { _playRequested = true; e.Use(); }
                }
                else if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "MDC_SFX_Search" && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    if (_matches.Count > 0) { _selected = _catalog.IndexOf(_matches[0]); _playRequested = true; _revealSelection = true; }
                    GUI.FocusControl(null); e.Use();
                }
                _sfxListScroll = GUILayout.BeginScrollView(_sfxListScroll);
                for (int i = 0; i < _matches.Count; i++)
                {
                    Entry entry = _matches[i];
                    bool selected = _catalog.Count > 0 && entry == _catalog[_selected];
                    GUI.backgroundColor = selected ? Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f) : oldBackground;
                    GUI.contentColor = selected ? ConsoleTheme.UnityAccent : oldContent;
                    if (GUILayout.Button(new GUIContent(entry.Id + " — " + entry.Name, "Select and play this effect. Up/Down selects within the filtered list; Enter or Space plays the selection."), GUILayout.MinHeight(34)))
                    { _selected = _catalog.IndexOf(entry); _playRequested = true; GUI.FocusControl(null); }
                    if (selected && _revealSelection && e.type == EventType.Repaint)
                    {
                        Rect row = GUILayoutUtility.GetLastRect();
                        float viewHeight = Math.Max(34, Screen.height - 160);
                        if (row.y < _sfxListScroll.y) _sfxListScroll.y = row.y;
                        else if (row.yMax > _sfxListScroll.y + viewHeight) _sfxListScroll.y = row.yMax - viewHeight;
                        _revealSelection = false;
                    }
                }
                GUI.backgroundColor = oldBackground; GUI.contentColor = oldContent;
                if (_matches.Count == 0) GUILayout.Label("No matching SFX.");
                GUILayout.EndScrollView();
                GUILayout.Label(_matches.Count + " effects");
                GUILayout.EndArea();

                GUILayout.BeginArea(new Rect(Screen.width - controlsWidth - 12, 12, controlsWidth, Screen.height - 24), GUI.skin.box);
                _panelScroll = GUILayout.BeginScrollView(_panelScroll);
                GUI.contentColor = ConsoleTheme.UnityAccent;
                GUILayout.Label("SFX PREVIEW — 1.2× SPEED");
                GUI.contentColor = oldContent;
                GUILayout.Label("Esc / F12: MDC menu · ← / →: cycle and play");
                GUILayout.Space(12);
                GUIStyle selectedStyle = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold, wordWrap = true };
                selectedStyle.normal.textColor = ConsoleTheme.UnityAccent;
                if (_catalog.Count > 0) GUILayout.Label(_catalog[_selected].Id + " — " + _catalog[_selected].Name, selectedStyle);
                GUILayout.Space(12);
                GUI.contentColor = ConsoleTheme.UnityAccent;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Play / Replay", "Restart the selected SFX at 1.2 times normal speed. Space or Enter also plays it. The active SFX camera uses fixed 0.50x zoom."), GUILayout.Height(40))) _playRequested = true;
                if (GUILayout.Button(new GUIContent(_paused ? "Resume" : "Pause", "Pause or resume the SFX, hit animation, damage popups, and preview sound playback."), GUILayout.Height(40))) TogglePause();
                if (GUILayout.Button(new GUIContent("Stop", "Stop playback and preview sounds, reset the actors, and restore the regular battle preview camera."), GUILayout.Height(40))) _stopRequested = true;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Previous", "Select and play the previous SFX in the full catalog. Left Arrow does the same."))) _pendingDelta--;
                if (GUILayout.Button(new GUIContent("Next", "Select and play the next SFX in the full catalog. Right Arrow does the same."))) _pendingDelta++;
                GUILayout.EndHorizontal();
                GUILayout.Space(12);
                GUI.backgroundColor = _audioEnabled ? Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f) : oldBackground;
                if (GUILayout.Button(new GUIContent((_audioEnabled ? "ON   " : "OFF   ") + "SFX sounds", "Enable or mute this preview's SFX sounds. Turning off stops sounds already playing.")))
                { _audioEnabled = !_audioEnabled; if (!_audioEnabled) StopPreviewAudio(); }
                GUI.backgroundColor = _skipBlankLeadIn ? Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f) : oldBackground;
                if (GUILayout.Button(new GUIContent((_skipBlankLeadIn ? "ON   " : "OFF   ") + "Skip blank lead-in", "On replay, advance through blank opening frames to reach the visible effect sooner, after a short initial delay."))) _skipBlankLeadIn = !_skipBlankLeadIn;
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
                GUILayout.Space(12);
                GUILayout.Label((_warming ? "Preparing first visible frame..." : _status) + (_playing ? " | Frame " + _frame : ""));
                GUI.contentColor = ConsoleTheme.UnityAccent;
                DrawStageControls();
                GUI.contentColor = oldContent;
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            finally { _hoverHelp.Draw(); GUI.skin = oldSkin; GUI.depth = depth; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground; }
        }
        private void ReleaseScene()
        {
            if (_damageTextMaterial != null) { UnityEngine.Object.Destroy(_damageTextMaterial); _damageTextMaterial = null; }
            if (_sfxSkin != null) { UnityEngine.Object.Destroy(_sfxSkin); _sfxSkin = null; }
            if (_damageRoot != null) { UnityEngine.Object.Destroy(_damageRoot); _damageRoot = null; }
            Application.targetFrameRate = _savedTargetFps;
            Camera.onPostRender -= Render;
            foreach (GameObject go in _hidden) if (go != null) go.SetActive(true);
            foreach (UIWidget widget in _labels) if (widget != null) widget.enabled = true;
            _hidden.Clear(); _labels.Clear();
            if (_root != null)
            {
                foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>())
                    if (renderer.gameObject.name == "Preview floor") UnityEngine.Object.Destroy(renderer.sharedMaterial);
                UnityEngine.Object.Destroy(_root); _root = null;
            }
            if (_camera != null)
            {
                _camera.transform.position = _savedCameraPosition; _camera.transform.rotation = _savedCameraRotation;
                _camera.transform.localScale = _savedCameraScale;
                _camera.worldToCameraMatrix = _savedView; _camera.projectionMatrix = _savedProjection;
                _camera.backgroundColor = _savedBackground; _camera.gameObject.tag = _savedCameraTag;
                _camera.cullingMask = _savedCullingMask;
            }
            foreach (Camera camera in _mainCameras) if (camera != null) camera.gameObject.tag = "MainCamera";
            _mainCameras.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _playing = false;
            try { StopPreviewAudio(); }
            finally
            {
                try { SoundLib.SfxSoundPlayer.ResumeAllSounds(); }
                finally
                {
                    try { RestoreNativeState(); }
                    finally { ReleaseScene(); }
                }
            }
        }

        private unsafe void RestoreNativeState()
        {
            try
            {
                if (_nativeChanged && _restoreNative)
                {
                    SFX.SFX_InitSystem(SFX.BattleCallback);
                    InitializeNative(_restoreInit);
                }
                else if (_startedCommon) { SFXRender.Release(); SFXMesh.Release(); }
            }
            finally
            {
                foreach (var pair in _savedSfx) pair.Key.SetValue(null, pair.Value);
                if (_savedVram != null) { PSXTextureMgr.originalVram = _savedVram; _savedVram = null; PSXTextureMgr.Reset(); }
                if (_savedChannels != null) SFX.channel = _savedChannels;
                _savedSfx.Clear(); _nativeChanged = _startedCommon = _initialized = false;
            }
        }
    }
}
