using Memoria.Assets;
using Memoria.Prime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed class ModelViewerService
    {
        private readonly FieldStateService _fieldState;

        private Color _savedCameraColor;
        private Boolean _hasSavedCameraColor;
        private readonly List<Renderer> _suspendedFieldRenderers = new List<Renderer>();

        private Camera _originalFieldCamera;
        private String _originalFieldCameraName;
        private Boolean _originalFieldCameraEnabled;
        private GameObject _viewerCameraObject;
        private readonly List<Camera> _suspendedWorldCameras = new List<Camera>();
        private GameObject _lastFramedModel;
        private Int32 _frameDelay;
        private Boolean _enteredFromField;
        private UIManager.UIState _savedUiState;
        private Boolean _hasSavedUiState;
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

        // IMPORTANT:
        // This method must be called from Unity's main thread.
        public String Toggle()
        {
            return IsActive ? ReturnToGameRuntime() : EnterModelViewerRuntime();
        }

        private String EnterModelViewerRuntime()
        {
            try
            {
                // Keep our field history/checkpoint completely untouched.
                // No Memoria.ini edit, no launcher, no process restart.
                FF9StateSystem launchState = PersistenSingleton<FF9StateSystem>.Instance;
                _enteredFromField = launchState != null &&
                                    launchState.mode == 1 &&
                                    GameObject.Find("FieldMap Camera") != null;

                SaveViewerCameraState();

                if (_enteredFromField)
                    SuspendCurrentFieldRenderers();
                else
                    Log.Message("[Dev Console][PASS14_18] Model Viewer opened outside field mode; no field renderers to suspend.");

                if (!PrepareDedicatedModelViewerCamera())
                {
                    RestoreCurrentFieldRenderers();
                    return "Model Viewer failed: could not create a clean viewer camera.";
                }

                if (!SetRuntimeModelViewerFlag(true))
                {
                    RestoreDedicatedModelViewerCamera();
                    RestoreCurrentFieldRenderers();
                    return "Model Viewer failed: could not enable Memoria's runtime Model Viewer flag.";
                }

                SuspendExistingGameSpsRenderers();
                SuspendExistingGameUiWidgets();

                if (!ModelViewerScene.initialized)
                    ModelViewerScene.Init();

                if (!ModelViewerScene.initialized)
                {
                    RestoreExistingGameUiWidgets();
                    RestoreExistingGameSpsRenderers();
                    SetRuntimeModelViewerFlag(false);
                    RestoreDedicatedModelViewerCamera();
                    RestoreCurrentFieldRenderers();
                    return "Model Viewer failed: Memoria did not initialize the viewer.";
                }

                _lastFramedModel = null;
                _frameDelay = 0;
                Log.Message("[Dev Console][PASS14_18] Model Viewer enabled. Game simulation frozen; viewer owns update.");
                return "Model Viewer opened instantly. Game simulation frozen.";
            }
            catch (Exception ex)
            {
                RestoreSavedUiState();
                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                RestoreDedicatedModelViewerCamera();
                RestoreCurrentFieldRenderers();
                Log.Error("[Dev Console] Runtime Model Viewer launch failed: " + ex);
                return "Model Viewer launch failed: " + ex.Message;
            }
        }

        public void UpdateRuntime()
        {
            if (!IsActive || !ModelViewerScene.initialized)
                return;

            try
            {
                KeepExistingGameSpsHidden();
                SuppressAllNonViewerSps();

                // Dev Console disables HonoBehaviorSystem without changing UIManager.State. Drive ONLY the viewer here.
                ModelViewerScene.Update();

                // Memoria's Model Viewer lays its left/right panels out across a
                // fixed ~2000-unit logical width. On narrower UI viewports (for
                // example 1600x900), the outer edges are clipped. Reposition the
                // panels after Memoria updates them so they remain inside the
                // actual visible NGUI width without changing Memoria's own saved
                // panel offsets.

                Type viewerType = typeof(ModelViewerScene);
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo modelField = viewerType.GetField("currentModel", flags);
                GameObject model = modelField != null ? modelField.GetValue(null) as GameObject : null;

                if (model == null)
                    return;

                SuppressViewerSpsWhenNotSelected(model);

                if (model != _lastFramedModel)
                {
                    _lastFramedModel = model;
                    _frameDelay = 3; // let ModelFactory/animations/renderers settle
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
                Log.Warning("[Dev Console][PASS14_6] Auto-frame failed: " + ex.Message);
            }
        }

        private void SuspendExistingGameUiWidgets()
        {
            _suspendedGameUiWidgets.Clear();

            try
            {
                // Capture BEFORE ModelViewerScene.Init(), so this list contains only the
                // game's already-existing NGUI/atlas UI (dialog boxes, bubbles, labels, etc.).
                // The Model Viewer creates its own UI afterwards and therefore remains visible.
                UIWidget[] widgets = UnityEngine.Object.FindObjectsOfType<UIWidget>();

                for (Int32 i = 0; i < widgets.Length; i++)
                {
                    UIWidget widget = widgets[i];
                    if (widget == null || !widget.enabled)
                        continue;

                    _suspendedGameUiWidgets.Add(widget);
                    widget.enabled = false;
                }

                Log.Message("[Dev Console][PASS14_18] Suspended " +
                            _suspendedGameUiWidgets.Count +
                            " pre-existing NGUI widgets for Model Viewer isolation.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][PASS14_18] Could not isolate pre-existing game UI: " + ex.Message);
            }
        }

        private void RestoreExistingGameUiWidgets()
        {
            try
            {
                for (Int32 i = 0; i < _suspendedGameUiWidgets.Count; i++)
                {
                    UIWidget widget = _suspendedGameUiWidgets[i];
                    if (widget != null)
                        widget.enabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][PASS14_18] Could not restore pre-existing game UI: " + ex.Message);
            }
            finally
            {
                _suspendedGameUiWidgets.Clear();
            }
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
                    if (effect == null)
                        continue;

                    Renderer[] renderers = effect.GetComponentsInChildren<Renderer>(true);
                    for (Int32 j = 0; j < renderers.Length; j++)
                    {
                        Renderer renderer = renderers[j];
                        if (renderer == null || !renderer.enabled)
                            continue;

                        if (!_suspendedGameSpsRenderers.Contains(renderer))
                            _suspendedGameSpsRenderers.Add(renderer);
                        renderer.enabled = false;
                    }
                }
                Log.Message("[Dev Console][PASS14_18] Suspended " + _suspendedGameSpsRenderers.Count + " pre-existing field/world SPS renderers.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][PASS14_18] Could not isolate game SPS: " + ex.Message);
            }
        }

        private void KeepExistingGameSpsHidden()
        {
            for (Int32 i = 0; i < _suspendedGameSpsRenderers.Count; i++)
            {
                Renderer renderer = _suspendedGameSpsRenderers[i];
                if (renderer != null)
                    renderer.enabled = false;
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
                    if (effect == null || effect.gameObject == null)
                        continue;

                    // ModelViewerScene.Init creates this exact viewer-owned SPS object.
                    if (effect.gameObject.name == "ModelViewer_SPS")
                        continue;

                    Renderer[] renderers = effect.GetComponentsInChildren<Renderer>(true);
                    for (Int32 j = 0; j < renderers.Length; j++)
                    {
                        if (renderers[j] != null)
                            renderers[j].enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][PASS14_18] SPS quarantine failed: " + ex.Message);
            }
        }

        private void RestoreExistingGameSpsRenderers()
        {
            try
            {
                for (Int32 i = 0; i < _suspendedGameSpsRenderers.Count; i++)
                {
                    Renderer renderer = _suspendedGameSpsRenderers[i];
                    if (renderer != null)
                        renderer.enabled = true;
                }
            }
            finally
            {
                _suspendedGameSpsRenderers.Clear();
            }
        }

        private void RestoreSavedUiState()
        {
            if (!_hasSavedUiState)
                return;
            try
            {
                UIManager uiManager = PersistenSingleton<UIManager>.Instance;
                if (uiManager != null)
                    uiManager.State = _savedUiState;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][PASS14_18] Could not restore UI state: " + ex.Message);
            }
            finally
            {
                _hasSavedUiState = false;
            }
        }

        private static void SuppressViewerSpsWhenNotSelected(GameObject currentModel)
        {
            try
            {
                GameObject viewerSps = GameObject.Find("ModelViewer_SPS");
                if (viewerSps == null)
                    return;

                Boolean selected = currentModel == viewerSps;
                Renderer[] renderers = viewerSps.GetComponentsInChildren<Renderer>(true);

                for (Int32 i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer != null)
                        renderer.enabled = selected;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][PASS14_18] Could not suppress idle viewer SPS: " + ex.Message);
            }
        }

        private static void AutoFrameCurrentModel(Type viewerType, GameObject model, BindingFlags flags)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            Boolean haveBounds = false;
            Bounds bounds = new Bounds();

            for (Int32 i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = true;

                if (!haveBounds)
                {
                    bounds = renderer.bounds;
                    haveBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!haveBounds)
                return;

            FieldInfo wrapperField = viewerType.GetField("currentModelWrapper", flags);
            GameObject wrapper = wrapperField != null ? wrapperField.GetValue(null) as GameObject : null;

            if (wrapper == null)
            {
                wrapper = new GameObject("CurrentModelWrapper");
                wrapperField.SetValue(null, wrapper);
                model.transform.SetParent(wrapper.transform, true);
            }

            // First center the rendered bounds at the viewer origin.
            Vector3 delta = -bounds.center;
            wrapper.transform.position += delta;

            // Re-read bounds after centering and fit it into the perspective camera.
            haveBounds = false;
            for (Int32 i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!haveBounds)
                {
                    bounds = renderer.bounds;
                    haveBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            Camera camera = GameObject.Find("FieldMap Camera") != null
                ? GameObject.Find("FieldMap Camera").GetComponent<Camera>()
                : null;

            if (camera != null)
            {
                camera.transform.position = new Vector3(0f, 0f, -1000f);
                camera.transform.LookAt(Vector3.zero, Vector3.down);
                camera.orthographic = false;
                camera.fieldOfView = 40f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10000f;
                camera.cullingMask = -1;
            }

            Single maxHalf = Mathf.Max(bounds.extents.x, bounds.extents.y);
            if (maxHalf > 0.001f)
            {
                // At z=1000/FOV40 the visible vertical half-height is ~364.
                // Target ~260 to leave comfortable UI/rotation room.
                Single fit = Mathf.Clamp(260f / maxHalf, 0.02f, 20f);
                model.transform.localScale *= fit;

                FieldInfo scaleField = viewerType.GetField("scaleFactor", flags);
                if (scaleField != null)
                    scaleField.SetValue(null, model.transform.localScale);
            }

            // Recenter once more after scaling.
            haveBounds = false;
            for (Int32 i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (!haveBounds)
                {
                    bounds = renderer.bounds;
                    haveBounds = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            if (haveBounds)
                wrapper.transform.position += -bounds.center;

            FieldInfo posField = viewerType.GetField("model_Position", flags);
            if (posField != null)
                posField.SetValue(null, wrapper.transform.localPosition);

            Log.Message("[Dev Console][PASS14_18] Auto-framed " + model.name +
                        " center=" + bounds.center +
                        " size=" + bounds.size +
                        " scale=" + model.transform.localScale);
        }

        private String ReturnToGameRuntime()
        {
            try
            {
                // Stop ModelViewerScene.Update from being called before touching its objects.
                if (!SetRuntimeModelViewerFlag(false))
                    return ReturnToGameFallback("Could not disable runtime Model Viewer flag.");

                // The viewer has no public teardown method in this Memoria build.
                // Clean up the objects it owns, reset its initialized flag, then reload
                // the already-live field state. FF9StateSystem itself was never replaced.
                CleanupModelViewerObjects();
                ModelViewerScene.initialized = false;
                RestoreViewerCameraState();
                RestoreDedicatedModelViewerCamera();

                if (!_enteredFromField)
                {
                    RestoreExistingGameUiWidgets();
                    RestoreExistingGameSpsRenderers();
                    ForgetSuspendedFieldRenderers();
                    _enteredFromField = false;
                    Log.Message("[Dev Console][PASS14_18] Model Viewer closed; resumed frozen non-field scene.");
                    return "Model Viewer closed instantly.";
                }

                // The live field runtime is disposable after ModelViewerScene has touched it.
                // Do NOT snapshot on viewer entry and do NOT try to resume its renderers.
                // Roll back to FieldState's newest already-stable snapshot: the one captured
                // after the last genuine field change/stability window.
                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                ForgetSuspendedFieldRenderers();

                if (_fieldState == null)
                    return ReturnToGameFallback("Field State service is unavailable.");

                String restoreResult = _fieldState.RestoreLatestStableFieldSnapshot();
                if (String.IsNullOrEmpty(restoreResult) || !restoreResult.StartsWith("Restoring field "))
                    return ReturnToGameFallback("Latest stable field snapshot restore failed: " + restoreResult);

                _enteredFromField = false;

                Log.Message("[Dev Console][PASS14_18] Model Viewer closed; rolled back to latest stable field snapshot. " + restoreResult);
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

                _enteredFromField = false;

                if (_fieldState != null)
                    _fieldState.PersistHistoryForRestart();

                // Runtime flag is process-local. No INI setting was changed by this pass.
                HardResetService.Restart();
                return "Instant return unavailable; restarting FFIX safely...";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Model Viewer fallback restart failed: " + ex);
                return "Model Viewer return failed: " + ex.Message;
            }
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
                viewerCamera.cullingMask = -1;
                viewerCamera.orthographic = false;
                viewerCamera.fieldOfView = 40f;
                viewerCamera.nearClipPlane = 0.1f;
                viewerCamera.farClipPlane = 10000f;
                viewerCamera.depth = _originalFieldCamera != null ? _originalFieldCamera.depth : 0f;
                viewerCamera.rect = new Rect(0f, 0f, 1f, 1f);
                viewerCamera.enabled = true;

                _viewerCameraObject.transform.position = new Vector3(0f, 0f, -1000f);
                _viewerCameraObject.transform.LookAt(Vector3.zero, Vector3.down);

                if (_originalFieldCamera == null)
                    Log.Message("[Dev Console][PASS14_18] No live FieldMap Camera existed; created standalone Model Viewer camera.");
                else
                    Log.Message("[Dev Console][PASS14_18] Live field camera suspended; dedicated Model Viewer camera created.");

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to create dedicated Model Viewer camera: " + ex);
                RestoreDedicatedModelViewerCamera();
                return false;
            }
        }

        private void SuspendCompetingWorldCameras(Camera viewerCamera)
        {
            _suspendedWorldCameras.Clear();

            try
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();

                for (Int32 i = 0; i < cameras.Length; i++)
                {
                    Camera camera = cameras[i];

                    if (camera == null || camera == viewerCamera || camera == _originalFieldCamera || !camera.enabled)
                        continue;

                    String cameraName = camera.gameObject != null ? camera.gameObject.name : String.Empty;

                    // Keep NGUI/UI cameras alive so the native Model Viewer labels and menus
                    // continue to render. Everything else is a competing world camera that
                    // can clear/draw over the dedicated viewer camera later in the frame.
                    Boolean isUiCamera =
                        cameraName.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cameraName.IndexOf("NGUI", StringComparison.OrdinalIgnoreCase) >= 0;

                    Log.Message("[Dev Console] Camera before Model Viewer: " +
                                cameraName +
                                " depth=" + camera.depth +
                                " clear=" + camera.clearFlags +
                                " enabled=" + camera.enabled +
                                " ui=" + isUiCamera);

                    if (isUiCamera)
                        continue;

                    _suspendedWorldCameras.Add(camera);
                    camera.enabled = false;
                }

                Log.Message("[Dev Console] Suspended " +
                            _suspendedWorldCameras.Count +
                            " competing world cameras for Model Viewer.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not isolate competing cameras: " + ex.Message);
            }
        }

        private void RestoreCompetingWorldCameras()
        {
            if (_suspendedWorldCameras.Count == 0)
                return;

            try
            {
                for (Int32 i = 0; i < _suspendedWorldCameras.Count; i++)
                {
                    Camera camera = _suspendedWorldCameras[i];
                    if (camera != null)
                        camera.enabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not restore all competing cameras: " + ex.Message);
            }
            finally
            {
                _suspendedWorldCameras.Clear();
            }
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
                        originalCameraObject.name = String.IsNullOrEmpty(_originalFieldCameraName)
                            ? "FieldMap Camera"
                            : _originalFieldCameraName;
                    }

                    _originalFieldCamera.enabled = _originalFieldCameraEnabled;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not fully restore field camera: " + ex.Message);
            }
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
                // The live field scene is rooted under "FieldMap Root".
                // Only suspend renderers inside that hierarchy so the Model Viewer
                // can continue using the shared UI/camera/render infrastructure.
                GameObject fieldRoot = GameObject.Find("FieldMap Root");

                if (fieldRoot == null)
                {
                    Log.Warning("[Dev Console] FieldMap Root not found; viewer isolation skipped.");
                    return;
                }

                Renderer[] renderers = fieldRoot.GetComponentsInChildren<Renderer>(true);

                for (Int32 i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];

                    if (renderer == null || !renderer.enabled)
                        continue;

                    _suspendedFieldRenderers.Add(renderer);
                    renderer.enabled = false;
                }

                Log.Message("[Dev Console] Suspended " +
                            _suspendedFieldRenderers.Count +
                            " FieldMap Root renderers for Model Viewer isolation.");
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not isolate FieldMap Root renderers: " + ex.Message);
            }
        }

        private void ForgetSuspendedFieldRenderers()
        {
            // ReplaceLoadMap rebuilds the field. These references belong to the old field and
            // must not be re-enabled after the rebuild.
            _suspendedFieldRenderers.Clear();
        }

        private void RestoreCurrentFieldRenderers()
        {
            if (_suspendedFieldRenderers.Count == 0)
                return;

            try
            {
                for (Int32 i = 0; i < _suspendedFieldRenderers.Count; i++)
                {
                    Renderer renderer = _suspendedFieldRenderers[i];

                    if (renderer != null)
                        renderer.enabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not restore all FieldMap Root renderers: " + ex.Message);
            }
            finally
            {
                _suspendedFieldRenderers.Clear();
            }
        }

        private static Boolean SetRuntimeModelViewerFlag(Boolean enabled)
        {
            try
            {
                Type configurationType = typeof(Memoria.Configuration);

                FieldInfo instanceField = configurationType.GetField(
                    "Instance",
                    BindingFlags.Static | BindingFlags.NonPublic);

                FieldInfo debugField = configurationType.GetField(
                    "_debug",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (instanceField == null || debugField == null)
                    return false;

                System.Object instance = instanceField.GetValue(null);
                if (instance == null)
                    return false;

                System.Object debugSection = debugField.GetValue(instance);
                if (debugSection == null)
                    return false;

                FieldInfo modelViewerField = debugSection.GetType().GetField(
                    "StartModelViewer",
                    BindingFlags.Instance | BindingFlags.Public);

                if (modelViewerField == null)
                    return false;

                System.Object iniValue = modelViewerField.GetValue(debugSection);
                if (iniValue == null)
                    return false;

                FieldInfo valueField = iniValue.GetType().GetField(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public);

                if (valueField == null)
                    return false;

                valueField.SetValue(iniValue, enabled);
                return Memoria.Configuration.Debug.StartModelViewer == enabled;
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Runtime Model Viewer flag reflection failed: " + ex);
                return false;
            }
        }

        private void SaveViewerCameraState()
        {
            try
            {
                MethodInfo getCamera = typeof(ModelViewerScene).GetMethod(
                    "GetCamera",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Camera camera = getCamera != null
                    ? getCamera.Invoke(null, null) as Camera
                    : Camera.main;

                if (camera == null)
                    return;

                _savedCameraColor = camera.backgroundColor;
                _hasSavedCameraColor = true;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not save Model Viewer camera state: " + ex.Message);
            }
        }

        private void RestoreViewerCameraState()
        {
            if (!_hasSavedCameraColor)
                return;

            try
            {
                MethodInfo getCamera = typeof(ModelViewerScene).GetMethod(
                    "GetCamera",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Camera camera = getCamera != null
                    ? getCamera.Invoke(null, null) as Camera
                    : Camera.main;

                if (camera != null)
                    camera.backgroundColor = _savedCameraColor;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not restore Model Viewer camera state: " + ex.Message);
            }
            finally
            {
                _hasSavedCameraColor = false;
            }
        }

        private static void CleanupModelViewerObjects()
        {
            Type viewerType = typeof(ModelViewerScene);

            // Direct Unity objects owned by the viewer.
            DestroyFieldUnityObject(viewerType, "currentModel");
            DestroyFieldUnityObject(viewerType, "currentModelWrapper");
            DestroyFieldUnityObject(viewerType, "currentWeaponModel");
            DestroyFieldUnityObject(viewerType, "currentFloorModel");
            DestroyFieldUnityObject(viewerType, "InsertTextGUI");
            DestroyFieldUnityObject(viewerType, "backgroundGo");
            DestroyFieldUnityObject(viewerType, "labelGo");

            // SPS component -> destroy its GameObject.
            DestroyComponentFieldGameObject(viewerType, "spsEffect");

            // ControlPanel is not a UnityEngine.Object itself; BasePanel is.
            DestroyControlPanel(viewerType, "infoPanel");
            DestroyControlPanel(viewerType, "controlPanel");
            DestroyControlPanel(viewerType, "extraInfoPanel");

            // Bone helper objects.
            DestroyUnityObjectList(viewerType, "boneModels");
            DestroyUnityObjectList(viewerType, "boneConnectModels");
            CloseDialogList(viewerType, "boneDialogs");

            // Named viewer-only scene helpers.
            DestroyNamedGameObject("ModelViewer_SPS");
            DestroyNamedGameObject("ModelViewerWMLight0");
            DestroyNamedGameObject("ModelViewerWMLight1");
            DestroyNamedGameObject("ModelViewerWMLight2");
        }

        private static System.Object GetStaticFieldValue(Type type, String fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            return field == null ? null : field.GetValue(null);
        }

        private static void SetStaticFieldValue(Type type, String fieldName, System.Object value)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (field != null && !field.IsInitOnly)
                field.SetValue(null, value);
        }

        private static void DestroyFieldUnityObject(Type viewerType, String fieldName)
        {
            System.Object value = GetStaticFieldValue(viewerType, fieldName);
            UnityEngine.Object unityObject = value as UnityEngine.Object;

            if (unityObject != null)
                UnityEngine.Object.Destroy(unityObject);

            SetStaticFieldValue(viewerType, fieldName, null);
        }

        private static void DestroyComponentFieldGameObject(Type viewerType, String fieldName)
        {
            System.Object value = GetStaticFieldValue(viewerType, fieldName);
            Component component = value as Component;

            if (component != null && component.gameObject != null)
                UnityEngine.Object.Destroy(component.gameObject);

            SetStaticFieldValue(viewerType, fieldName, null);
        }

        private static void DestroyControlPanel(Type viewerType, String fieldName)
        {
            System.Object panel = GetStaticFieldValue(viewerType, fieldName);
            if (panel == null)
                return;

            try
            {
                PropertyInfo basePanelProperty = panel.GetType().GetProperty(
                    "BasePanel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                System.Object basePanel = basePanelProperty == null
                    ? null
                    : basePanelProperty.GetValue(panel, null);

                Component component = basePanel as Component;
                if (component != null && component.gameObject != null)
                    UnityEngine.Object.Destroy(component.gameObject);
                else
                {
                    UnityEngine.Object unityObject = basePanel as UnityEngine.Object;
                    if (unityObject != null)
                        UnityEngine.Object.Destroy(unityObject);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Model Viewer panel cleanup failed: " + ex.Message);
            }

            SetStaticFieldValue(viewerType, fieldName, null);
        }

        private static void DestroyUnityObjectList(Type viewerType, String fieldName)
        {
            IEnumerable list = GetStaticFieldValue(viewerType, fieldName) as IEnumerable;
            if (list == null)
                return;

            foreach (System.Object item in list)
            {
                UnityEngine.Object unityObject = item as UnityEngine.Object;
                if (unityObject != null)
                    UnityEngine.Object.Destroy(unityObject);
            }

            System.Object raw = GetStaticFieldValue(viewerType, fieldName);
            if (raw != null)
            {
                MethodInfo clear = raw.GetType().GetMethod("Clear", Type.EmptyTypes);
                if (clear != null)
                    clear.Invoke(raw, null);
            }
        }

        private static void CloseDialogList(Type viewerType, String fieldName)
        {
            IEnumerable list = GetStaticFieldValue(viewerType, fieldName) as IEnumerable;
            if (list == null)
                return;

            foreach (System.Object item in list)
            {
                if (item == null)
                    continue;

                try
                {
                    MethodInfo forceClose = item.GetType().GetMethod(
                        "ForceClose",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (forceClose != null)
                        forceClose.Invoke(item, null);
                }
                catch
                {
                }
            }

            System.Object raw = GetStaticFieldValue(viewerType, fieldName);
            if (raw != null)
            {
                MethodInfo clear = raw.GetType().GetMethod("Clear", Type.EmptyTypes);
                if (clear != null)
                    clear.Invoke(raw, null);
            }
        }

        private static void DestroyNamedGameObject(String name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                UnityEngine.Object.Destroy(go);
        }
    }
}
