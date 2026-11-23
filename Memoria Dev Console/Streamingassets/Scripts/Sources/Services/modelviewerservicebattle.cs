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
        private Transform _viewerDialogHost;
        private readonly List<Transform> _viewerBoneDialogRoots = new List<Transform>();
        private Boolean _viewerPreCullHooked;
        private const Int32 ViewerModelLayer = 31;

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

                if (IsViewerBoneDialogUi(widget.gameObject))
                {
                    widget.enabled = true;
                    continue;
                }
                widget.enabled = false;
                // Never deactivate the button GameObject. ButtonGroupState.OnDisable
                // unregisters it and removes its pointer/navigation state. Disabling
                // only UIWidget removes its graphics from the panel without touching
                // the battle's command/target selection or its other components.
            }

            IsolateViewerModelLayer();
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

                if (!_viewerPreCullHooked)
                {
                    Camera.onPreCull += OnViewerCameraPreCull;
                    _viewerPreCullHooked = true;
                }
            }
            catch (Exception ex) 
            { 
                ViewerDiagnostics.Warning("Camera isolate failure: " + ex.Message); 
            }
        }

        private void RestoreCompetingWorldCameras()
        {
            if (_viewerPreCullHooked)
            {
                Camera.onPreCull -= OnViewerCameraPreCull;
                _viewerPreCullHooked = false;
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
                ViewerDiagnostics.Warning("Script restore error: " + ex.Message); 
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
            _viewerBoneDialogRoots.Clear();
            _viewerDialogHost = null;
        }

        private void OnViewerCameraPreCull(Camera renderingCamera)
        {
            if (!IsActive)
                return;

            if (_viewerCameraObject != null && renderingCamera == _viewerCameraObject.GetComponent<Camera>())
            {
                try { SyncViewerBoneGeometry(renderingCamera); }
                catch (Exception ex) { ViewerDiagnostics.Warning("[Dev Console][Model Viewer] Bone overlay update failed: " + ex.Message); }
            }
            IsolateViewerModelLayer();

            // ModelViewerService.AutoFrameCurrentModel changes the camera mask when a
            // model is cycled. Reassert the isolated mask at the last possible moment.
            if (_viewerCameraObject != null)
            {
                Camera viewerCamera = _viewerCameraObject.GetComponent<Camera>();
                if (viewerCamera != null)
                    viewerCamera.cullingMask = 1 << ViewerModelLayer;
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
                if (uiObject != null && uiObject.activeSelf && !IsViewerBoneDialogUi(uiObject))
                    uiObject.SetActive(false);
            }
        }

        private Boolean IsViewerBoneDialogUi(GameObject uiObject)
        {
            if (!_enteredFromBattle || uiObject == null) return false;
            Transform candidate = uiObject.transform;
            if (_viewerDialogHost == null)
            {
                DialogManager manager = PersistenSingleton<UIManager>.Instance.Dialogs;
                if (manager != null) _viewerDialogHost = manager.transform;
            }
            // Keep the shared host alive before AttachDialog/Show runs. Existing game
            // dialogs remain hidden individually; only viewer-owned children are exempt.
            if (_viewerDialogHost != null && _viewerDialogHost.IsChildOf(candidate)) return true;
            for (Int32 i = 0; i < _viewerBoneDialogRoots.Count; i++)
            {
                Transform dialogRoot = _viewerBoneDialogRoots[i];
                if (dialogRoot != null && (candidate.IsChildOf(dialogRoot) || dialogRoot.IsChildOf(candidate)))
                    return true;
            }
            return false;
        }

        private void RefreshViewerBoneDialogVisibility()
        {
            _viewerBoneDialogRoots.Clear();
            if (!_enteredFromBattle) return;
            System.Collections.IEnumerable dialogs = GetStaticFieldValue(typeof(ModelViewerScene), "boneDialogs") as System.Collections.IEnumerable;
            if (dialogs == null) return;
            foreach (System.Object item in dialogs)
            {
                Dialog dialog = item as Dialog;
                if (dialog != null) _viewerBoneDialogRoots.Add(dialog.transform);
            }
            // DialogManager pools/reuses these objects. An object that belonged to the
            // old game UI snapshot can now be a viewer label or its shared parent.
            for (Int32 i = 0; i < _suspendedGameUiWidgets.Count; i++)
            {
                UIWidget widget = _suspendedGameUiWidgets[i];
                if (widget != null && IsViewerBoneDialogUi(widget.gameObject)) widget.enabled = true;
            }
            for (Int32 i = 0; i < _suspendedBattleUiObjects.Count; i++)
            {
                GameObject uiObject = _suspendedBattleUiObjects[i];
                if (uiObject != null && !uiObject.activeSelf && IsViewerBoneDialogUi(uiObject))
                    uiObject.SetActive(true);
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
            SuspendBattleSimulation();
            _suspendedBattleRenderers.Clear();
            _suspendedBattleRendererStates.Clear();
            try
            {
                GameObject battleRoot = GameObject.Find("BattleMap Root");
                if (battleRoot == null)
                {
                    ViewerDiagnostics.Warning("[Dev Console][Battle Viewer] BattleMap Root was not found.");
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
                ViewerDiagnostics.Warning("Renderer isolate failure: " + ex.Message); 
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

                // UI reactivation and viewer cleanup can change the battle's menu
                // and pause gates independently of renderer/Hono enabled states.
                try { RestoreBattleControlState(); }
                finally { RestoreBattleSimulation(); }

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
                    ViewerDiagnostics.Warning("[Dev Console][Battle Viewer] Could not restore battle FPS/TPS: " + ex.Message);
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

            IsolateViewerModelLayer();
        }

        private static void SyncViewerBoneGeometry(Camera camera)
        {
            // Animation and BoneOffsetApplier run after Update. Read final world-space
            // bone transforms immediately before drawing the dedicated viewer camera.
            Type viewerType = typeof(ModelViewerScene);
            BoneHierarchyNode bones = GetStaticFieldValue(viewerType, "currentModelBones") as BoneHierarchyNode;
            System.Collections.IList markers = GetStaticFieldValue(viewerType, "boneModels") as System.Collections.IList;
            System.Collections.IList connections = GetStaticFieldValue(viewerType, "boneConnectModels") as System.Collections.IList;
            if (bones == null) return;
            Int32 markerIndex = 0;
            Int32 connectionIndex = 0;
            foreach (BoneHierarchyNode bone in bones)
            {
                if (markers != null && markerIndex < markers.Count)
                {
                    GameObject marker = markers[markerIndex++] as GameObject;
                    if (marker != null)
                    {
                        marker.transform.position = bone.Position;
                        marker.transform.rotation = camera.transform.rotation;
                        PrepareViewerBoneMesh(marker);
                    }
                }
                if (bone.Parent == null || connections == null || connectionIndex >= connections.Count) continue;
                GameObject connection = connections[connectionIndex++] as GameObject;
                if (connection == null) continue;
                Mesh mesh = PrepareViewerBoneMesh(connection);
                if (mesh == null) continue;
                Vector3 side = Vector3.Cross(bone.Position - bone.Parent.Position, camera.transform.forward).normalized;
                if (side.sqrMagnitude < 0.001f) side = camera.transform.right;
                Single width = Mathf.Max(0.5f, bone.Bone.lossyScale.magnitude);
                mesh.vertices = new Vector3[] {
                    connection.transform.InverseTransformPoint(bone.Parent.Position + side * width),
                    connection.transform.InverseTransformPoint(bone.Parent.Position - side * width),
                    connection.transform.InverseTransformPoint(bone.Position + side * width),
                    connection.transform.InverseTransformPoint(bone.Position - side * width)
                };
                mesh.RecalculateBounds();
            }
        }

        private static Mesh PrepareViewerBoneMesh(GameObject viewerObject)
        {
            SkinnedMeshRenderer source = viewerObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (source == null || source.sharedMesh == null) return null;
            // These procedural meshes have no bone weights or bind poses. Render
            // them as ordinary meshes rather than skinning them through an accessory.
            MeshFilter filter = viewerObject.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = viewerObject.AddComponent<MeshFilter>();
                filter.sharedMesh = source.sharedMesh;
                Int32[] triangles = filter.sharedMesh.triangles;
                Int32[] twoSided = new Int32[triangles.Length * 2];
                Array.Copy(triangles, twoSided, triangles.Length);
                for (Int32 i = 0; i < triangles.Length; i += 3)
                {
                    twoSided[triangles.Length + i] = triangles[i + 2];
                    twoSided[triangles.Length + i + 1] = triangles[i + 1];
                    twoSided[triangles.Length + i + 2] = triangles[i];
                }
                filter.sharedMesh.triangles = twoSided;
                MeshRenderer renderer = viewerObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = source.material;
                renderer.sharedMaterial.mainTexture = Texture2D.whiteTexture;
                renderer.sharedMaterial.color = Color.yellow;
            }
            source.enabled = false;
            filter.sharedMesh.RecalculateBounds();
            return filter.sharedMesh;
        }
        private void IsolateViewerModelLayer()
        {
            if (!IsActive)
                return;

            try
            {
                Type viewerType = typeof(ModelViewerScene);
                FieldInfo modelField = viewerType.GetField("currentModel",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                GameObject model = modelField != null ? modelField.GetValue(null) as GameObject : null;
                if (model != null)
                    SetLayerRecursively(model.transform, ViewerModelLayer);

                // Bones, connections, weapons and the floor are independent objects.
                // Admit the complete viewer scene, never the underlying game scene.
                foreach (String fieldName in new String[] { "currentWeaponModel", "currentFloorModel" })
                {
                    GameObject viewerObject = GetStaticFieldValue(viewerType, fieldName) as GameObject;
                    if (viewerObject != null) SetLayerRecursively(viewerObject.transform, ViewerModelLayer);
                }
                foreach (String fieldName in new String[] { "boneModels", "boneConnectModels" })
                {
                    System.Collections.IEnumerable objects = GetStaticFieldValue(viewerType, fieldName) as System.Collections.IEnumerable;
                    if (objects == null) continue;
                    foreach (System.Object item in objects)
                    {
                        GameObject viewerObject = item as GameObject;
                        if (viewerObject != null) SetLayerRecursively(viewerObject.transform, ViewerModelLayer);
                    }
                }

                if (_viewerCameraObject != null)
                {
                    Camera viewerCamera = _viewerCameraObject.GetComponent<Camera>();
                    if (viewerCamera != null)
                        viewerCamera.cullingMask = 1 << ViewerModelLayer;
                }
            }
            catch (Exception ex)
            {
                ViewerDiagnostics.Warning("[Dev Console][Battle Viewer] Could not isolate viewer model layer: " + ex.Message);
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
