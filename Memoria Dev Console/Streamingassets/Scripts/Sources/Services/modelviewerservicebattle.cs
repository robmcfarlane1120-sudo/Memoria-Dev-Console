using Assets.Scripts.Common;
using Memoria.Assets;
using Memoria.Prime;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private readonly List<Camera> _suspendedWorldCameras = new List<Camera>();
        private readonly List<Behaviour> _suspendedCompetingBehaviours = new List<Behaviour>();
        private Boolean _enteredFromBattle;
        private readonly List<Renderer> _suspendedBattleRenderers = new List<Renderer>();
        private readonly List<Boolean> _suspendedBattleRendererStates = new List<Boolean>();
        private readonly List<GameObject> _suspendedBattleUiObjects = new List<GameObject>();
        private readonly List<Boolean> _suspendedBattleUiObjectStates = new List<Boolean>();
        private Boolean _battleViewerPreCullHooked;
        private const Int32 BattleViewerModelLayer = 31;

        private void KeepExistingGameUiWidgetsHidden()
        {
            // Every widget in this list existed before ModelViewerScene.Init(), so it
            // belongs to the battle HUD. Do not exempt "(Clone)" hierarchies: most of
            // FFIX's normal battle interface is cloned too.
            for (Int32 i = 0; i < _suspendedGameUiWidgets.Count; i++)
            {
                UIWidget widget = _suspendedGameUiWidgets[i];
                if (widget == null || widget.gameObject == null)
                    continue;

                widget.enabled = false;
                SuspendBattleUiObject(widget.gameObject);
            }

            IsolateBattleViewerModelLayer();
        }

        private void SuspendCompetingWorldCameras(Camera viewerCamera)
        {
            _suspendedWorldCameras.Clear();
            _suspendedCompetingBehaviours.Clear();
            try
            {
                Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                for (Int32 i = 0; i < cameras.Length; i++)
                {
                    Camera camera = cameras[i];
                    if (camera == null || camera == viewerCamera || camera == _originalFieldCamera || !camera.enabled)
                    {
                        continue;
                    }

                    String name = camera.gameObject != null ? camera.gameObject.name : String.Empty;
                    Boolean hasUiComp = false;
                    try { hasUiComp = camera.GetComponent<UICamera>() != null; }
                    catch { }

                    Boolean isUi = hasUiComp ||
                        name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("NGUI", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isUi) continue;

                    _suspendedWorldCameras.Add(camera);
                    camera.enabled = false;

                    // Freeze is already owned by the dev-console controller. Leave the
                    // battle camera's controller/aspect/SFX behaviours untouched so
                    // they retain their internal state for the first frame after exit.
                }

                if (!_battleViewerPreCullHooked)
                {
                    Camera.onPreCull += OnBattleViewerCameraPreCull;
                    _battleViewerPreCullHooked = true;
                }
            }
            catch (Exception ex) 
            { 
                Log.Warning("Camera isolate failure: " + ex.Message); 
            }
        }

        private void RestoreCompetingWorldCameras()
        {
            if (_battleViewerPreCullHooked)
            {
                Camera.onPreCull -= OnBattleViewerCameraPreCull;
                _battleViewerPreCullHooked = false;
            }

            // Object.Destroy is deferred until the end of the Unity frame. Disable the
            // viewer camera now, before restoring the battle camera, so it cannot clear
            // the framebuffer over the resumed battle for one final frame.
            if (_viewerCameraObject != null)
            {
                Camera viewerCamera = _viewerCameraObject.GetComponent<Camera>();
                if (viewerCamera != null)
                    viewerCamera.enabled = false;
            }

            try
            {
                for (Int32 i = 0; i < _suspendedCompetingBehaviours.Count; i++)
                {
                    if (_suspendedCompetingBehaviours[i] != null) _suspendedCompetingBehaviours[i].enabled = true;
                }
            }
            catch (Exception ex) 
            { 
                Log.Warning("Script restore error: " + ex.Message); 
            }
            finally { _suspendedCompetingBehaviours.Clear(); }

            try
            {
                for (Int32 i = 0; i < _suspendedWorldCameras.Count; i++)
                {
                    if (_suspendedWorldCameras[i] != null) _suspendedWorldCameras[i].enabled = true;
                }
            }
            finally { _suspendedWorldCameras.Clear(); }

            RestoreBattleUiObjects();
        }

        private void OnBattleViewerCameraPreCull(Camera renderingCamera)
        {
            if (!_enteredFromBattle || !IsActive)
                return;

            // ModelViewerService.AutoFrameCurrentModel changes the camera mask when a
            // model is cycled. Reassert the isolated mask at the last possible moment.
            if (_viewerCameraObject != null)
            {
                Camera viewerCamera = _viewerCameraObject.GetComponent<Camera>();
                if (viewerCamera != null)
                    viewerCamera.cullingMask = 1 << BattleViewerModelLayer;
            }

            for (Int32 i = 0; i < _suspendedBattleRenderers.Count; i++)
            {
                Renderer renderer = _suspendedBattleRenderers[i];
                if (renderer != null)
                    renderer.enabled = false;
            }

            for (Int32 i = 0; i < _suspendedBattleUiObjects.Count; i++)
            {
                GameObject uiObject = _suspendedBattleUiObjects[i];
                if (uiObject != null && uiObject.activeSelf)
                    uiObject.SetActive(false);
            }
        }

        private void SuspendBattleUiObject(GameObject uiObject)
        {
            if (uiObject == null)
                return;

            Int32 existingIndex = _suspendedBattleUiObjects.IndexOf(uiObject);
            if (existingIndex < 0)
            {
                _suspendedBattleUiObjects.Add(uiObject);
                _suspendedBattleUiObjectStates.Add(uiObject.activeSelf);
            }

            if (uiObject.activeSelf)
                uiObject.SetActive(false);
        }

        private void RestoreBattleUiObjects()
        {
            try
            {
                for (Int32 i = 0; i < _suspendedBattleUiObjects.Count; i++)
                {
                    GameObject uiObject = _suspendedBattleUiObjects[i];
                    if (uiObject == null)
                        continue;

                    Boolean wasActive = i < _suspendedBattleUiObjectStates.Count &&
                                        _suspendedBattleUiObjectStates[i];
                    uiObject.SetActive(wasActive);
                }
            }
            finally
            {
                _suspendedBattleUiObjects.Clear();
                _suspendedBattleUiObjectStates.Clear();
            }
        }

        private void SuspendCurrentBattleRenderers()
        {
            _suspendedBattleRenderers.Clear();
            _suspendedBattleRendererStates.Clear();
            try
            {
                GameObject battleRoot = GameObject.Find("BattleMap Root");
                if (battleRoot == null)
                {
                    Log.Warning("[Dev Console][Battle Viewer] BattleMap Root was not found.");
                    return;
                }

                // Capture the complete battle hierarchy, including renderers that happen
                // to be disabled on this exact frame. Viewer objects do not exist yet and
                // therefore can never enter this list.
                Renderer[] renderers = battleRoot.GetComponentsInChildren<Renderer>(true);
                for (Int32 i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                        continue;

                    _suspendedBattleRenderers.Add(renderer);
                    _suspendedBattleRendererStates.Add(renderer.enabled);
                    renderer.enabled = false;
                }
            }
            catch (Exception ex) 
            { 
                Log.Warning("Renderer isolate failure: " + ex.Message); 
            }
        }

        private void RestoreCurrentBattleRenderers()
        {
            try
            {
                for (Int32 i = 0; i < _suspendedBattleRenderers.Count; i++)
                {
                    if (_suspendedBattleRenderers[i] != null)
                    {
                        Boolean wasEnabled = i < _suspendedBattleRendererStates.Count &&
                                             _suspendedBattleRendererStates[i];
                        _suspendedBattleRenderers[i].enabled = wasEnabled;
                    }
                }
            }
            finally
            {
                _suspendedBattleRenderers.Clear();
                _suspendedBattleRendererStates.Clear();

                // ModelViewerScene.Init() switches the engine to MenuFPS/MenuTPS.
                // Because MDC resumes this battle without changing scenes, Memoria's
                // normal scene-loading timing reset never runs. Restore the timing for
                // the still-active battle scene explicitly.
                try
                {
                    SceneDirector.SetTargetFrameRateForCurrentScene();
                }
                catch (Exception ex)
                {
                    Log.Warning("[Dev Console][Battle Viewer] Could not restore battle FPS/TPS: " + ex.Message);
                }
            }
        }

        private void KeepBattleRenderersHidden()
        {
            for (Int32 i = 0; i < _suspendedBattleRenderers.Count; i++)
            {
                Renderer renderer = _suspendedBattleRenderers[i];
                if (renderer != null)
                    renderer.enabled = false;
            }

            IsolateBattleViewerModelLayer();
        }

        private void IsolateBattleViewerModelLayer()
        {
            if (!_enteredFromBattle)
                return;

            try
            {
                Type viewerType = typeof(ModelViewerScene);
                FieldInfo modelField = viewerType.GetField("currentModel",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                GameObject model = modelField != null ? modelField.GetValue(null) as GameObject : null;
                if (model != null)
                    SetLayerRecursively(model.transform, BattleViewerModelLayer);

                if (_viewerCameraObject != null)
                {
                    Camera viewerCamera = _viewerCameraObject.GetComponent<Camera>();
                    if (viewerCamera != null)
                        viewerCamera.cullingMask = 1 << BattleViewerModelLayer;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][Battle Viewer] Could not isolate viewer model layer: " + ex.Message);
            }
        }

        private static void SetLayerRecursively(Transform root, Int32 layer)
        {
            if (root == null)
                return;

            root.gameObject.layer = layer;
            for (Int32 i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private static void FixClonedModelViewerLabelClipping()
        {
            // Intentionally left alone. The Model Viewer controls use the established
            // NGUI panel configuration. Disabling clipping on their shared UI Root also
            // changes the battle HUD and produces stretched/overlapping UI geometry.
        }

    }
}
