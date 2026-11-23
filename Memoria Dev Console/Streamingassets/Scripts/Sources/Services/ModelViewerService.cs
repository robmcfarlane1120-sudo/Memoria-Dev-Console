using Assets.Scripts.Common;
using Memoria.Assets;
using Memoria.Prime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private readonly FieldStateService _fieldState;

        private Color _savedCameraColor;
        private Boolean _hasSavedCameraColor;
        private readonly List<Renderer> _suspendedFieldRenderers = new List<Renderer>();

        private Camera _originalFieldCamera;
        private String _originalFieldCameraName;
        private Boolean _originalFieldCameraEnabled;
        private GameObject _viewerCameraObject;
        private GameObject _lastFramedModel;
        private Int32 _frameDelay;
        private Boolean _enteredFromField;
        private UIManager.UIState _savedUiState;
        private Boolean _hasSavedUiState;
        private readonly List<Camera> _suspendedBattleCameras = new List<Camera>();
        private readonly List<MonoBehaviour> _suspendedBattleScripts = new List<MonoBehaviour>();

        private readonly List<Renderer> _suspendedGameSpsRenderers = new List<Renderer>();
        private readonly List<UIWidget> _suspendedGameUiWidgets = new List<UIWidget>();

        public ModelViewerService(FieldStateService fieldState)
        {
            _fieldState = fieldState;
        }

        public Boolean IsActive
        {
            get { return Memoria.Configuration.Debug.StartModelViewer; }
        }

        public Boolean ReturnHomeRequested { get; set; }

        public Boolean ExitRequested
        {
            // Poll Unity input while the native console is hidden. Release-based
            // exit keeps Escape's key-down out of the resumed game's pause menu.
            // SPS search owns its first Escape; the next one leaves the viewer.
            get
            {
                if (ReturnHomeRequested) return !Input.GetKey(KeyCode.Escape) && !Input.GetKey(KeyCode.F12);
                if (_fieldViewerEscapePending) { if (!Input.GetKey(KeyCode.Escape)) _fieldViewerEscapePending = false; return false; }
                if (_fieldViewer != null) return false;
                if (_sfxEscapePending) { if (!Input.GetKey(KeyCode.Escape)) _sfxEscapePending = false; return false; }
                if (_sfxPreview != null) return false;
                if (_spsSearchEscapePending)
                {
                    if (!Input.GetKey(KeyCode.Escape)) _spsSearchEscapePending = false;
                    return false;
                }
                if (IsActive && !_spsSearchOpen && Input.GetKeyUp(KeyCode.Escape)) { ReturnHomeRequested=true; return true; }
                return false;
            }
        }

        private static Boolean IsBattleActive()
        {
            try
            {
                return FF9StateSystem.Battle != null &&
                       FF9StateSystem.Battle.FF9Battle != null &&
                       FF9StateSystem.Battle.FF9Battle.map != null &&
                       FF9StateSystem.Battle.FF9Battle.map.btlBGPtr != null;
            }
            catch
            {
                return false;
            }
        }

        public String Toggle()
        {
            return IsActive ? ReturnToGameRuntime() : EnterModelViewerRuntime();
        }

        public String OpenViewer(Int32 kind)
        {
            String result = IsActive ? "Viewer active." : EnterModelViewerRuntime();
            if (IsActive)
            {
                CloseFieldViewer();
                CloseSfxPreview();
                ResetViewerNavigation();
                _openFieldViewer = kind == 2;
                _openSfxPreview = kind == 1;
            }
            return result;
        }
        private void ResetViewerNavigation()
        {
            ReturnHomeRequested = false;
            _sfxEscapePending = _fieldViewerEscapePending = _spsSearchEscapePending = false;
            _openSfxPreview = _openFieldViewer = false;
        }
        private String EnterModelViewerRuntime()
        {
            try
            {

                ResetViewerNavigation();
                FF9StateSystem launchState = PersistenSingleton<FF9StateSystem>.Instance;
                _enteredFromField = launchState != null &&
                                    launchState.mode == 1 &&
                                    GameObject.Find("FieldMap Camera") != null;

                _enteredFromBattle = !_enteredFromField && IsBattleActive();
                SaveBattleControlState();

                SaveViewerCameraState();

                if (_enteredFromField)
                {
                    SuspendCurrentFieldRenderers();
                }
                else if (_enteredFromBattle)
                {
                    SuspendCurrentBattleRenderers();
                }

                if (!PrepareDedicatedModelViewerCamera())
                {
                    RestoreCurrentFieldRenderers();
                    RestoreCurrentBattleRenderers();
                    return "Model Viewer failed: could not create a clean viewer camera.";
                }

                if (_viewerCameraObject != null)
                {
                    Camera viewerCameraComponent = _viewerCameraObject.GetComponent<Camera>();
                    SuspendCompetingWorldCameras(viewerCameraComponent);
                }

                if (!SetRuntimeModelViewerFlag(true))
                {
                    RestoreDedicatedModelViewerCamera();
                    RestoreCurrentFieldRenderers();
                    RestoreCurrentBattleRenderers();
                    return "Model Viewer failed: could not enable Memoria's runtime Model Viewer flag.";
                }

                SuspendExistingGameSpsRenderers();
                SuspendExistingGameUiWidgets();
                SuspendViewerMenuInput();

                if (!ModelViewerScene.initialized)
                {
                    try
                    {
                        ModelViewerScene.Init();
                    }
                    catch (NullReferenceException initEx)
                    {
                        Log.Warning("[Dev Console][Model Viewer] Init hit a stale model/animation pair; recovering to model index 0. " + initEx.Message);

                        Type viewerType = typeof(ModelViewerScene);
                        BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                        FieldInfo animIndexField = viewerType.GetField("currentAnimIndex", flags);
                        if (animIndexField != null)
                            animIndexField.SetValue(null, 0);

                        MethodInfo changeModelMethod = viewerType.GetMethod("ChangeModel", flags);
                        if (changeModelMethod == null)
                            throw;

                        changeModelMethod.Invoke(null, new System.Object[] { 0 });

                    }
                }

                if (!ModelViewerScene.initialized)
                {
                    CleanupModelViewerObjects();
                    RestoreExistingGameUiWidgets();
                    RestoreExistingGameSpsRenderers();
                    SetRuntimeModelViewerFlag(false);
                    RestoreDedicatedModelViewerCamera();
                    RestoreCurrentFieldRenderers();
                    RestoreCurrentBattleRenderers();
                    return "Model Viewer failed: Memoria did not initialize the viewer.";
                }

                if (_enteredFromBattle)
                    FixClonedModelViewerLabelClipping();

                ExtendModelViewerAnimations();

                WriteMonsterAnimationAudit();

                _lastFramedModel = null;
                _frameDelay = 0;
                if (_enteredFromBattle)
                {
                    return "Model Viewer opened instantly from battle. Game simulation frozen.";
                }

                return "Model Viewer opened instantly. Game simulation frozen.";
            }
            catch (Exception ex)
            {
                SetRuntimeModelViewerFlag(false);
                CleanupModelViewerObjects();
                ModelViewerScene.initialized = false;
                RestoreViewerCameraState();
                RestoreSavedUiState();
                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                RestoreDedicatedModelViewerCamera();
                RestoreCurrentFieldRenderers();
                RestoreCurrentBattleRenderers();
                RestoreGameFrameRate();
                Log.Error("[Dev Console] Runtime Model Viewer launch failed: " + ex);
                return "Model Viewer launch failed: " + ex.Message;
            }
        }

        public void UpdateRuntime()
        {
            LogBattleResumeState();
            if (IsActive || _consoleMenuInputBlocked) SuspendViewerMenuInput(); else RestoreViewerMenuInput();
            if (!IsActive || !ModelViewerScene.initialized)
            {
                CloseFieldViewer();

                _spsSearchOpen = false;
                _spsPendingIndex = -1;
                return;
            }

            try
            {
                KeepExistingGameSpsHidden();
                SuppressAllNonViewerSps();

                if (_enteredFromBattle)
                {
                    KeepBattleRenderersHidden();
                }

                KeepExistingGameUiWidgetsHidden();
                if (HandleFieldViewer()) return;
                if (HandleSfxPreview()) return;
                if (HandleSpsSearch()) return;
                if (!HandleModelBoundaryJump()) ModelViewerScene.Update();
                ExtendModelViewerAnimations();
                RefreshViewerBoneDialogVisibility();
                ShowModelExportControl();
                IsolateViewerModelLayer();

                Type viewerType = typeof(ModelViewerScene);
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo modelField = viewerType.GetField("currentModel", flags);
                GameObject model = modelField != null ? modelField.GetValue(null) as GameObject : null;

                if (model == null)
                    return;

                if (Input.GetKeyDown(KeyCode.F11))
                    DumpCurrentModelToFbx(model);

                SuppressViewerSpsWhenNotSelected(model);

                if (model != _lastFramedModel)
                {
                    FieldInfo keepCoordinates = viewerType.GetField("KeepCoordinates", flags);
                    Boolean preserveCoordinates = keepCoordinates != null && (Boolean)keepCoordinates.GetValue(null);
                    _frameDelay = _lastFramedModel == null || !preserveCoordinates ? 3 : 0;
                    _lastFramedModel = model;
                    return;
                }

                if (_frameDelay > 0)
                {
                    _frameDelay--;
                    if (_frameDelay == 0)
                        AutoFrameCurrentModel(viewerType, model, flags);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Auto-frame failed: " + ex.Message);
            }
        }

        private static void ShowModelExportControl()
        {
            UILabel label = GetStaticFieldValue(typeof(ModelViewerScene), "controlLabel") as UILabel;
            if (label == null) return;
            String controls = label.rawText;
            if (String.IsNullOrEmpty(controls)) return;
            const String anchor = "Increment Keyframe [FFFF00][X][FFFFFF]\r\n";
            const String exportHint = "Export Model Asset [FFFF00][F11][FFFFFF]\r\n";
            // NGUI consumes [F3] as a hexadecimal alpha tag; space the key like native [F 1].
            const String searchHint = "Find SPS [FFFF00][F 3][FFFFFF]\r\n";
            const String sfxHint = "SFX Preview [FFFF00][F 12][FFFFFF]\r\n";
            String clean = controls.Replace(sfxHint, "").Replace(exportHint, "").Replace(searchHint, "")
                .Replace("Find SPS [FFFF00][F3][FFFFFF]\r\n", "");
            String updated = clean.Contains(anchor)
                ? clean.Replace(anchor, anchor + exportHint + searchHint)
                : clean.TrimEnd('\r', '\n') + "\r\n" + exportHint + searchHint;
            if (updated != controls) label.rawText = updated;
        }
        private static void RestoreGameFrameRate()
        {
            try { SceneDirector.SetTargetFrameRateForCurrentScene(); }
            catch (Exception ex) { Log.Warning("[Dev Console][Model Viewer] Could not restore game FPS/TPS: " + ex.Message); }
        }
        public void Shutdown()
        {
            if (_spsSearchSkin != null) { UnityEngine.Object.Destroy(_spsSearchSkin); _spsSearchSkin = null; }
            CloseFieldViewer();
            CloseSfxPreview();
            // Quitting must never use ReturnToGameRuntime: it can reload a scene or
            // invoke the hard-restart fallback. Only release viewer-owned state here.
            SetRuntimeModelViewerFlag(false);
            ModelViewerScene.initialized = false;
            try { CleanupModelViewerObjects(); }
            finally
            {
                RestoreViewerCameraState();
                RestoreDedicatedModelViewerCamera();
                RestoreSavedUiState();
                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                RestoreCurrentFieldRenderers();
                RestoreCurrentBattleRenderers();
                _enteredFromField = false;
                _enteredFromBattle = false;
                _lastFramedModel = null;
                _frameDelay = 0;
            }
        }
        private String ReturnToGameRuntime()
        {
            CloseFieldViewer();
            CloseSfxPreview();
            try
            {
                if (!SetRuntimeModelViewerFlag(false))
                    return ReturnToGameFallback("Could not disable runtime Model Viewer flag.");

                CleanupModelViewerObjects();
                ModelViewerScene.initialized = false;
                RestoreViewerCameraState();
                RestoreDedicatedModelViewerCamera();
                RestoreGameFrameRate();

                if (_enteredFromBattle)
                {
                    RestoreExistingGameUiWidgets();
                    RestoreExistingGameSpsRenderers();
                    RestoreCurrentBattleRenderers();
                    
                    // The existing battle/SFX sequence owns its camera phase. The
                    // dedicated viewer camera has already been removed/restored;
                    // changing native SFX phase here can disturb an in-flight action.

                    _enteredFromBattle = false;
                    return "Model Viewer closed. Battle resumed.";
                }

                if (!_enteredFromField)
                {
                    RestoreExistingGameUiWidgets();
                    RestoreExistingGameSpsRenderers();
                    ForgetSuspendedFieldRenderers();
                    _enteredFromField = false;
                    return "Model Viewer closed instantly.";
                }

                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                ForgetSuspendedFieldRenderers();

                if (_fieldState == null)
                    return ReturnToGameFallback("Field State service is unavailable.");

                String restoreResult = _fieldState.RestoreLatestStableFieldSnapshot();
                if (String.IsNullOrEmpty(restoreResult) || !restoreResult.StartsWith("Restoring field "))
                    return ReturnToGameFallback("Latest stable field snapshot restore failed: " + restoreResult);

                _enteredFromField = false;

                return "Model Viewer closed. " + restoreResult;
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Runtime Model Viewer return failed: " + ex);
                return ReturnToGameFallback(ex.Message);
            }
        }

        private String ReturnToGameFallback(String reason)
        {
            try
            {
                Log.Warning("[Dev Console] Instant Model Viewer return unavailable: " + reason);
                Log.Warning("[Dev Console] Falling back to hard restart.");

                RestoreDedicatedModelViewerCamera();
                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                ForgetSuspendedFieldRenderers();
                RestoreCurrentBattleRenderers();

                _enteredFromField = false;
                _enteredFromBattle = false;

                if (_fieldState != null)
                    _fieldState.PersistHistoryForRestart();

                HardResetService.Restart();
                return "Instant return unavailable; restarting FFIX safely...";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Model Viewer fallback restart failed: " + ex);
                return "Model Viewer return failed: " + ex.Message;
            }
        }

        private void SuspendExistingGameUiWidgets()
        {
            _suspendedGameUiWidgets.Clear();
            try
            {
                UIWidget[] widgets = UnityEngine.Object.FindObjectsOfType<UIWidget>();
                for (Int32 i = 0; i < widgets.Length; i++)
                {
                    UIWidget widget = widgets[i];
                    if (widget == null || !widget.enabled) continue;
                    _suspendedGameUiWidgets.Add(widget);
                    widget.enabled = false;
                }
            }
            catch (Exception ex) { Log.Warning("Could not isolate pre-existing game UI: " + ex.Message); }
        }

        private void RestoreExistingGameUiWidgets()
        {
            RestoreViewerMenuInput();
            try
            {
                for (Int32 i = 0; i < _suspendedGameUiWidgets.Count; i++)
                {
                    if (_suspendedGameUiWidgets[i] != null) _suspendedGameUiWidgets[i].enabled = true;
                }
            }
            catch (Exception ex) { Log.Warning("Could not restore pre-existing game UI: " + ex.Message); }
            finally { _suspendedGameUiWidgets.Clear(); }
        }

        private void SuspendExistingGameSpsRenderers()
        {
            _suspendedGameSpsRenderers.Clear();
            try
            {
                SPSEffect[] effects = UnityEngine.Object.FindObjectsOfType<SPSEffect>();
                for (Int32 i = 0; i < effects.Length; i++)
                {
                    SPSEffect effect = effects[i];
                    if (effect == null) continue;
                    Renderer[] renderers = effect.GetComponentsInChildren<Renderer>(true);
                    for (Int32 j = 0; j < renderers.Length; j++)
                    {
                        Renderer renderer = renderers[j];
                        if (renderer == null || !renderer.enabled) continue;
                        if (!_suspendedGameSpsRenderers.Contains(renderer)) _suspendedGameSpsRenderers.Add(renderer);
                        renderer.enabled = false;
                    }
                }
            }
            catch (Exception ex) { Log.Warning("Could not isolate game SPS: " + ex.Message); }
        }

        private void KeepExistingGameSpsHidden()
        {
            for (Int32 i = 0; i < _suspendedGameSpsRenderers.Count; i++)
            {
                if (_suspendedGameSpsRenderers[i] != null) _suspendedGameSpsRenderers[i].enabled = false;
            }
        }



        private static void SuppressAllNonViewerSps()
        {
            try
            {
                SPSEffect[] effects = UnityEngine.Object.FindObjectsOfType<SPSEffect>();
                for (Int32 i = 0; i < effects.Length; i++)
                {
                    SPSEffect effect = effects[i];
                    if (effect == null || effect.gameObject == null) continue;
                    if (effect.gameObject.name == "ModelViewer_SPS") continue;
                    Renderer[] renderers = effect.GetComponentsInChildren<Renderer>(true);
                    for (Int32 j = 0; j < renderers.Length; j++)
                    {
                        if (renderers[j] != null) renderers[j].enabled = false;
                    }
                }
            }
            catch (Exception ex) { Log.Warning("SPS quarantine failed: " + ex.Message); }
        }

        private void RestoreExistingGameSpsRenderers()
        {
            try
            {
                for (Int32 i = 0; i < _suspendedGameSpsRenderers.Count; i++)
                {
                    if (_suspendedGameSpsRenderers[i] != null) _suspendedGameSpsRenderers[i].enabled = true;
                }
            }
            finally { _suspendedGameSpsRenderers.Clear(); }
        }

        private void RestoreSavedUiState()
        {
            if (!_hasSavedUiState) return;
            try
            {
                UIManager uiManager = PersistenSingleton<UIManager>.Instance;
                if (uiManager != null) uiManager.State = _savedUiState;
            }
            catch (Exception ex) { Log.Warning("Could not restore UI state: " + ex.Message); }
            finally { _hasSavedUiState = false; }
        }

        private static void SuppressViewerSpsWhenNotSelected(GameObject currentModel)
        {
            try
            {
                GameObject viewerSps = GameObject.Find("ModelViewer_SPS");
                if (viewerSps == null) return;
                Boolean selected = currentModel == viewerSps;
                Renderer[] renderers = viewerSps.GetComponentsInChildren<Renderer>(true);
                for (Int32 i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null) renderers[i].enabled = selected;
                }
            }
            catch (Exception ex) { Log.Warning("Could not suppress idle viewer SPS: " + ex.Message); }
        }

        private static void AutoFrameCurrentModel(Type viewerType, GameObject model, BindingFlags flags)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Boolean haveBounds = false;
            Bounds bounds = new Bounds();
            for (Int32 i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!haveBounds) { bounds = renderer.bounds; haveBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!haveBounds) return;

            FieldInfo wrapperField = viewerType.GetField("currentModelWrapper", flags);
            GameObject wrapper = wrapperField != null ? wrapperField.GetValue(null) as GameObject : null;
            if (wrapper == null)
            {
                wrapper = new GameObject("CurrentModelWrapper");
                wrapperField.SetValue(null, wrapper);
                model.transform.SetParent(wrapper.transform, true);
            }

            wrapper.transform.position += -bounds.center;
            haveBounds = false;
            for (Int32 i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!haveBounds) { bounds = renderer.bounds; haveBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            Camera camera = GameObject.Find("FieldMap Camera") != null ? GameObject.Find("FieldMap Camera").GetComponent<Camera>() : null;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0f, 0f, -1000f);
                camera.transform.LookAt(Vector3.zero, Vector3.down);
                camera.orthographic = false;
                camera.fieldOfView = 40f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10000f;
                camera.cullingMask = GetViewerWorldCullingMask();
            }

            Single maxHalf = Mathf.Max(bounds.extents.x, bounds.extents.y);
            if (maxHalf > 0.001f)
            {
                Single fit = Mathf.Clamp(260f / maxHalf, 0.02f, 20f);
                model.transform.localScale *= fit;
                FieldInfo scaleField = viewerType.GetField("scaleFactor", flags);
                if (scaleField != null) scaleField.SetValue(null, model.transform.localScale);
            }

            haveBounds = false;
            for (Int32 i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!haveBounds) { bounds = renderer.bounds; haveBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (haveBounds) wrapper.transform.position += -bounds.center;

            FieldInfo posField = viewerType.GetField("model_Position", flags);
            if (posField != null) posField.SetValue(null, wrapper.transform.localPosition);
        }

        private Boolean PrepareDedicatedModelViewerCamera()
        {
            try
            {
                GameObject originalCameraObject = GameObject.Find("FieldMap Camera");
                if (originalCameraObject != null)
                {
                    _originalFieldCamera = originalCameraObject.GetComponent<Camera>();
                    if (_originalFieldCamera != null)
                    {
                        _originalFieldCameraName = originalCameraObject.name;
                        _originalFieldCameraEnabled = _originalFieldCamera.enabled;
                        originalCameraObject.name = "DevConsole_SuspendedFieldCamera";
                        _originalFieldCamera.enabled = false;
                    }
                }

                _viewerCameraObject = new GameObject("FieldMap Camera");
                Camera viewerCamera = _viewerCameraObject.AddComponent<Camera>();
                viewerCamera.clearFlags = CameraClearFlags.SolidColor;
                viewerCamera.backgroundColor = Color.black;
                // ModelViewerScene creates its controls under the existing NGUI/UI
                // hierarchy. Let the UI camera draw those layers; the perspective
                // model camera must never project their panel geometry into the world.
                viewerCamera.cullingMask = GetViewerWorldCullingMask();
                viewerCamera.orthographic = false;
                viewerCamera.fieldOfView = 40f;
                viewerCamera.nearClipPlane = 0.1f;
                viewerCamera.farClipPlane = 10000f;
                viewerCamera.depth = _originalFieldCamera != null ? _originalFieldCamera.depth : 0f;
                viewerCamera.rect = new Rect(0f, 0f, 1f, 1f);
                viewerCamera.enabled = true;

                _viewerCameraObject.transform.position = new Vector3(0f, 0f, -1000f);
                _viewerCameraObject.transform.LookAt(Vector3.zero, Vector3.down);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to create dedicated Model Viewer camera: " + ex);
                RestoreDedicatedModelViewerCamera();
                return false;
            }
        }

        private static Int32 GetViewerWorldCullingMask()
        {
            Int32 mask = -1;
            Int32 uiLayer = LayerMask.NameToLayer("UI");
            Int32 nguiLayer = LayerMask.NameToLayer("NGUI");

            if (uiLayer >= 0)
                mask &= ~(1 << uiLayer);
            if (nguiLayer >= 0)
                mask &= ~(1 << nguiLayer);

            return mask;
        }



        private void RestoreDedicatedModelViewerCamera()
        {
            try
            {
                RestoreCompetingWorldCameras();
                if (_viewerCameraObject != null)
                {
                    UnityEngine.Object.Destroy(_viewerCameraObject);
                    _viewerCameraObject = null;
                }
                if (_originalFieldCamera != null)
                {
                    GameObject originalCameraObject = _originalFieldCamera.gameObject;
                    if (originalCameraObject != null)
                    {
                        originalCameraObject.name = String.IsNullOrEmpty(_originalFieldCameraName) ? "FieldMap Camera" : _originalFieldCameraName;
                    }
                    _originalFieldCamera.enabled = _originalFieldCameraEnabled;
                }
            }
            catch (Exception ex) { Log.Warning("Could not fully restore field camera: " + ex.Message); }
            finally
            {
                _originalFieldCamera = null;
                _originalFieldCameraName = null;
                _originalFieldCameraEnabled = false;
            }
        }

        private void SuspendCurrentFieldRenderers()
        {
            _suspendedFieldRenderers.Clear();
            try
            {
                GameObject fieldRoot = GameObject.Find("FieldMap Root");
                if (fieldRoot == null) return;
                Renderer[] renderers = fieldRoot.GetComponentsInChildren<Renderer>(true);
                for (Int32 i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.enabled) continue;
                    _suspendedFieldRenderers.Add(renderer);
                    renderer.enabled = false;
                }
            }
            catch (Exception ex) { Log.Warning("Could not isolate FieldMap Root renderers: " + ex.Message); }
        }

        private void ForgetSuspendedFieldRenderers()
        {
            _suspendedFieldRenderers.Clear();
        }

        private void RestoreCurrentFieldRenderers()
        {
            try
            {
                for (Int32 i = 0; i < _suspendedFieldRenderers.Count; i++)
                {
                    if (_suspendedFieldRenderers[i] != null) _suspendedFieldRenderers[i].enabled = true;
                }
            }
            finally { _suspendedFieldRenderers.Clear(); }
        }





        private static Boolean SetRuntimeModelViewerFlag(Boolean enabled)
        {
            try
            {
                Type configurationType = typeof(Memoria.Configuration);
                FieldInfo instanceField = configurationType.GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo debugField = configurationType.GetField("_debug", BindingFlags.Instance | BindingFlags.NonPublic);
                if (instanceField == null || debugField == null) return false;
                System.Object instance = instanceField.GetValue(null);
                if (instance == null) return false;
                System.Object debugSection = debugField.GetValue(instance);
                if (debugSection == null) return false;
                FieldInfo modelViewerField = debugSection.GetType().GetField("StartModelViewer", BindingFlags.Instance | BindingFlags.Public);
                if (modelViewerField == null) return false;
                System.Object iniValue = modelViewerField.GetValue(debugSection);
                if (iniValue == null) return false;
                FieldInfo valueField = iniValue.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueField == null) return false;
                valueField.SetValue(iniValue, enabled);
                return Memoria.Configuration.Debug.StartModelViewer == enabled;
            }
            catch (Exception ex) { Log.Error("Runtime Model Viewer flag reflection failed: " + ex); return false; }
        }

        private void SaveViewerCameraState()
        {
            try
            {
                GameObject cameraObject = GameObject.Find("FieldMap Camera");
                Camera camera = _originalFieldCamera != null ? _originalFieldCamera : (cameraObject != null ? cameraObject.GetComponent<Camera>() : null);
                if (camera == null) return;
                _savedCameraColor = camera.backgroundColor;
                _hasSavedCameraColor = true;
            }
            catch (Exception ex) { Log.Warning("Could not save Model Viewer camera state: " + ex.Message); }
        }

        private void RestoreViewerCameraState()
        {
            if (!_hasSavedCameraColor) return;
            try
            {
                GameObject cameraObject = GameObject.Find("FieldMap Camera");
                Camera camera = _originalFieldCamera != null ? _originalFieldCamera : (cameraObject != null ? cameraObject.GetComponent<Camera>() : null);
                if (camera != null) camera.backgroundColor = _savedCameraColor;
            }
            catch (Exception ex) { Log.Warning("Could not restore Model Viewer camera state: " + ex.Message); }
            finally { _hasSavedCameraColor = false; }
        }

        private static void CleanupModelViewerObjects()
        {
            Type viewerType = typeof(ModelViewerScene);
            DestroyFieldUnityObject(viewerType, "currentModel");
            DestroyFieldUnityObject(viewerType, "currentModelWrapper");
            DestroyFieldUnityObject(viewerType, "currentWeaponModel");
            DestroyFieldUnityObject(viewerType, "currentFloorModel");
            DestroyFieldUnityObject(viewerType, "InsertTextGUI");
            DestroyFieldUnityObject(viewerType, "backgroundGo");
            DestroyFieldUnityObject(viewerType, "labelGo");
            DestroyComponentFieldGameObject(viewerType, "spsEffect");
            DestroyControlPanel(viewerType, "infoPanel");
            DestroyControlPanel(viewerType, "controlPanel");
            DestroyControlPanel(viewerType, "extraInfoPanel");
            DestroyUnityObjectList(viewerType, "boneModels");
            DestroyUnityObjectList(viewerType, "boneConnectModels");
            CloseDialogList(viewerType, "boneDialogs");
            DestroyNamedGameObject("ModelViewer_SPS");
            DestroyNamedGameObject("ModelViewerWMLight0");
            DestroyNamedGameObject("ModelViewerWMLight1");
            DestroyNamedGameObject("ModelViewerWMLight2");
        }

        private static System.Object GetStaticFieldValue(Type type, String fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return field == null ? null : field.GetValue(null);
        }

        private static void SetStaticFieldValue(Type type, String fieldName, System.Object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && !field.IsInitOnly) field.SetValue(null, value);
        }

        private static void DestroyFieldUnityObject(Type viewerType, String fieldName)
        {
            System.Object value = GetStaticFieldValue(viewerType, fieldName);
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            if (unityObject != null) UnityEngine.Object.Destroy(unityObject);
            SetStaticFieldValue(viewerType, fieldName, null);
        }

        private static void DestroyComponentFieldGameObject(Type viewerType, String fieldName)
        {
            System.Object value = GetStaticFieldValue(viewerType, fieldName);
            Component component = value as Component;
            if (component != null && component.gameObject != null) UnityEngine.Object.Destroy(component.gameObject);
            SetStaticFieldValue(viewerType, fieldName, null);
        }

        private static void DestroyControlPanel(Type viewerType, String fieldName)
        {
            System.Object panel = GetStaticFieldValue(viewerType, fieldName);
            if (panel == null) return;
            try
            {
                PropertyInfo basePanelProperty = panel.GetType().GetProperty("BasePanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                System.Object basePanel = basePanelProperty == null ? null : basePanelProperty.GetValue(panel, null);
                Component component = basePanel as Component;
                if (component != null && component.gameObject != null) UnityEngine.Object.Destroy(component.gameObject);
                else
                {
                    UnityEngine.Object unityObject = basePanel as UnityEngine.Object;
                    if (unityObject != null) UnityEngine.Object.Destroy(unityObject);
                }
            }
            catch (Exception ex) { Log.Warning("Model Viewer panel cleanup failed: " + ex.Message); }
            SetStaticFieldValue(viewerType, fieldName, null);
        }





        private static void DestroyUnityObjectList(Type viewerType, String fieldName)
        {
            IEnumerable list = GetStaticFieldValue(viewerType, fieldName) as IEnumerable;
            if (list == null) return;
            foreach (System.Object item in list)
            {
                UnityEngine.Object unityObject = item as UnityEngine.Object;
                if (unityObject != null) UnityEngine.Object.Destroy(unityObject);
            }
            System.Object raw = GetStaticFieldValue(viewerType, fieldName);
            if (raw != null)
            {
                MethodInfo clear = raw.GetType().GetMethod("Clear", Type.EmptyTypes);
                if (clear != null) clear.Invoke(raw, null);
            }
        }

        private static void CloseDialogList(Type viewerType, String fieldName)
        {
            IEnumerable list = GetStaticFieldValue(viewerType, fieldName) as IEnumerable;
            if (list == null) return;
            foreach (System.Object item in list)
            {
                if (item == null) continue;
                try
                {
                    MethodInfo forceClose = item.GetType().GetMethod("ForceClose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (forceClose != null) forceClose.Invoke(item, null);
                }
                catch {}
            }
            System.Object raw = GetStaticFieldValue(viewerType, fieldName);
            if (raw != null)
            {
                MethodInfo clear = raw.GetType().GetMethod("Clear", Type.EmptyTypes);
                if (clear != null) clear.Invoke(raw, null);
            }
        }

        private static void DestroyNamedGameObject(String name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        private void DumpCurrentModelToFbx(GameObject model)
        {
            if (model == null) return;
            try
            {
                Int32 modelId;
                String modelName;
                Boolean hasMetadata = TryGetCurrentModelViewerMetadata(out modelId, out modelName);
                ExportModelToFbx(model, hasMetadata ? modelId : -1, modelName);
            }
            catch (Exception ex) { Log.Error("[Dev Console][FBX Dump] FAILED: " + ex); }
        }

        public String DumpFbxByModelId(Int32 modelId)
        {
            GameObject model = null;
            try
            {
                String modelName = null;
                foreach (KeyValuePair<Int32, String> entry in FF9BattleDB.GEO)
                    if (entry.Key == modelId) { modelName = entry.Value; break; }
                if (String.IsNullOrEmpty(modelName))
                    return "Model ID " + modelId + " was not found in the GEO model database.";
                // Same loader as normal GEO models in ModelViewerScene.ChangeModel.
                // Export in root-local space: the shared FBX writer owns orientation
                // and bind-pose corrections, not the viewer's display transforms.
                model = ModelFactory.CreateModel(modelName, false, true,
                    Memoria.Configuration.Graphics.ElementsSmoothTexture);
                if (model == null) return "Could not load model " + modelId + " (" + modelName + ").";
                model.SetActive(false);
                String path = ExportModelToFbx(model, modelId, modelName);
                return "FBX export complete: " + modelId + " / " + modelName + "\r\nOutput: " + path;
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console][FBX Dump] FAILED: " + ex);
                return "FBX export failed for model " + modelId + ": " + ex.Message;
            }
            finally
            {
                if (model != null) UnityEngine.Object.Destroy(model);
            }
        }

        private static String ExportModelToFbx(GameObject model, Int32 modelId, String modelName)
        {
            if (String.IsNullOrEmpty(modelName)) modelName = model.name;
            String safeName = MakeSafeFileName(modelName);
            String folderName = modelId >= 0
                ? modelId.ToString(CultureInfo.InvariantCulture) + "_" + safeName : safeName;
            String assetDumpRoot = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Memoria Dev Console"), "Asset Dump");
            String modelFolder = Path.Combine(Path.Combine(assetDumpRoot, "FBX"), folderName);
            Directory.CreateDirectory(modelFolder);
            String stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            String outputPath = Path.Combine(modelFolder, "FBX_Dump_" + safeName + "_" + stamp + ".fbx");
            String diagnosticPath = Path.Combine(modelFolder, "FBX_Dump_" + safeName + "_" + stamp + "_RIG_DEBUG.txt");
            File.WriteAllText(diagnosticPath, BuildFullRigDiagnostic(model), new UTF8Encoding(false));
            File.WriteAllText(outputPath, BuildAsciiFbx(model, modelFolder), new UTF8Encoding(false));
            ViewerDiagnostics.Message("[Dev Console][FBX Dump] SUCCESS: " + outputPath);
            return outputPath;
        }
        private static Boolean TryGetCurrentModelViewerMetadata(out Int32 modelId, out String modelName)
        {
            modelId = -1;
            modelName = null;

            try
            {
                Type viewerType = typeof(ModelViewerScene);
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo geoListField = viewerType.GetField("geoList", flags);
                FieldInfo currentGeoIndexField = viewerType.GetField("currentGeoIndex", flags);

                if (geoListField == null || currentGeoIndexField == null)
                    return false;

                IList geoList = geoListField.GetValue(null) as IList;
                if (geoList == null)
                    return false;

                Int32 currentGeoIndex = Convert.ToInt32(currentGeoIndexField.GetValue(null), CultureInfo.InvariantCulture);
                if (currentGeoIndex < 0 || currentGeoIndex >= geoList.Count)
                    return false;

                System.Object modelObject = geoList[currentGeoIndex];
                if (modelObject == null)
                    return false;

                Type modelObjectType = modelObject.GetType();
                FieldInfo idField = modelObjectType.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo nameField = modelObjectType.GetField("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo idProperty = modelObjectType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo nameProperty = modelObjectType.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                System.Object idValue = idField != null ? idField.GetValue(modelObject) : (idProperty != null ? idProperty.GetValue(modelObject, null) : null);
                System.Object nameValue = nameField != null ? nameField.GetValue(modelObject) : (nameProperty != null ? nameProperty.GetValue(modelObject, null) : null);

                if (idValue == null || nameValue == null)
                    return false;

                modelId = Convert.ToInt32(idValue, CultureInfo.InvariantCulture);
                modelName = nameValue as String;
                return !String.IsNullOrEmpty(modelName);
            }
            catch
            {
                modelId = -1;
                modelName = null;
                return false;
            }
        }

        private static String BuildFullRigDiagnostic(GameObject root)
        {
            StringBuilder sb = new StringBuilder(1024 * 512);
            sb.AppendLine("================================================================================");
            sb.AppendLine("MEMORIA DEV CONSOLE - FULL LIVE UNITY MODEL/RIG DIAGNOSTIC");
            sb.AppendLine("================================================================================");
            if (root == null) { sb.AppendLine("ROOT: <null>"); return sb.ToString(); }

            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (Int32 i = 0; i < allTransforms.Length; i++)
            {
                Transform t = allTransforms[i];
                AppendTransformDiagnostic(sb, "TRANSFORM", t, root.transform);
            }
            return sb.ToString();
        }

        private static void AppendTransformDiagnostic(StringBuilder sb, String label, Transform t, Transform root)
        {
            if (t == null) return;
            sb.AppendLine(label + ".Name=" + t.name);
            sb.AppendLine(label + ".Path=" + TransformPathOrNull(t, root));
        }

        private static String TransformPathOrNull(Transform t, Transform stopRoot)
        {
            if (t == null) return "<null>";
            List<String> parts = new List<String>();
            Transform current = t;
            while (current != null)
            {
                parts.Add(current.name);
                if (stopRoot != null && current == stopRoot) break;
                current = current.parent;
            }
            parts.Reverse();
            return String.Join("/", parts.ToArray());
        }

        private static String BuildAsciiFbx(GameObject root, String textureDirectory)
        {
            FbxAsciiWriter writer = new FbxAsciiWriter(root, textureDirectory);
            return writer.Build();
        }

        private static String MakeSafeFileName(String value)
        {
            if (String.IsNullOrEmpty(value)) return "Model";
            Char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder(value.Length);
            for (Int32 i = 0; i < value.Length; i++)
            {
                Char c = value[i];
                Boolean bad = false;
                for (Int32 j = 0; j < invalid.Length; j++) { if (c == invalid[j]) { bad = true; break; } }
                result.Append(bad ? '_' : c);
            }
            return result.ToString();
        }

        private sealed class FbxAsciiWriter
        {
            private sealed class MeshRecord
            {
                public Int64 GeometryId;
                public Int64 ModelId;
                public String Name;
                public Mesh Mesh;
                public Matrix4x4 ModelMatrix;
                public SkinnedMeshRenderer SkinRenderer;
                public Int64 SkinId;
                public Int64 MaterialId;
                public String MaterialName;
                public Material UnityMaterial;
                public Int64 TextureId;
                public Int64 VideoId;
                public String TextureFileName;
                public readonly List<BoneRecord> Bones = new List<BoneRecord>();
            }

            private sealed class GlobalBoneRecord
            {
                public Int32 BoneIndex;
                public Int64 ModelId;
                public String Name;
                public Transform Transform;
                public Int32 ParentBoneIndex;
                public Vector3 LocalPosition;
                public Vector3 LocalRotationEuler;
                public Vector3 LocalScale;
            }

            private sealed class BoneRecord
            {
                public Int32 BoneIndex;
                public Int32 GlobalBoneIndex;
                public Int64 ModelId;
                public Int64 ClusterId;
                public String Name;
                public Matrix4x4 GlobalBindMatrix;
                public readonly List<Int32> VertexIndices = new List<Int32>();
                public readonly List<Single> VertexWeights = new List<Single>();
            }

            private readonly GameObject _root;
            private readonly String _textureDirectory;
            private readonly List<MeshRecord> _meshes = new List<MeshRecord>();
            private readonly List<GlobalBoneRecord> _globalBones = new List<GlobalBoneRecord>();
            private readonly Dictionary<Transform, Int32> _globalBoneLookup = new Dictionary<Transform, Int32>();
            private Int64 _nextId = 100000L;
            private Int64 _rootModelId;

            public FbxAsciiWriter(GameObject root, String textureDirectory)
            {
                _root = root;
                _textureDirectory = textureDirectory;
            }

            public String Build()
            {
                Collect();

                if (_meshes.Count == 0)
                    throw new InvalidOperationException("The current Model Viewer object has no MeshFilter or SkinnedMeshRenderer meshes to export.");

                StringBuilder sb = new StringBuilder(1024 * 256);
                WriteHeader(sb);
                WriteGlobalSettings(sb);
                WriteDefinitions(sb);
                WriteObjects(sb);
                WriteConnections(sb);
                return sb.ToString();
            }

            private Int64 NewId()
            {
                return _nextId++;
            }

            private void Collect()
            {
                _rootModelId = NewId();
                Matrix4x4 rootWorldToLocal = _root.transform.worldToLocalMatrix;

                SkinnedMeshRenderer[] skinned = _root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                CollectSharedSkeleton(skinned, rootWorldToLocal);

                SkinnedMeshRenderer[] skinnedForMeshes = skinned;
                for (Int32 i = 0; i < skinnedForMeshes.Length; i++)
                {
                    SkinnedMeshRenderer smr = skinnedForMeshes[i];
                    if (smr == null || smr.sharedMesh == null)
                        continue;

                    MeshRecord record = new MeshRecord();
                    record.GeometryId = NewId();
                    record.ModelId = NewId();
                    record.SkinId = NewId();
                    record.MaterialId = NewId();
                    record.Name = String.IsNullOrEmpty(smr.name) ? ("SkinnedMesh_" + i) : smr.name;
                    record.MaterialName = record.Name + "_Material";
                    record.UnityMaterial = smr.sharedMaterial;
                    PrepareTextureRecord(record, i);
                    record.Mesh = smr.sharedMesh;
                    record.ModelMatrix = rootWorldToLocal * smr.transform.localToWorldMatrix;
                    record.SkinRenderer = smr;

                    CollectBones(record);
                    _meshes.Add(record);
                }

                MeshFilter[] filters = _root.GetComponentsInChildren<MeshFilter>(true);
                for (Int32 i = 0; i < filters.Length; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter == null || filter.sharedMesh == null)
                        continue;

                    // A GameObject with a SkinnedMeshRenderer is already handled above.
                    if (filter.GetComponent<SkinnedMeshRenderer>() != null)
                        continue;

                    MeshRecord record = new MeshRecord();
                    record.GeometryId = NewId();
                    record.ModelId = NewId();
                    record.SkinId = 0L;
                    record.MaterialId = NewId();
                    record.Name = String.IsNullOrEmpty(filter.name) ? ("Mesh_" + i) : filter.name;
                    record.MaterialName = record.Name + "_Material";
                    Renderer renderer = filter.GetComponent<Renderer>();
                    record.UnityMaterial = renderer != null ? renderer.sharedMaterial : null;
                    PrepareTextureRecord(record, i);
                    record.Mesh = filter.sharedMesh;
                    record.ModelMatrix = rootWorldToLocal * filter.transform.localToWorldMatrix;
                    record.SkinRenderer = null;
                    _meshes.Add(record);
                }
            }

            private void CollectSharedSkeleton(SkinnedMeshRenderer[] renderers, Matrix4x4 rootWorldToLocal)
            {
                HashSet<Transform> required = new HashSet<Transform>();

                // CRITICAL FIX:
                // Do NOT serialize the transforms' CURRENT animated pose as the FBX rest skeleton.
                // The Mesh.bindposes describe the mesh's actual bind/rest pose. Model Viewer can
                // have an animation playing while F11 is pressed, so t.localPosition/Rotation
                // are often an arbitrary animation frame and are NOT the correct FBX bind pose.
                //
                // Recover each bone's GLOBAL BIND transform in exported-root space:
                //
                //   Unity bindpose = bone.worldToLocal(bind) * renderer.localToWorld(bind)
                //   inverse(bindpose) = renderer.worldToLocal(bind) * bone.localToWorld(bind)
                //   meshModel * inverse(bindpose) = bone.localToWorld(bind), expressed under export root
                //
                // Then derive bone LOCAL bind transforms from parent/child global bind matrices.
                Dictionary<Transform, Matrix4x4> globalBindByTransform =
                    new Dictionary<Transform, Matrix4x4>();

                for (Int32 r = 0; r < renderers.Length; r++)
                {
                    SkinnedMeshRenderer smr = renderers[r];
                    if (smr == null || smr.sharedMesh == null || smr.bones == null)
                        continue;

                    Transform[] rendererBones = smr.bones;
                    Matrix4x4[] bindPoses = smr.sharedMesh.bindposes;
                    Matrix4x4 meshModel = rootWorldToLocal * smr.transform.localToWorldMatrix;

                    Int32 bindCount = bindPoses != null ? Math.Min(rendererBones.Length, bindPoses.Length) : 0;

                    for (Int32 b = 0; b < rendererBones.Length; b++)
                    {
                        Transform t = rendererBones[b];

                        Transform walk = t;
                        while (walk != null && walk != _root.transform)
                        {
                            required.Add(walk);
                            walk = walk.parent;
                        }

                        if (t != null && b < bindCount && !globalBindByTransform.ContainsKey(t))
                            globalBindByTransform[t] = meshModel * bindPoses[b].inverse;
                    }
                }

                // Some FF9/helper bones are part of the hierarchy but are not directly
                // referenced by any renderer bone slot, so Unity gives us no bindpose entry
                // for them. The previous build fell back to their CURRENT animated transform.
                // That is exactly why isolated chains (feet/hands/root helpers) could still
                // hang away from an otherwise-correct bind skeleton.
                //
                // Infer those missing helper GLOBAL bind transforms from a direct child whose
                // bind transform IS known. The helper itself is unweighted, so the critical
                // invariant is that the weighted child's global bind remains exact.
                //
                // We preserve the child's hierarchy offset (localPosition/localScale) and use
                // identity local bind rotation for the otherwise unknowable helper link:
                //
                //   parentBind = childGlobalBind * inverse(TRS(child.localPosition, I, child.localScale))
                //
                // Then the later parent^-1 * child calculation gives that child the exact
                // global bind matrix from Unity while placing the helper at the correct joint
                // location instead of at an arbitrary animation-frame pose.
                Int32 inferredHelperCount = 0;
                Boolean inferredSomething = true;
                Int32 inferencePass = 0;

                while (inferredSomething && inferencePass < 64)
                {
                    inferredSomething = false;
                    inferencePass++;

                    List<Transform> requiredSnapshot = new List<Transform>(required);
                    for (Int32 p = 0; p < requiredSnapshot.Count; p++)
                    {
                        Transform parent = requiredSnapshot[p];
                        if (parent == null || globalBindByTransform.ContainsKey(parent))
                            continue;

                        Transform chosenChild = null;
                        Matrix4x4 chosenChildGlobal = Matrix4x4.identity;

                        // Prefer a direct hierarchy child with a real/inferred bind matrix.
                        for (Int32 c = 0; c < parent.childCount; c++)
                        {
                            Transform child = parent.GetChild(c);
                            Matrix4x4 childGlobal;
                            if (child != null &&
                                required.Contains(child) &&
                                globalBindByTransform.TryGetValue(child, out childGlobal))
                            {
                                chosenChild = child;
                                chosenChildGlobal = childGlobal;
                                break;
                            }
                        }

                        if (chosenChild == null)
                            continue;

                        Vector3 childBindPos = chosenChild.localPosition;
                        Vector3 childBindScale = chosenChild.localScale;

                        // Animation changes local rotation constantly; local position/scale are
                        // the stable hierarchy information we can safely retain here.
                        Matrix4x4 assumedChildLocal =
                            Matrix4x4.TRS(childBindPos, Quaternion.identity, childBindScale);

                        Matrix4x4 inferredParentGlobal =
                            chosenChildGlobal * assumedChildLocal.inverse;

                        globalBindByTransform[parent] = inferredParentGlobal;
                        inferredHelperCount++;
                        inferredSomething = true;

                        ViewerDiagnostics.Message("[Dev Console][FBX Dump] Inferred helper bind: " +
                                    parent.name + " <- child " + chosenChild.name);
                    }
                }

                ViewerDiagnostics.Message("[Dev Console][FBX Dump] Helper bind transforms inferred: " +
                            inferredHelperCount);

                List<Transform> ordered = new List<Transform>(required);
                ordered.Sort(delegate(Transform a, Transform b)
                {
                    Int32 depthCompare = GetTransformDepth(a).CompareTo(GetTransformDepth(b));
                    if (depthCompare != 0)
                        return depthCompare;

                    // Keep ordering deterministic when siblings share a depth.
                    return String.CompareOrdinal(TransformHierarchyKey(a), TransformHierarchyKey(b));
                });

                // First create all records/lookups so parent indices are always resolvable.
                for (Int32 i = 0; i < ordered.Count; i++)
                {
                    Transform t = ordered[i];
                    GlobalBoneRecord bone = new GlobalBoneRecord();
                    bone.BoneIndex = i;
                    bone.ModelId = NewId();
                    bone.Transform = t;
                    bone.ParentBoneIndex = -1;

                    String originalName = t != null ? t.name : null;
                    String suffix = i.ToString("000", CultureInfo.InvariantCulture);
                    if (!String.IsNullOrEmpty(originalName) &&
                        originalName.Length >= 3 &&
                        Char.IsDigit(originalName[originalName.Length - 1]) &&
                        Char.IsDigit(originalName[originalName.Length - 2]) &&
                        Char.IsDigit(originalName[originalName.Length - 3]))
                        bone.Name = originalName;
                    else
                        bone.Name = "bone" + suffix;

                    _globalBoneLookup[t] = i;
                    _globalBones.Add(bone);
                }

                // Resolve hierarchy and reconstruct the REST/BIND local transforms.
                for (Int32 i = 0; i < _globalBones.Count; i++)
                {
                    GlobalBoneRecord bone = _globalBones[i];
                    Transform t = bone.Transform;

                    if (t != null && t.parent != null)
                    {
                        Int32 parentIndex;
                        if (_globalBoneLookup.TryGetValue(t.parent, out parentIndex))
                            bone.ParentBoneIndex = parentIndex;
                    }

                    // Older Memoria compiler does not prove definite assignment through
                    // `t != null && TryGetValue(..., out childGlobalBind)`, so initialize
                    // explicitly before the lookup.
                    Matrix4x4 childGlobalBind = Matrix4x4.identity;
                    Boolean hasChildBind = false;
                    if (t != null)
                        hasChildBind = globalBindByTransform.TryGetValue(t, out childGlobalBind);

                    if (hasChildBind)
                    {
                        Matrix4x4 localBind = childGlobalBind;

                        if (bone.ParentBoneIndex >= 0 && bone.ParentBoneIndex < _globalBones.Count)
                        {
                            Transform parentTransform = _globalBones[bone.ParentBoneIndex].Transform;
                            Matrix4x4 parentGlobalBind;
                            if (parentTransform != null &&
                                globalBindByTransform.TryGetValue(parentTransform, out parentGlobalBind))
                            {
                                localBind = parentGlobalBind.inverse * childGlobalBind;
                            }
                            else
                            {
                                // Do NOT use the live animated parent here. If inference could not
                                // recover this helper, keeping childGlobalBind directly is safer
                                // than mixing bind-space with an arbitrary animation-frame matrix.
                                localBind = childGlobalBind;
                            }
                        }

                        Vector3 bindPos;
                        Vector3 bindEuler;
                        Vector3 bindScale;
                        Decompose(localBind, out bindPos, out bindEuler, out bindScale);

                        bone.LocalPosition = bindPos;
                        bone.LocalRotationEuler = bindEuler;
                        bone.LocalScale = bindScale;
                    }
                    else
                    {
                        // Last-resort helper fallback: never bake the CURRENT animation rotation
                        // into the FBX rest skeleton. Position/scale are useful hierarchy data;
                        // rest rotation is neutral when no bind evidence exists.
                        bone.LocalPosition = t != null ? t.localPosition : Vector3.zero;
                        bone.LocalRotationEuler = Vector3.zero;
                        bone.LocalScale = t != null ? t.localScale : Vector3.one;
                    }
                }

                ViewerDiagnostics.Message("[Dev Console][FBX Dump] Shared skeleton bones: " + _globalBones.Count +
                            " | bind-pose matrices recovered: " + globalBindByTransform.Count);

                if (_globalBones.Count > 0)
                {
                    GlobalBoneRecord b0 = _globalBones[0];
                    ViewerDiagnostics.Message("[Dev Console][FBX Dump] bone0 BIND local: pos=" + b0.LocalPosition +
                        " rot=" + b0.LocalRotationEuler + " scale=" + b0.LocalScale);
                }
            }

            private static String TransformHierarchyKey(Transform t)
            {
                if (t == null)
                    return String.Empty;

                List<String> parts = new List<String>();
                Transform current = t;
                while (current != null)
                {
                    parts.Add(current.name);
                    current = current.parent;
                }
                parts.Reverse();
                return String.Join("/", parts.ToArray());
            }

            private static Matrix4x4 ApplyExportOrientation(Matrix4x4 matrix)
            {
                Matrix4x4 rot = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 180f), Vector3.one);
                return rot * matrix;
            }

            private static Int32 GetTransformDepth(Transform t)
            {
                Int32 depth = 0;
                while (t != null)
                {
                    depth++;
                    t = t.parent;
                }
                return depth;
            }

            private void PrepareTextureRecord(MeshRecord record, Int32 index)
            {
                Texture texture = record.UnityMaterial != null ? record.UnityMaterial.mainTexture : null;
                if (texture == null)
                    return;

                record.TextureId = NewId();
                record.VideoId = NewId();
                String textureName = String.IsNullOrEmpty(texture.name) ? (record.Name + "_tex_" + index) : texture.name;
                record.TextureFileName = MakeSafeFileName(textureName) + ".png";

                try
                {
                    Texture2D readable = TextureHelper.CopyAsReadable(texture);
                    if (readable != null)
                    {
                        TextureHelper.WriteTextureToFile(readable, Path.Combine(_textureDirectory, record.TextureFileName));
                        if (!UnityEngine.Object.ReferenceEquals(readable, texture))
                            UnityEngine.Object.DestroyImmediate(readable);
                        ViewerDiagnostics.Message("[Dev Console][FBX Dump] Texture: " + record.TextureFileName);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[Dev Console][FBX Dump] Texture export failed for " + textureName + ": " + ex.Message);
                    record.TextureId = 0L;
                    record.VideoId = 0L;
                    record.TextureFileName = null;
                }
            }

            private void CollectBones(MeshRecord record)
            {
                SkinnedMeshRenderer smr = record.SkinRenderer;
                Mesh mesh = record.Mesh;
                Transform[] bones = smr.bones;
                Matrix4x4[] bindPoses = mesh.bindposes;

                if (bones == null || bindPoses == null)
                    return;

                Int32 count = Math.Min(bones.Length, bindPoses.Length);
                Dictionary<Int32, Int32> meshBoneToRecordBone = new Dictionary<Int32, Int32>();

                for (Int32 i = 0; i < count; i++)
                {
                    Transform t = bones[i];
                    Int32 globalIndex;
                    if (t == null || !_globalBoneLookup.TryGetValue(t, out globalIndex))
                        continue;

                    GlobalBoneRecord global = _globalBones[globalIndex];

                    BoneRecord bone = new BoneRecord();
                    bone.BoneIndex = i;
                    bone.GlobalBoneIndex = globalIndex;
                    bone.ModelId = global.ModelId;
                    bone.ClusterId = NewId();
                    bone.Name = global.Name;

                    // Keep cluster matrices in the SAME coordinate system as the mesh and
                    // bone nodes. The 180-degree display/orientation correction lives on the
                    // common FBX root node, so applying it again here double-converts the skin.
                    //
                    // Unity bindpose = bone.worldToLocal(bind) * renderer.localToWorld(bind)
                    // therefore record.ModelMatrix * inverse(bindpose) is the bone's bind
                    // transform in exported-root space.
                    bone.GlobalBindMatrix = record.ModelMatrix * bindPoses[i].inverse;

                    meshBoneToRecordBone[i] = record.Bones.Count;
                    record.Bones.Add(bone);
                }

                BoneWeight[] weights = mesh.boneWeights;
                if (weights == null)
                    return;

                for (Int32 vertex = 0; vertex < weights.Length; vertex++)
                {
                    BoneWeight bw = weights[vertex];
                    AddWeightMapped(record, meshBoneToRecordBone, bw.boneIndex0, vertex, bw.weight0);
                    AddWeightMapped(record, meshBoneToRecordBone, bw.boneIndex1, vertex, bw.weight1);
                    AddWeightMapped(record, meshBoneToRecordBone, bw.boneIndex2, vertex, bw.weight2);
                    AddWeightMapped(record, meshBoneToRecordBone, bw.boneIndex3, vertex, bw.weight3);
                }
            }

            private static void AddWeightMapped(MeshRecord record, Dictionary<Int32, Int32> map, Int32 meshBoneIndex, Int32 vertexIndex, Single weight)
            {
                if (weight <= 0f)
                    return;

                Int32 recordBoneIndex;
                if (!map.TryGetValue(meshBoneIndex, out recordBoneIndex))
                    return;

                record.Bones[recordBoneIndex].VertexIndices.Add(vertexIndex);
                record.Bones[recordBoneIndex].VertexWeights.Add(weight);
            }

            private static void AddWeight(MeshRecord record, Int32 boneIndex, Int32 vertexIndex, Single weight)
            {
                if (weight <= 0f || boneIndex < 0 || boneIndex >= record.Bones.Count)
                    return;

                record.Bones[boneIndex].VertexIndices.Add(vertexIndex);
                record.Bones[boneIndex].VertexWeights.Add(weight);
            }

            private static void WriteHeader(StringBuilder sb)
            {
                sb.AppendLine("; FBX 7.4.0 project file");
                sb.AppendLine("; Created by Memoria Dev Console runtime Model Viewer FBX dumper");
                sb.AppendLine("FBXHeaderExtension:  {");
                sb.AppendLine("\tFBXHeaderVersion: 1003");
                sb.AppendLine("\tFBXVersion: 7400");
                sb.AppendLine("\tCreator: \"Memoria Dev Console\"");
                sb.AppendLine("}");
            }

            private static void WriteGlobalSettings(StringBuilder sb)
            {
                sb.AppendLine("GlobalSettings:  {");
                sb.AppendLine("\tVersion: 1000");
                sb.AppendLine("\tProperties70:  {");
                sb.AppendLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
                sb.AppendLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
                sb.AppendLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
                sb.AppendLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
                sb.AppendLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
                sb.AppendLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
                sb.AppendLine("\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",1");
                sb.AppendLine("\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",1");
                sb.AppendLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
                sb.AppendLine("\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",1");
                sb.AppendLine("\t}");
                sb.AppendLine("}");
            }

            private static void WriteDefinitions(StringBuilder sb)
            {
                // Critical for Memoria's FBX importer:
                // FbxDocument.GetAllBones() reads the Model PropertyTemplate's
                // RotationOrder. If this block is absent, Memoria falls back to XYZ (0).
                //
                // Unity's Quaternion.eulerAngles convention is ZXY. We export the live
                // Unity bone Euler values directly, so tell Memoria to reconstruct them
                // using its matching EULER_ZXY enum value (4).
                sb.AppendLine("Definitions:  {");
                sb.AppendLine("\tVersion: 100");
                sb.AppendLine("\tCount: 1");
                sb.AppendLine("\tObjectType: \"Model\" {");
                sb.AppendLine("\t\tCount: 0");
                sb.AppendLine("\t\tPropertyTemplate: \"FbxNode\" {");
                sb.AppendLine("\t\t\tProperties70:  {");
                sb.AppendLine("\t\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
                sb.AppendLine("}");
            }


            private void WriteObjects(StringBuilder sb)
            {
                sb.AppendLine("Objects:  {");
                // Memoria's importer consumes FBX model transforms directly. The runtime
                // Unity model comes back vertically inverted if we leave the FBX root at
                // identity, so rotate the entire exported hierarchy 180 degrees around Z.
                WriteModelNode(sb, _rootModelId, MakeFbxName(_root.name), "Null",
                    Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 180f), Vector3.one));

                for (Int32 i = 0; i < _meshes.Count; i++)
                {
                    MeshRecord record = _meshes[i];
                    WriteGeometry(sb, record);
                    WriteModelNode(sb, record.ModelId, MakeFbxName(record.Name + "_Mesh"), "Mesh", record.ModelMatrix);
                    WriteMaterial(sb, record);
                    WriteTexture(sb, record);

                    if (record.SkinRenderer != null && record.Bones.Count > 0)
                    {
                        WriteSkin(sb, record);

                        for (Int32 b = 0; b < record.Bones.Count; b++)
                            WriteCluster(sb, record, record.Bones[b]);
                    }
                }

                // Write ONE shared armature for the whole model. FFIX models commonly use
                // several SkinnedMeshRenderers that all reference pieces of the same skeleton;
                // emitting a separate armature per renderer caused Memoria to discover only
                // a single root bone ("0").
                for (Int32 b = 0; b < _globalBones.Count; b++)
                {
                    GlobalBoneRecord bone = _globalBones[b];
                    // The near-working reference FBX uses bone000 as a LimbNode directly
                    // under the model root; Memoria's FbxSkeleton fallback recognizes it.
                    WriteModelNodeDirect(sb, bone.ModelId, MakeFbxName(bone.Name), "LimbNode",
                        bone.LocalPosition, bone.LocalRotationEuler, bone.LocalScale);
                }

                sb.AppendLine("}");
            }

            private static void WriteGeometry(StringBuilder sb, MeshRecord record)
            {
                Mesh mesh = record.Mesh;
                Vector3[] vertices = mesh.vertices;
                Int32[] triangles = mesh.triangles;
                Vector3[] normals = mesh.normals;
                Vector2[] uv = mesh.uv;

                sb.Append("\tGeometry: ").Append(record.GeometryId).Append(", \"Geometry::").Append(MakeFbxName(record.Name)).AppendLine("\", \"Mesh\" {");
                sb.AppendLine("\t\tGeometryVersion: 124");

                sb.Append("\t\tVertices: *").Append(vertices.Length * 3).AppendLine(" {");
                sb.Append("\t\t\ta: ");
                for (Int32 i = 0; i < vertices.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendVector3(sb, vertices[i]);
                }
                sb.AppendLine();
                sb.AppendLine("\t\t}");

                sb.Append("\t\tPolygonVertexIndex: *").Append(triangles.Length).AppendLine(" {");
                sb.Append("\t\t\ta: ");
                for (Int32 i = 0; i < triangles.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    Int32 index = triangles[i];
                    if ((i % 3) == 2)
                        index = -index - 1;
                    sb.Append(index.ToString(CultureInfo.InvariantCulture));
                }
                sb.AppendLine();
                sb.AppendLine("\t\t}");

                if (normals != null && normals.Length == vertices.Length)
                {
                    sb.AppendLine("\t\tLayerElementNormal: 0 {");
                    sb.AppendLine("\t\t\tVersion: 101");
                    sb.AppendLine("\t\t\tName: \"\"");
                    sb.AppendLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                    sb.Append("\t\t\tNormals: *").Append(normals.Length * 3).AppendLine(" {");
                    sb.Append("\t\t\t\ta: ");
                    for (Int32 i = 0; i < normals.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        AppendVector3(sb, normals[i]);
                    }
                    sb.AppendLine();
                    sb.AppendLine("\t\t\t}");
                    sb.AppendLine("\t\t}");
                }

                if (uv != null && uv.Length == vertices.Length)
                {
                    sb.AppendLine("\t\tLayerElementUV: 0 {");
                    sb.AppendLine("\t\t\tVersion: 101");
                    sb.AppendLine("\t\t\tName: \"UVChannel_1\"");
                    sb.AppendLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                    sb.Append("\t\t\tUV: *").Append(uv.Length * 2).AppendLine(" {");
                    sb.Append("\t\t\t\ta: ");
                    for (Int32 i = 0; i < uv.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        AppendDouble(sb, uv[i].x);
                        sb.Append(',');
                        AppendDouble(sb, uv[i].y);
                    }
                    sb.AppendLine();
                    sb.AppendLine("\t\t\t}");
                    sb.AppendLine("\t\t}");
                }

                sb.AppendLine("\t\tLayer: 0 {");
                sb.AppendLine("\t\t\tVersion: 100");
                if (normals != null && normals.Length == vertices.Length)
                {
                    sb.AppendLine("\t\t\tLayerElement:  {");
                    sb.AppendLine("\t\t\t\tType: \"LayerElementNormal\"");
                    sb.AppendLine("\t\t\t\tTypedIndex: 0");
                    sb.AppendLine("\t\t\t}");
                }
                if (uv != null && uv.Length == vertices.Length)
                {
                    sb.AppendLine("\t\t\tLayerElement:  {");
                    sb.AppendLine("\t\t\t\tType: \"LayerElementUV\"");
                    sb.AppendLine("\t\t\t\tTypedIndex: 0");
                    sb.AppendLine("\t\t\t}");
                }
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
            }

            private static void WriteModelNode(StringBuilder sb, Int64 id, String name, String type, Matrix4x4 localMatrix)
            {
                Vector3 translation;
                Vector3 rotation;
                Vector3 scale;
                Decompose(localMatrix, out translation, out rotation, out scale);

                sb.Append("\tModel: ").Append(id).Append(", \"Model::").Append(name).Append("\", \"").Append(type).AppendLine("\" {");
                sb.AppendLine("\t\tVersion: 232");
                sb.AppendLine("\t\tProperties70:  {");
                sb.Append("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",");
                AppendVector3(sb, translation);
                sb.AppendLine();
                sb.Append("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",");
                AppendVector3(sb, rotation);
                sb.AppendLine();
                sb.Append("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",");
                AppendVector3(sb, scale);
                sb.AppendLine();
                if (type == "LimbNode")
                    sb.AppendLine("\t\t\tP: \"Size\", \"double\", \"Number\", \"\",1");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tShading: T");
                sb.AppendLine("\t\tCulling: \"CullingOff\"");
                sb.AppendLine("\t}");
            }


            private static void WriteModelNodeDirect(StringBuilder sb, Int64 id, String name, String type,
                Vector3 translation, Vector3 rotationEuler, Vector3 scale)
            {
                sb.Append("\tModel: ").Append(id).Append(", \"Model::").Append(name).Append("\", \"").Append(type).AppendLine("\" {");
                sb.AppendLine("\t\tVersion: 232");
                sb.AppendLine("\t\tProperties70:  {");
                sb.Append("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",");
                AppendVector3(sb, translation);
                sb.AppendLine();
                sb.Append("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",");
                AppendVector3(sb, rotationEuler);
                sb.AppendLine();
                sb.Append("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",");
                AppendVector3(sb, scale);
                sb.AppendLine();
                if (type == "LimbNode")
                    sb.AppendLine("\t\t\tP: \"Size\", \"double\", \"Number\", \"\",1");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tShading: T");
                sb.AppendLine("\t\tCulling: \"CullingOff\"");
                sb.AppendLine("\t}");
            }


            private static void WriteMaterial(StringBuilder sb, MeshRecord record)
            {
                String materialName = MakeFbxName(record.MaterialName);
                Material material = record.UnityMaterial;
                Color color = Color.white;

                try
                {
                    if (material != null && material.HasProperty("_Color"))
                        color = material.color;
                }
                catch
                {
                }

                sb.Append("\tMaterial: ").Append(record.MaterialId).Append(", \"Material::").Append(materialName).AppendLine("\", \"\" {");
                sb.AppendLine("\t\tVersion: 102");
                sb.AppendLine("\t\tShadingModel: \"Phong\"");
                sb.AppendLine("\t\tMultiLayer: 0");
                sb.AppendLine("\t\tProperties70:  {");
                sb.Append("\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",");
                AppendDouble(sb, color.r);
                sb.Append(',');
                AppendDouble(sb, color.g);
                sb.Append(',');
                AppendDouble(sb, color.b);
                sb.AppendLine();
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
            }

            private static void WriteTexture(StringBuilder sb, MeshRecord record)
            {
                if (record.TextureId == 0L || record.VideoId == 0L || String.IsNullOrEmpty(record.TextureFileName))
                    return;

                String textureName = MakeFbxName(record.Name + "_DiffuseTexture");
                String fileName = record.TextureFileName.Replace("\\", "/");

                sb.Append("\tVideo: ").Append(record.VideoId).Append(", \"Video::").Append(textureName).AppendLine("\", \"Clip\" {");
                sb.AppendLine("\t\tType: \"Clip\"");
                sb.Append("\t\tFilename: \"").Append(fileName).AppendLine("\"");
                sb.Append("\t\tRelativeFilename: \"").Append(fileName).AppendLine("\"");
                sb.AppendLine("\t}");

                sb.Append("\tTexture: ").Append(record.TextureId).Append(", \"Texture::").Append(textureName).AppendLine("\", \"\" {");
                sb.AppendLine("\t\tType: \"TextureVideoClip\"");
                sb.AppendLine("\t\tVersion: 202");
                sb.Append("\t\tTextureName: \"Texture::").Append(textureName).AppendLine("\"");
                sb.Append("\t\tMedia: \"Video::").Append(textureName).AppendLine("\"");
                sb.Append("\t\tFileName: \"").Append(fileName).AppendLine("\"");
                sb.Append("\t\tRelativeFilename: \"").Append(fileName).AppendLine("\"");
                sb.AppendLine("\t\tModelUVTranslation: 0,0");
                sb.AppendLine("\t\tModelUVScaling: 1,1");
                sb.AppendLine("\t\tTexture_Alpha_Source: \"None\"");
                sb.AppendLine("\t\tCropping: 0,0,0,0");
                sb.AppendLine("\t}");
            }

            private static void WriteSkin(StringBuilder sb, MeshRecord record)
            {
                sb.Append("\tDeformer: ").Append(record.SkinId).Append(", \"Deformer::Skin_").Append(MakeFbxName(record.Name)).AppendLine("\", \"Skin\" {");
                sb.AppendLine("\t\tVersion: 101");
                sb.AppendLine("\t\tLink_DeformAcuracy: 50");
                sb.AppendLine("\t}");
            }

            private static void WriteCluster(StringBuilder sb, MeshRecord record, BoneRecord bone)
            {
                sb.Append("\tDeformer: ").Append(bone.ClusterId).Append(", \"SubDeformer::Cluster_").Append(MakeFbxName(record.Name + "_" + bone.Name + "_" + bone.BoneIndex)).AppendLine("\", \"Cluster\" {");
                sb.AppendLine("\t\tVersion: 100");
                sb.AppendLine("\t\tUserData: \"\", \"\"");

                sb.Append("\t\tIndexes: *").Append(bone.VertexIndices.Count).AppendLine(" {");
                sb.Append("\t\t\ta: ");
                for (Int32 i = 0; i < bone.VertexIndices.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(bone.VertexIndices[i].ToString(CultureInfo.InvariantCulture));
                }
                sb.AppendLine();
                sb.AppendLine("\t\t}");

                sb.Append("\t\tWeights: *").Append(bone.VertexWeights.Count).AppendLine(" {");
                sb.Append("\t\t\ta: ");
                for (Int32 i = 0; i < bone.VertexWeights.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendDouble(sb, bone.VertexWeights[i]);
                }
                sb.AppendLine();
                sb.AppendLine("\t\t}");

                WriteMatrixArray(sb, "Transform", record.ModelMatrix);
                WriteMatrixArray(sb, "TransformLink", bone.GlobalBindMatrix);
                sb.AppendLine("\t}");
            }

            private void WriteConnections(StringBuilder sb)
            {
                sb.AppendLine("Connections:  {");
                sb.Append("\tC: \"OO\",").Append(_rootModelId).AppendLine(",0");

                for (Int32 i = 0; i < _meshes.Count; i++)
                {
                    MeshRecord record = _meshes[i];
                    sb.Append("\tC: \"OO\",").Append(record.GeometryId).Append(',').Append(record.ModelId).AppendLine();
                    sb.Append("\tC: \"OO\",").Append(record.ModelId).Append(',').Append(_rootModelId).AppendLine();
                    // Memoria's FBX importer resolves a geometry's material through
                    // Geometry -> Model and then Material -> Model connections.
                    sb.Append("\tC: \"OO\",").Append(record.MaterialId).Append(',').Append(record.ModelId).AppendLine();
                    if (record.TextureId != 0L && record.VideoId != 0L)
                    {
                        sb.Append("\tC: \"OO\",").Append(record.VideoId).Append(',').Append(record.TextureId).AppendLine();
                        sb.Append("\tC: \"OP\",").Append(record.TextureId).Append(',').Append(record.MaterialId).AppendLine(",\"DiffuseColor\"");
                    }

                    if (record.SkinRenderer != null && record.Bones.Count > 0)
                    {
                        sb.Append("\tC: \"OO\",").Append(record.SkinId).Append(',').Append(record.GeometryId).AppendLine();

                        for (Int32 b = 0; b < record.Bones.Count; b++)
                        {
                            BoneRecord bone = record.Bones[b];
                            sb.Append("\tC: \"OO\",").Append(bone.ClusterId).Append(',').Append(record.SkinId).AppendLine();
                            sb.Append("\tC: \"OO\",").Append(bone.ModelId).Append(',').Append(bone.ClusterId).AppendLine();
                        }
                    }
                }

                for (Int32 b = 0; b < _globalBones.Count; b++)
                {
                    GlobalBoneRecord bone = _globalBones[b];
                    Int64 parentId = _rootModelId;
                    if (bone.ParentBoneIndex >= 0 && bone.ParentBoneIndex < _globalBones.Count)
                        parentId = _globalBones[bone.ParentBoneIndex].ModelId;
                    sb.Append("\tC: \"OO\",").Append(bone.ModelId).Append(',').Append(parentId).AppendLine();
                }

                sb.AppendLine("}");
            }

            private static void WriteMatrixArray(StringBuilder sb, String label, Matrix4x4 matrix)
            {
                sb.Append("\t\t").Append(label).AppendLine(": *16 {");
                sb.Append("\t\t\ta: ");
                for (Int32 row = 0; row < 4; row++)
                {
                    for (Int32 col = 0; col < 4; col++)
                    {
                        if (row != 0 || col != 0) sb.Append(',');
                        AppendDouble(sb, matrix[row, col]);
                    }
                }
                sb.AppendLine();
                sb.AppendLine("\t\t}");
            }

            private static void AppendVector3(StringBuilder sb, Vector3 value)
            {
                AppendDouble(sb, value.x);
                sb.Append(',');
                AppendDouble(sb, value.y);
                sb.Append(',');
                AppendDouble(sb, value.z);
            }

            private static void AppendDouble(StringBuilder sb, Double value)
            {
                sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            }

            private static String MakeFbxName(String value)
            {
                if (String.IsNullOrEmpty(value))
                    return "Unnamed";

                return value.Replace("\\", "_").Replace("/", "_").Replace(":", "_").Replace("\"", "'");
            }

            private static void Decompose(Matrix4x4 matrix, out Vector3 translation, out Vector3 rotationEuler, out Vector3 scale)
            {
                translation = new Vector3(matrix.m03, matrix.m13, matrix.m23);

                Vector3 x = new Vector3(matrix.m00, matrix.m10, matrix.m20);
                Vector3 y = new Vector3(matrix.m01, matrix.m11, matrix.m21);
                Vector3 z = new Vector3(matrix.m02, matrix.m12, matrix.m22);

                scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
                if (scale.x == 0f) scale.x = 1f;
                if (scale.y == 0f) scale.y = 1f;
                if (scale.z == 0f) scale.z = 1f;

                // Preserve a negative determinant rather than silently flipping handedness.
                if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f)
                    scale.x = -scale.x;

                x /= scale.x;
                y /= scale.y;
                z /= scale.z;

                Quaternion rotation = Quaternion.LookRotation(z, y);
                rotationEuler = rotation.eulerAngles;
            }
        }
    }
}
