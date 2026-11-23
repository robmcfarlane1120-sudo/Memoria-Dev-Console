using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Memoria.Assets;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private FieldAssetViewer _fieldViewer;
        private Boolean _openFieldViewer, _fieldViewerEscapePending;

        private Boolean HandleFieldViewer()
        {
            if (_openFieldViewer || Input.GetKeyDown(KeyCode.F4))
            {
                _openFieldViewer = false;
                if (_fieldViewer != null) { ReturnHomeRequested = true; }
                else
                {
                    CloseSfxPreview();
                    _spsSearchOpen = false;
                    _spsPendingIndex = -1;
                    try { _fieldViewer = new FieldAssetViewer(); }
                    catch (Exception ex) { Log.Error("[Field Viewer] Could not open viewer: " + ex); }
                }
                return true;
            }
            if (_fieldViewer == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape) || _fieldViewer.CloseRequested)
            {
                _fieldViewerEscapePending = true; ReturnHomeRequested = true;
                return true;
            }
            // Keep the preview covering the model scene until the service exits.
            if (ReturnHomeRequested) return true;
            _fieldViewer.Update();
            return true;
        }

        private void CloseFieldViewer()
        {
            if (_fieldViewer == null) return;
            try { _fieldViewer.Dispose(); }
            finally { _fieldViewer = null; _lastFramedModel = null; _frameDelay = 3; }
        }

        public void DrawFieldViewer()
        {
            if (!IsActive || !ModelViewerScene.initialized) return;
            if (_fieldViewer != null) { _fieldViewer.Draw(); return; }
            if (_sfxPreview == null && !_spsSearchOpen && GUI.Button(new Rect(12, 12, 190, 32), "Field Viewer [F4]"))
                _openFieldViewer = true;
        }
    }

    // Asset-only preview: never loads event bytecode or changes the active field ID.
    // The scene is active only during its explicit offscreen camera render.
    internal sealed partial class FieldAssetViewer : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const Int32 Layer = 30;
        private readonly ViewerHoverHelp _hoverHelp = new ViewerHoverHelp();
        private sealed class FieldEntry { internal Int32 Id; internal String Map; }
        private sealed class EffectEntry { internal Int32 Id; internal String Map; internal String Key { get { return Map + "/" + Id; } } }
        private readonly List<FieldEntry> _fields = new List<FieldEntry>();
        private readonly List<FieldEntry> _matches = new List<FieldEntry>();
        private readonly List<EffectEntry> _effects = new List<EffectEntry>();
        private readonly Dictionary<String, String> _labels = new Dictionary<String, String>();
        private readonly List<GameObject> _hiddenModels = new List<GameObject>();
        private readonly List<UIWidget> _hiddenLabels = new List<UIWidget>();
        private readonly List<EffectEntry> _effectMatches = new List<EffectEntry>();
        private readonly List<DevSoundEntry> _audioMatches = new List<DevSoundEntry>();
        private readonly HashSet<Int32> _pausedMusic = new HashSet<Int32>();
        private readonly PreviewAudio _music = new PreviewAudio(), _sound = new PreviewAudio();
        private readonly List<DevSoundEntry> _musicEntries = new List<DevSoundEntry>(), _soundEntries = new List<DevSoundEntry>();
        private GameObject _root, _cameraGo;
        private Camera _camera;
        private RenderTexture _image;
        private BGSCENE_DEF _scene;
        private SPSEffect _effect;
        private CommonSPSSystem _sps;
        private Texture2D _ownedAtlas, _ownedSpsTexture;
        private String _selectedAudioPath;
        private static bool _mutePreviewBgm;
        private readonly bool _previousCursorVisible = Cursor.visible;
        private readonly CursorLockMode _previousCursorLock = Cursor.lockState;
        private string _previewMusicPath;

        private GUISkin _readableSkin;

        private void PlayPreviewMusic(string path)
        {
            _previewMusicPath = path;
            PauseGameMusic();
            if (_mutePreviewBgm) _music.Stop(); else _music.Play(path);
        }

        private void SetPreviewBgmMuted(bool muted)
        {
            _mutePreviewBgm = muted;
            PauseGameMusic();
            if (muted) { _music.Stop(); _sound.Stop(); PauseAllGameAudio(); }
            else { ResumeGameMusic(); PauseGameMusic(); }
            if (!muted && !String.IsNullOrEmpty(_previewMusicPath)) _music.Play(_previewMusicPath);
        }
        private FieldEntry _selected;
        private EffectEntry _selectedEffect;
        private String _query = "", _filter = null, _assetQuery = "", _label = "", _status = "", _audioStatus = "";
        private Int32 _pendingId = -1, _cameraIndex, _tab;
        private Boolean _allAudio, _animate = true, _disposed;
        private Vector2 _fieldScroll, _assetScroll;
        private Single _time, _effectScale = 1f, _zoom = 1f;
        private Bounds _bounds;
        private static String LabelPath { get { return Path.GetFullPath("Memoria Dev Console/FieldViewerSpsLabels.txt"); } }
        internal Boolean CloseRequested;

        internal FieldAssetViewer()
        {
            var fieldDictionary = (Dictionary<Int32, String>)typeof(EventEngine).Assembly.GetType("EventEngineUtils").GetField("eventIDToFBGID", Flags).GetValue(null);
            foreach (var pair in fieldDictionary.OrderBy(p => p.Key))
                if (!String.IsNullOrEmpty(pair.Value) && pair.Value != "invalidFieldMapID") _fields.Add(new FieldEntry { Id = pair.Key, Map = pair.Value });
            ReadFieldNames();
            ReadEffects();
            if (File.Exists(LabelPath))
                foreach (String line in File.ReadAllLines(LabelPath))
                {
                    Int32 separator = line.IndexOf('\t');
                    if (separator > 0) _labels[line.Substring(0, separator)] = line.Substring(separator + 1);
                }
            // Build just field music / sound metadata, not the expensive battle SFX catalog.
            if (SoundMetaData.MusicIndex.Count == 0) SoundMetaData.LoadMetaData();
            foreach (var pair in SoundMetaData.MusicIndex.OrderBy(p => p.Key))
                _musicEntries.Add(new DevSoundEntry { Id = pair.Key, Path = pair.Value, Usage = AudioUsageCatalog.GetMusicUsage(pair.Key) });
            foreach (var pair in SoundMetaData.SoundEffectIndex.OrderBy(p => p.Key))
                _soundEntries.Add(new DevSoundEntry { Id = pair.Key, Path = pair.Value, Usage = AudioUsageCatalog.GetSoundEffectUsage(pair.Key) });
            _pendingId = FF9StateSystem.Common.FF9.fldMapNo;
            if (!_fields.Any(f => f.Id == _pendingId) && _fields.Count > 0) _pendingId = _fields[0].Id;
            foreach (String name in new[] { "currentModel", "currentWeaponModel", "currentFloorModel", "backgroundGo" })
            {
                FieldInfo field = typeof(ModelViewerScene).GetField(name, Flags);
                GameObject go = field == null ? null : field.GetValue(null) as GameObject;
                if (go != null && go.activeSelf) { _hiddenModels.Add(go); go.SetActive(false); }
            }
            foreach (String name in new[] { "controlLabel", "infoLabel", "extraInfoLabel" })
            {
                FieldInfo field = typeof(ModelViewerScene).GetField(name, Flags);
                UIWidget label = field == null ? null : field.GetValue(null) as UIWidget;
                if (label != null && label.enabled) { _hiddenLabels.Add(label); label.enabled = false; }
            }
        }

        private void ReadEffects()
        {
            IList models = typeof(ModelViewerScene).GetField("geoList", Flags).GetValue(null) as IList;
            if (models == null) return;
            Int32 kind = (Int32)typeof(ModelViewerScene).GetField("MODEL_KIND_SPS", Flags).GetRawConstantValue();
            foreach (System.Object model in models)
            {
                Type t = model.GetType();
                if ((Int32)t.GetField("Kind").GetValue(model) != kind) continue;
                _effects.Add(new EffectEntry { Id = (Int32)t.GetField("Id").GetValue(model), Map = (String)t.GetField("Name").GetValue(model) });
            }
        }

        internal static Boolean MatchesField(Int32 id, String map, String query)
        {
            Int32 wanted;
            query = (query ?? "").Trim();
            return query.Length == 0 || (Int32.TryParse(query, out wanted) ? id == wanted : map.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RefreshMatches()
        {
            if (_filter == _query) return;
            _filter = _query;
            _matches.Clear();
            _matches.AddRange(_fields.Where(f => MatchesField(f.Id, f.Map, _query) || (!String.IsNullOrEmpty(_query) && FieldLabel(f).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0 && !Char.IsDigit(_query.Trim()[0]))));
            _fieldScroll = Vector2.zero;
        }

        private void StepField(Int32 direction)
        {
            RefreshMatches();
            if (_matches.Count == 0) return;
            Int32 index = _matches.FindIndex(f => _selected != null && f.Id == _selected.Id);
            if (index < 0) index = direction > 0 ? -1 : 0;
            _pendingId = _matches[((index + direction) % _matches.Count + _matches.Count) % _matches.Count].Id;
        }

        internal void Update()
        {
            try
            {
                Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
                _music.Update(); _sound.Update();
                if (_mutePreviewBgm) { _sound.Stop(); PauseAllGameAudio(); }
                if (_pendingId >= 0)
                {
                    Int32 id = _pendingId; _pendingId = -1;
                    LoadField(id);
                }
                if (_root == null || _scene == null) return;
                if (_animate) _time += Math.Min(Time.unscaledDeltaTime, 0.1f);
                UpdateFieldScripts();
                if (_effect != null && _effect.spsBin != null)
                {
                    _effect.curFrame = ((Int32)(_time * 30f) * 16) % Math.Max(16, _effect.frameCount);
                    _effect.GenerateSPS();
                    // SPS mode 0 generates the native mesh, but its default orientation
                    // follows the model camera. Use the offscreen field camera instead.
                    _effect.transform.rotation = Quaternion.identity;
                    _effect.transform.localScale = new Vector3(_effectScale, -_effectScale, _effectScale);
                    _effect.transform.position = new Vector3(_bounds.center.x, _bounds.center.y, _bounds.min.z - 50f);
                    _effect.meshRenderer.enabled = true;
                }
                _root.SetActive(true);
                try { _camera.Render(); }
                finally { _root.SetActive(false); }
            }
            catch (Exception ex)
            {
                _status = "Preview failed: " + ex.GetBaseException().Message;
                ViewerDiagnostics.Warning("[Field Viewer] " + ex);
                ClearScene();
            }
        }

        private void InvokeScene(String name, params System.Object[] args)
        {
            MethodInfo method = typeof(BGSCENE_DEF).GetMethod(name, Flags);
            if (method == null) throw new MissingMethodException("BGSCENE_DEF", name);
            method.Invoke(_scene, args);
        }

        private void LoadField(Int32 id)
        {
            ClearScene();
            _selected = _fields.FirstOrDefault(f => f.Id == id);
            if (_selected == null) { _status = "Unknown field ID " + id; return; }
            _cameraIndex = 0; _time = 0; _zoom = 1f;
            _assetScroll = Vector2.zero;
            _root = new GameObject("MDC_FieldAssetPreview");
            _root.SetActive(false);
            // A detached viewer can open before a live field has initialized its
            // UseUpscalFM flag. Always request the installed atlas; the native
            // loader falls back to PSX TIM data when no atlas exists.
            _scene = new BGSCENE_DEF(true);
            String path = FieldMap.GetMapResourcePath(_selected.Map);
            _scene.LoadResources(path, _selected.Map);
            if (!(Boolean)typeof(BGSCENE_DEF).GetField("useUpscaleFM", Flags).GetValue(_scene)) _ownedAtlas = _scene.atlas;
            if (_scene.cameraList.Count == 0) throw new InvalidOperationException("No background cameras/assets for field " + id);
            // Only the mesh-builder needs a FieldMap reference. Keep it inactive
            // throughout: HonoBehavior.Awake must never register a second live field.
            GameObject holder = new GameObject("MDC_InactiveFieldBuilder");
            holder.SetActive(false);
            try
            {
                FieldMap map = holder.AddComponent<FieldMap>();
                map.mapName = _selected.Map;
                if (_scene.isPureMemoriaScene) _scene.CreatePureMemoriaScene(_root.transform);
                else
                {
                    InvokeScene("CreateMaterials");
                    InvokeScene("CreateScene_Background", _root.transform, false);
                    Boolean upscale = (Boolean)typeof(BGSCENE_DEF).GetField("useUpscaleFM", Flags).GetValue(_scene);
                    Int32 tile = upscale ? DetectAtlasTileSize(_scene.atlas.width, _scene.atlas.height,
                        _scene.overlayList.Where(o => !o.isMemoria).Sum(o => o.spriteList.Count),
                        (Int32)_scene.SPRITE_W) : 0;
                    _scene.LoadLocalizationInfo(_selected.Map, path);
                    if (upscale) RefreshAtlasLayout(tile);
                    String imported = Path.Combine(Memoria.Configuration.Import.Path, path);
                    if (Memoria.Configuration.Import.Field && File.Exists(Path.Combine(imported, "atlases/atlas_1.png")))
                        InvokeScene("importOverlaysFromPsd", map, upscale, imported);
                    else
                        InvokeScene("handleOverlays", map, upscale);
                }
            }
            finally { UnityEngine.Object.Destroy(holder); }
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
            _cameraGo = new GameObject("MDC_FieldPreviewCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.cullingMask = 1 << Layer;
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.07f, 0.07f, 0.07f, 1);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 150000f;
            _image = new RenderTexture(1280, 960, 24);
            _image.Create();
            _camera.targetTexture = _image;
            SelectCamera(0);
            AddCustomEffects(path);
            AutoPlayFieldMusic();
            InitializeFieldScripts();
        }

        private void RefreshAtlasLayout(Int32 tile)
        {
            Int32 width = _scene.atlas.width, height = _scene.atlas.height;
            Int32 nativeTile = (Int32)_scene.SPRITE_W;
            ViewerDiagnostics.Message("[Field Viewer] Field " + _selected.Id + ": atlas " + width + "x" + height + ", tile " + tile);
            if (nativeTile == tile) return;

            Int32 nativeStride = nativeTile + 4, stride = tile + 4;
            Int32 nativeColumns = width / nativeStride, columns = width / stride;
            foreach (BGOVERLAY_DEF overlay in _scene.overlayList)
            {
                if (overlay.isMemoria) continue;
                foreach (BGSPRITE_LOC_DEF sprite in overlay.spriteList)
                {
                    // Preserve native atlas indices, including localized title tiles.
                    Int32 index = (sprite.atlasY - 2) / nativeStride * nativeColumns
                                + (sprite.atlasX - 2) / nativeStride;
                    sprite.atlasX = (UInt16)(2 + index % columns * stride);
                    sprite.atlasY = (UInt16)(2 + index / columns * stride);
                    sprite.w = sprite.h = (UInt16)tile;
                }
            }
            _scene.SPRITE_W = _scene.SPRITE_H = (UInt32)tile;
        }

        internal static Int32 DetectAtlasTileSize(Int32 width, Int32 height, Int32 sprites, Int32 configured)
        {
            // Stock textures can have trailing padding. Moguri generally packs
            // complete cells, so use that evidence before the stock 32px default.
            var fitting = new[] { 32, 64, configured }.Distinct()
                .Where(tile => tile > 0 && tile <= 4096 && width >= tile + 4 && height >= tile + 4
                    && (Int64)(width / (tile + 4)) * (height / (tile + 4)) >= sprites)
                .ToList();
            var packed = fitting.Where(tile => width % (tile + 4) == 0 && height % (tile + 4) == 0);
            Int32 exact = packed.OrderByDescending(tile => tile).FirstOrDefault();
            if (exact != 0) return exact;
            if (fitting.Contains(32)) return 32;
            if (fitting.Count > 0) return fitting[0];
            throw new InvalidOperationException("Cannot fit " + sprites + " field tiles in atlas " + width + "x" + height + ".");
        }

        private void AutoPlayFieldMusic()
        {
            _music.Stop(); _sound.Stop(); _previewMusicPath = null;
            DevSoundEntry track = _musicEntries.FirstOrDefault(s => IsKnownFieldAudio(s.Usage, _selected.Id));
            if (track == null)
            {
                _audioStatus = "No indexed BGM for this field. Awaiting script playback.";
                return;
            }
            PauseGameMusic();
            PlayPreviewMusic(track.Path);
            _selectedAudioPath = track.Path;
            _audioStatus = "Automatic catalog BGM " + track.Id + "\n" + track.Path;
        }

        private void AddCustomEffects(String path)
        {
            // Native Model Viewer enumerates bundles; custom field SPS also live on disk.
            foreach (String folder in AssetManager.FolderHighToLow.Select(f => f.FolderPath))
            {
                String directory = Path.Combine(Path.Combine(folder, "StreamingAssets/Assets/Resources"), path);
                if (!Directory.Exists(directory)) continue;
                foreach (String file in Directory.GetFiles(directory, "*.sps*", SearchOption.TopDirectoryOnly))
                {
                    Int32 id;
                    String filename = Path.GetFileName(file);
                    if (Int32.TryParse(filename.Split('.')[0], out id) && !_effects.Any(e => e.Id == id && String.Equals(e.Map, _selected.Map, StringComparison.OrdinalIgnoreCase)))
                        _effects.Add(new EffectEntry { Id = id, Map = _selected.Map });
                }
            }
        }

        private void SelectCamera(Int32 index)
        {
            if (_scene == null || _scene.cameraList.Count == 0) return;
            _cameraIndex = (index + _scene.cameraList.Count) % _scene.cameraList.Count;
            Boolean first = true;
            for (Int32 i = 0; i < _scene.cameraList.Count; i++)
            {
                Transform camera = _scene.cameraList[i].transform;
                if (camera == null) continue;
                camera.gameObject.SetActive(i == _cameraIndex);
                if (i != _cameraIndex) continue;
                foreach (MeshFilter mesh in camera.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mesh.sharedMesh == null) continue;
                    Bounds b = mesh.sharedMesh.bounds;
                    Vector3 min = mesh.transform.TransformPoint(b.min), max = mesh.transform.TransformPoint(b.max);
                    if (first) { _bounds = new Bounds(min, Vector3.zero); first = false; }
                    _bounds.Encapsulate(min); _bounds.Encapsulate(max);
                }
            }
            if (first) _bounds = new Bounds(Vector3.zero, new Vector3(320, 224, 1));
            _camera.transform.position = new Vector3(_bounds.center.x, _bounds.center.y, _bounds.min.z - 1000f);
            _camera.transform.rotation = Quaternion.identity;
            FitCamera();
            UpdateCapturedCamera();
        }

        private void FitCamera()
        {
            if (_camera != null) _camera.orthographicSize = Math.Max(1, Math.Max(_bounds.size.y / 2f, _bounds.size.x / (2f * 4f / 3f))) * 1.03f / _zoom;
        }

        private void SelectEffect(EffectEntry entry)
        {
            if (_root == null) return;
            ClearEffect();
            _selectedEffect = entry;
            _labels.TryGetValue(entry.Key, out _label); _label = _label ?? "";
            GameObject go = new GameObject("MDC_FieldSPS_" + entry.Id);
            go.transform.parent = _root.transform; go.layer = Layer;
            _effect = go.AddComponent<SPSEffect>();
            _effect.Init(0);
            _effect.spsTransform = go.transform;
            _effect.meshRenderer = go.AddComponent<MeshRenderer>();
            _effect.meshFilter = go.AddComponent<MeshFilter>();
            _sps = new CommonSPSSystem();
            UInt16[] vram = PSXTextureMgr.originalVram == null ? null : (UInt16[])PSXTextureMgr.originalVram.Clone();
            try
            {
                Byte[] binary;
                if (!_sps.LoadSPSBin(new KeyValuePair<String, Int32>(entry.Map, entry.Id), out binary))
                    throw new InvalidOperationException("Missing SPS binary: " + entry.Key);
                _effect.mapName = entry.Map; _effect.spsId = entry.Id;
                _effect.spsBin = binary; _effect.frameCount = CommonSPSSystem.GetSpsFrameCount(binary);
                if (entry.Map == "WorldMap" || entry.Map == "FromPrototype") _sps.SetupSPSTexture(_effect);
                else
                {
                    String texture = SPSConst.GetFieldSPSTexture(entry.Id);
                    if (texture != null) _effect.pngTexture = AssetManager.Load<Texture2D>("FieldMaps/FieldSPS/" + texture.Split('/')[1] + ".png", true);
                }
                if (_effect.pngTexture == null)
                {
                    _sps.LoadMapTextureInVram(entry.Map);
                    if (_sps.IsTCBReady) { _ownedSpsTexture = _effect.GetTextureFromCurrentTCB(); _effect.pngTexture = _ownedSpsTexture; }
                }
                if (_effect.pngTexture == null) throw new InvalidOperationException("No usable texture for SPS " + entry.Key);
                _effect.fade = 128; _effect.curFrame = 0;
                _effectScale = 1f; _time = 0;
                _status = "SPS " + entry.Key + " — " + (_effect.frameCount >> 4) + " frames. Use a label to identify it.";
            }
            catch (Exception ex) { _status = ex.GetBaseException().Message; ClearEffect(); }
            finally
            {
                if (vram != null) { PSXTextureMgr.originalVram = vram; PSXTextureMgr.Reset(); }
            }
        }

        internal void Draw()
        {
            Int32 oldDepth = GUI.depth; GUI.depth = -11000;
            GUISkin oldSkin = GUI.skin;
            if (_readableSkin == null)
            {
                _readableSkin = UnityEngine.Object.Instantiate(oldSkin) as GUISkin;
                foreach (GUIStyle style in new[] { _readableSkin.label, _readableSkin.button, _readableSkin.toggle, _readableSkin.textField })
                {
                    style.fontSize = 17;
                    style.fixedHeight = 0;
                    style.padding = new RectOffset(8, 8, 5, 5);
                }
                _readableSkin.label.wordWrap = true;
                _readableSkin.button.wordWrap = true;
                _readableSkin.button.alignment = TextAnchor.MiddleLeft;
                _readableSkin.label.alignment = TextAnchor.MiddleLeft;
            }
            GUI.skin = _readableSkin;
            try
            {
                GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
                Single sidebar = Math.Min(400, Screen.width * .26f);
                Single fieldWidth = Math.Min(310, Screen.width * .21f);
                Single fieldX = Screen.width - fieldWidth - 12;
                Single previewX = sidebar + 28;
                Single previewWidth = Math.Max(80, fieldX - previewX - 16);
                GUILayout.BeginArea(new Rect(12, 8, Screen.width - 24, 70));
                GUILayout.BeginHorizontal();
                GUILayout.Label("FIELD VIEWER — assets / SPS / audio", GUILayout.Width(340));
                if (GUILayout.Button(new GUIContent("Previous field", "Open the previous field. Left Arrow does the same; Shift + Arrow skips ten fields."))) StepField(-1);
                if (GUILayout.Button(new GUIContent("Next field", "Open the next field. Right Arrow does the same; Shift + Arrow skips ten fields."))) StepField(1);
                if (GUILayout.Button(new GUIContent("Back to MDC Menu [Esc / F4]", "Close the field preview and return to the main MDC menu."))) CloseRequested = true;
                GUILayout.EndHorizontal();
                GUILayout.Label(_selected == null ? "Choose a field" : FieldLabel(_selected));
                GUILayout.EndArea();
                Color controlsText = GUI.contentColor;
                GUI.contentColor = ConsoleTheme.UnityAccent;
                if (GUI.Button(new Rect(12, 90, sidebar, 40), new GUIContent("GLOBAL AUDIO MUTE: " + (_mutePreviewBgm ? "ON" : "OFF"), "Mute all preview audio, including music and sound effects. The mute setting stays active when you change fields.")))
                    SetPreviewBgmMuted(!_mutePreviewBgm);
                GUI.contentColor = controlsText;
                Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
                GUILayout.BeginArea(new Rect(fieldX, 80, fieldWidth, Screen.height - 92));
                Color fieldHeaderColor = GUI.contentColor;
                GUI.contentColor = ConsoleTheme.UnityAccent;
                GUILayout.Label("FIELDS — ID / NAME");
                GUI.contentColor = fieldHeaderColor;
                GUI.SetNextControlName("MDC_FieldSearch");
                _query = GUILayout.TextField(_query, 100);
                ViewerHoverHelp.Last("Search by field ID or name. Press Enter to open the first match.");
                GUILayout.Space(8);
                RefreshMatches();
                _fieldScroll = GUILayout.BeginScrollView(_fieldScroll);
                foreach (FieldEntry field in _matches)
                {
                    Color previous = GUI.backgroundColor;
                    if (_selected != null && field.Id == _selected.Id) GUI.backgroundColor = Color.Lerp(ConsoleTheme.UnityAccent, Color.white, .25f);
                    if (GUILayout.Button(new GUIContent(FieldLabel(field), "Open this field and initialize its preview effects. Resource: " + field.Map), GUILayout.MinHeight(30)))
                    { _pendingId = field.Id; GUI.FocusControl(""); }
                    GUI.backgroundColor = previous;
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                GUILayout.BeginArea(new Rect(12, 152, sidebar, Screen.height - 164));                GUI.contentColor = ConsoleTheme.UnityAccent;
                Int32 tab = GUILayout.Toolbar(_tab, new[] { new GUIContent("SPS", "Browse and toggle this field's particle effects, including background and object effects."), new GUIContent("Music", "Browse music associated with the field and audition a track."), new GUIContent("Sounds", "Browse sound effects associated with the field and audition a sound.") }, GUILayout.Height(40));
                GUI.contentColor = controlsText;
                GUILayout.Space(10);
                if (tab != _tab) { _tab = tab; _assetScroll = Vector2.zero; _assetQuery = ""; }
                GUI.SetNextControlName("MDC_FieldAssetSearch");
                _assetQuery = GUILayout.TextField(_assetQuery, 100);
                ViewerHoverHelp.Last("Filter the current SPS, music, or sounds list by ID or name.");
                GUILayout.Space(10);
                if (_tab == 0) DrawEffects(); else DrawAudio();
                GUILayout.EndArea();
                Rect imageArea = new Rect(previewX, 82, previewWidth, Math.Max(50, Screen.height - 222));
                if (_image != null) GUI.DrawTexture(imageArea, _image, ScaleMode.ScaleToFit, false);
                DrawPreviewRain(imageArea);
                GUILayout.BeginArea(new Rect(previewX, Screen.height - 132, previewWidth, 128));
                GUILayout.BeginHorizontal();
                GUILayout.Label("Zoom", GUILayout.Width(65));
                Single zoom = GUILayout.HorizontalSlider(_zoom, .5f, 3f);
                ViewerHoverHelp.Last("Enlarge or reduce the field preview, from 0.5 to 3 times its fitted size.");
                if (zoom != _zoom) { _zoom = zoom; FitCamera(); }
                GUILayout.EndHorizontal();
                GUILayout.Label(new GUIContent(_status, "Preview status and limitations. Object attachment positions can be approximate; full game scripts and bone movement are not simulated."));
                GUILayout.Label("Left / Right: field • Shift + Arrow: 10 fields • Search + Enter: open");
                GUILayout.EndArea();                Event ev = Event.current;
                if (ev.type == EventType.KeyDown)
                {
                    String focus = GUI.GetNameOfFocusedControl();
                    if (ev.keyCode == KeyCode.Return && focus == "MDC_FieldSearch" && _matches.Count > 0)
                    { _pendingId = _matches[0].Id; GUI.FocusControl(""); ev.Use(); }
                    else if (focus != "MDC_FieldSearch" && focus != "MDC_FieldAssetSearch" && focus != "MDC_SpsLabel")
                    {
                        if (ev.keyCode == KeyCode.LeftArrow) { StepField(ev.shift ? -10 : -1); ev.Use(); }
                        else if (ev.keyCode == KeyCode.RightArrow) { StepField(ev.shift ? 10 : 1); ev.Use(); }
                        else if (ev.keyCode == KeyCode.UpArrow) { if (ev.shift) StepField(-10); else StepAsset(-1); ev.Use(); }
                        else if (ev.keyCode == KeyCode.DownArrow) { if (ev.shift) StepField(10); else StepAsset(1); ev.Use(); }
                    }
                }
            }
            finally { _hoverHelp.Draw(); GUI.skin = oldSkin; GUI.depth = oldDepth; }
        }

        private void DrawEffects()
        {
            DrawCapturedSpsList();
            _effectMatches.Clear();
        }

        internal static Boolean IsKnownFieldAudio(String usage, Int32 fieldId)
        {
            // Include the delimiter to avoid matching field 100 inside field 1100.
            String needle = fieldId.ToString("D4") + " - ";
            usage = usage ?? "";
            Int32 start = 0, index;
            while ((index = usage.IndexOf(needle, start, StringComparison.Ordinal)) >= 0)
            {
                if (index == 0 || !Char.IsDigit(usage[index - 1])) return true;
                start = index + 1;
            }
            return false;
        }

        private void DrawAudio()
        {
            _allAudio = GUILayout.Toggle(_allAudio, new GUIContent("All audio (otherwise known field usage)", "Show the full audio catalog instead of only documented usage for this field. Custom script usage may not be indexed."));
            _audioMatches.Clear();
            _assetScroll = GUILayout.BeginScrollView(_assetScroll);
            Int32 count = 0;
            foreach (DevSoundEntry sound in (_tab == 1 ? _musicEntries : _soundEntries))
            {
                if (!_allAudio && (_selected == null || !IsKnownFieldAudio(sound.Usage, _selected.Id))) continue;
                if ((sound.Id + " " + sound.Path + " " + sound.Usage).IndexOf(_assetQuery, StringComparison.OrdinalIgnoreCase) < 0) continue;
                count++;
                _audioMatches.Add(sound);
                if (GUILayout.Button(new GUIContent(sound.Id + "  " + Path.GetFileName(sound.Path), "Play this audio entry. Global mute must be off to hear it. Source: " + sound.Path)))
                {
                    PlayAudio(sound); GUI.FocusControl("");
                }
            }
            if (count == 0) GUILayout.Label("No documented matches. Enable All audio to audition by ID. Custom script usage is not indexed.");
            GUILayout.EndScrollView();
            GUILayout.Label(_audioStatus);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Stop music", "Stop the manually auditioned track; resume the field's music when global mute is off."))) { _music.Stop(); _previewMusicPath = null; if (!_mutePreviewBgm) ResumeGameMusic(); }
            if (GUILayout.Button(new GUIContent("Stop sounds", "Stop the sound effect being auditioned."))) _sound.Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label(_music.Error + _sound.Error);
        }

        private void PlayAudio(DevSoundEntry sound)
        {
            if (_tab == 1) PauseGameMusic();
            if (_tab == 1) PlayPreviewMusic(sound.Path); else if (!_mutePreviewBgm) _sound.Play(sound.Path);
            _selectedAudioPath = sound.Path;
            _audioStatus = "Preview " + sound.Id + "\n" + sound.Path;
        }

        private void PauseAllGameAudio()
        {
            // Pause tracked native voices too: field ambience survives opening the viewer.
            foreach (PropertyInfo property in typeof(SoundLib).GetProperties(BindingFlags.Static | BindingFlags.Public))
            {
                if (!property.Name.EndsWith("Player")) continue;
                object player = property.GetValue(null, null);
                if (player == null) continue;
                for (Type type = player.GetType(); type != null; type = type.BaseType)
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(SoundDatabase).IsAssignableFrom(field.FieldType)) continue;
                        SoundDatabase db = field.GetValue(player) as SoundDatabase;
                        if (db != null) foreach (SoundProfile profile in db.ReadAll().Values) PauseMusicHandle(profile.SoundID);
                    }
            }
        }
        private void PauseGameMusic()
        {
            try
            {
                if (SoundLib.MusicPlayer != null) PauseMusicHandle(SoundLib.MusicPlayer.GetActiveSoundID());
                // Event-script BGM uses the dispatch player's separate database.
                if (SoundLib.AllSoundDispatchPlayer != null)
                {
                    FieldInfo field = typeof(AllSoundDispatchPlayer).GetField("musicImmediateDB", Flags);
                    SoundDatabase database = field == null ? null : field.GetValue(SoundLib.AllSoundDispatchPlayer) as SoundDatabase;
                    if (database != null) foreach (SoundProfile profile in database.ReadAll().Values) PauseMusicHandle(profile.SoundID);
                }
            }
            catch (Exception ex) { ViewerDiagnostics.Warning("[Field Viewer] Could not pause original BGM: " + ex.Message); }
        }

        private void PauseMusicHandle(Int32 id)
        {
            if (id <= 0 || _pausedMusic.Contains(id)) return;
            if (ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_IsExist(id) != 0 && ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_IsPaused(id) == 0)
            {
                ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_SetPause(id, 1, 0);
                _pausedMusic.Add(id);
            }
        }

        private void ResumeGameMusic()
        {
            foreach (Int32 id in _pausedMusic)
                if (ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_IsExist(id) != 0)
                    ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_SetPause(id, 0, 0);
            _pausedMusic.Clear();
        }

        private void StepAsset(Int32 direction)
        {
            if (_tab == 0 && _effectMatches.Count > 0)
            {
                Int32 index = _effectMatches.IndexOf(_selectedEffect);
                if (index < 0) index = direction > 0 ? -1 : 0;
                index = (index + direction + _effectMatches.Count) % _effectMatches.Count;
                SelectEffect(_effectMatches[index]); _assetScroll.y = index * 24;
            }
            else if (_tab != 0 && _audioMatches.Count > 0)
            {
                Int32 index = _audioMatches.FindIndex(a => a.Path == _selectedAudioPath);
                if (index < 0) index = direction > 0 ? -1 : 0;
                index = (index + direction + _audioMatches.Count) % _audioMatches.Count;
                PlayAudio(_audioMatches[index]); _assetScroll.y = index * 24;
            }
        }

        private void ClearEffect()
        {
            if (_effect != null)
            {
                _effect.gameObject.SetActive(false);
                _effect.transform.parent = null;
                if (_effect.meshFilter != null && _effect.meshFilter.sharedMesh != null) UnityEngine.Object.Destroy(_effect.meshFilter.sharedMesh);
                foreach (Material material in _effect.materials) if (material != null) UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(_effect.gameObject);
            }
            if (_ownedSpsTexture != null) UnityEngine.Object.Destroy(_ownedSpsTexture);
            _ownedSpsTexture = null;
            _effect = null; _sps = null; _selectedEffect = null;
        }

        private void ClearScene()
        {
            ClearCapturedSps();
            ClearFieldScripts();
            ClearEffect();
            if (_root != null)
            {
                var materials = new HashSet<Material>();
                if (_scene != null)
                {
                    foreach (Material material in _scene.materialList.Values) if (material != null) materials.Add(material);
                    foreach (BGOVERLAY_DEF overlay in _scene.overlayList) if (overlay.memoriaMaterial != null) materials.Add(overlay.memoriaMaterial);
                }
                foreach (MeshFilter mesh in _root.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.sharedMesh != null) UnityEngine.Object.Destroy(mesh.sharedMesh);
                foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials) if (material != null) materials.Add(material);
                foreach (Material material in materials) UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(_root);
            }
            if (_cameraGo != null) UnityEngine.Object.Destroy(_cameraGo);
            if (_image != null) { _image.Release(); UnityEngine.Object.Destroy(_image); }
            if (_ownedAtlas != null) UnityEngine.Object.Destroy(_ownedAtlas);
            _ownedAtlas = null;
            _root = null; _camera = null; _cameraGo = null; _image = null; _scene = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cursor.visible = _previousCursorVisible; Cursor.lockState = _previousCursorLock;
            try { _music.Stop(); _sound.Stop(); }
            finally
            {
                try { ResumeGameMusic(); }
                finally
                {
                    ClearScene();
                    if (_readableSkin != null) UnityEngine.Object.Destroy(_readableSkin);
                    foreach (GameObject go in _hiddenModels) if (go != null) go.SetActive(true);
                    foreach (UIWidget label in _hiddenLabels) if (label != null) label.enabled = true;
                }
            }
        }

        // Separate handles let BGM and ambient sounds play together, and never
        // stop the game's own audio or replace its music/event bookkeeping.
        private sealed class PreviewAudio
        {
            private readonly System.Object _gate = new System.Object();
            private Int32 _generation;
            private SoundProfile _ready, _playing;
            private Boolean _loaded;
            internal String Error = "";
            internal void Play(String path)
            {
                Stop(); Error = "";
                Int32 generation = _generation;
                try
                {
                    SoundLib.LoadSoundEffect(path, delegate(SoundProfile profile, SoundDatabase database)
                    { lock (_gate) { if (generation == _generation) { _ready = profile; _loaded = true; } } });
                }
                catch (Exception ex) { Error = ex.Message; }
            }
            internal void Update()
            {
                lock (_gate)
                {
                    if (!_loaded) return;
                    _loaded = false;
                    // Loaders may return a cached bank profile. Own a distinct voice
                    // handle rather than replacing that shared profile's SoundID.
                    _playing = _ready == null ? null : new SoundProfile {
                        BankID = _ready.BankID, SoundIndex = _ready.SoundIndex,
                        ResourceID = _ready.ResourceID, Code = _ready.Code, Name = _ready.Name,
                        SoundProfileType = _ready.SoundProfileType, SoundVolume = _ready.SoundVolume,
                        Pitch = _ready.Pitch, Panning = _ready.Panning
                    };
                    _ready = null;
                }
                if (_playing == null || _playing.BankID == 0) { Error = "Audio resource could not be loaded."; return; }
                try { SoundPlayer.StaticCreateSound(_playing); SoundPlayer.StaticStartSound(_playing, 1f); }
                catch (Exception ex) { Error = ex.Message; }
            }
            internal void Stop()
            {
                lock (_gate) { _generation++; _loaded = false; _ready = null; }
                if (_playing != null) { SoundPlayer.StaticStopSound(_playing); _playing = null; }
            }
        }
    }
    // Shared, non-interactive hover bubbles for both previewers.
    internal sealed class ViewerHoverHelp
    {
        private string _text = "";
        private float _since;
        internal static void Last(string text)
        {
            if (Event.current.type == EventType.Repaint)
                GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", text), GUIStyle.none);
        }
        internal void Draw()
        {
            if (Event.current.type != EventType.Repaint) return;
            string text = GUI.tooltip ?? "";
            if (_text != text) { _text = text; _since = Time.realtimeSinceStartup; }
            if (text.Length == 0 || Time.realtimeSinceStartup - _since < .35f) return;
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 9, 9) };
            style.normal.textColor = Color.white;
            float width = Math.Min(420f, Screen.width - 24f);
            float height = Math.Min(style.CalcHeight(new GUIContent(text), width), Screen.height - 24f);
            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Clamp(mouse.x + 18, 12, Math.Max(12, Screen.width - width - 12));
            float y = mouse.y + 24;
            if (y + height > Screen.height - 12) y = mouse.y - height - 12;
            y = Mathf.Clamp(y, 12, Math.Max(12, Screen.height - height - 12));
            Rect box = new Rect(x, y, width, height);
            Color old = GUI.color, content = GUI.contentColor;
            int depth = GUI.depth;
            GUI.depth = -20000;
            GUI.color = ConsoleTheme.UnityAccent;
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(.035f, .045f, .055f, 1f);
            GUI.DrawTexture(new Rect(x + 1, y + 1, width - 2, height - 2), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.contentColor = Color.white;
            GUI.Label(box, text, style);
            GUI.color = old; GUI.contentColor = content; GUI.depth = depth;
        }
    }
}
