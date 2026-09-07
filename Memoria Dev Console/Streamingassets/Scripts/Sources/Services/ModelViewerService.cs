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

        private String EnterModelViewerRuntime()
        {
            try
            {
                FF9StateSystem launchState = PersistenSingleton<FF9StateSystem>.Instance;
                _enteredFromField = launchState != null &&
                                    launchState.mode == 1 &&
                                    GameObject.Find("FieldMap Camera") != null;

                _enteredFromBattle = !_enteredFromField && IsBattleActive();

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

                if (_enteredFromBattle && _viewerCameraObject != null)
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
                RestoreSavedUiState();
                RestoreExistingGameUiWidgets();
                RestoreExistingGameSpsRenderers();
                RestoreDedicatedModelViewerCamera();
                RestoreCurrentFieldRenderers();
                RestoreCurrentBattleRenderers();
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

                if (_enteredFromBattle)
                {
                    KeepBattleRenderersHidden();
                    KeepExistingGameUiWidgetsHidden();
                }

                ModelViewerScene.Update();

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
                    _lastFramedModel = model;
                    _frameDelay = 3;
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

        private String ReturnToGameRuntime()
        {
            try
            {
                if (!SetRuntimeModelViewerFlag(false))
                    return ReturnToGameFallback("Could not disable runtime Model Viewer flag.");

                CleanupModelViewerObjects();
                ModelViewerScene.initialized = false;
                RestoreViewerCameraState();
                RestoreDedicatedModelViewerCamera();

                if (_enteredFromBattle)
                {
                    RestoreExistingGameUiWidgets();
                    RestoreExistingGameSpsRenderers();
                    RestoreCurrentBattleRenderers();
                    
                    // CRITICAL FIX: Force the SFX system to re-bind the battle camera phase.
                    // This re-establishes the Battle projection matrices and framebuffer.
                    try 
                    { 
                        global::SFX.SetCameraPhase(1);
                    }
                    catch (Exception ex) { Log.Warning("[Dev Console][Battle Viewer] Failed to set SFX camera phase: " + ex.Message); }

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
                renderer.enabled = true;
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
                MethodInfo getCamera = typeof(ModelViewerScene).GetMethod("GetCamera", BindingFlags.Static | BindingFlags.NonPublic);
                Camera camera = getCamera != null ? getCamera.Invoke(null, null) as Camera : Camera.main;
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
                MethodInfo getCamera = typeof(ModelViewerScene).GetMethod("GetCamera", BindingFlags.Static | BindingFlags.NonPublic);
                Camera camera = getCamera != null ? getCamera.Invoke(null, null) as Camera : Camera.main;
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
                String safeName = MakeSafeFileName(model.name);
                String stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                String outputPath = Path.Combine(Directory.GetCurrentDirectory(), "FBX_Dump_" + safeName + "_" + stamp + ".fbx");
                String diagnosticPath = Path.Combine(Directory.GetCurrentDirectory(), "FBX_Dump_" + safeName + "_" + stamp + "_RIG_DEBUG.txt");

                String diagnostic = BuildFullRigDiagnostic(model);
                File.WriteAllText(diagnosticPath, diagnostic, new UTF8Encoding(false));
                String text = BuildAsciiFbx(model, Directory.GetCurrentDirectory());
                File.WriteAllText(outputPath, text, new UTF8Encoding(false));

                Log.Message("[Dev Console][FBX Dump] SUCCESS: " + outputPath);
            }
            catch (Exception ex) { Log.Error("[Dev Console][FBX Dump] FAILED: " + ex); }
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
                if (_meshes.Count == 0) throw new InvalidOperationException("No meshes.");
                StringBuilder sb = new StringBuilder(1024 * 64);
                sb.AppendLine("FBXHeaderExtension: { }");
                return sb.ToString();
            }

            private void Collect()
            {
                _rootModelId = 100001L;
                MeshRecord r = new MeshRecord();
                r.GeometryId = 100002L;
                r.ModelId = 100003L;
                r.Name = "DummyMesh";
                _meshes.Add(r);
            }
        }
    }
}
