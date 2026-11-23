using Assets.Scripts.Common;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO;
using FF9;
using Memoria.Data;
using UnityEngine;

namespace Memoria.BattleSceneLayouts
{
    // Shared by MDC and the exported standalone mod sources. No editor dependency.
    public static class BattleLayoutRuntime
    {
        private static Boolean hooksInstalled;
        internal static Boolean RestoresAfterNativeEffects;
        private static Action formationInitialized;
        internal static BattleLayoutCameraHook ActiveCamera;
        private static readonly Dictionary<SFXDataMesh.Raw, RawEffectAnchor> rawTargets = new Dictionary<SFXDataMesh.Raw, RawEffectAnchor>();
        private static RawEffectAnchor rawEffect;
        private static SFXDataMesh.Runtime nativeEffect;
        [ThreadStatic] private static Boolean sequenceCameraStack;
        private static readonly Dictionary<SFXData, Boolean> cameraStacks = new Dictionary<SFXData, Boolean>();
        private static readonly FieldInfo pendingEffects = typeof(SFXData).GetField("sfxLoadedNotPlayed", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo effectQueue = typeof(SFXData).GetField("loadingQueue", BindingFlags.Static | BindingFlags.NonPublic);
        private sealed class RawEffectAnchor
        {
            public BTL_DATA[] Targets;
            public Vector3? BakedScreen;
            public BTL_DATA Launcher;
            public Boolean PhysicalHit;
            public Boolean CasterRoot;
            public Boolean CameraStack;
            public CMD_DATA StackCommand;
        }

        internal static void InstallHooks(Action initialize = null)
        {
            if (initialize != null) formationInitialized = initialize;
            if (hooksInstalled) return;
            try
            {
                Assembly harmony = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "0Harmony");
                if (harmony == null)
                {
                    String directory = Path.GetDirectoryName(typeof(BattleLayoutRuntime).Assembly.Location);
                    String path = Path.Combine(directory, "QuickRuntime/0Harmony.dll");
                    if (!File.Exists(path)) path = Path.GetFullPath("The Lost Chapters/StreamingAssets/Scripts/QuickRuntime/0Harmony.dll");
                    harmony = Assembly.LoadFrom(path);
                }
                Type type = harmony.GetType("HarmonyLib.Harmony", true);
                Type methodType = harmony.GetType("HarmonyLib.HarmonyMethod", true);
                System.Object instance = Activator.CreateInstance(type, "battle.layout." + typeof(BattleLayoutRuntime).Assembly.GetName().Name);
                MethodInfo patch = Array.Find(type.GetMethods(), m => m.Name == "Patch" && m.GetParameters().Length >= 3 &&
                    m.GetParameters()[0].ParameterType == typeof(MethodBase) && m.GetParameters()[2].Name == "postfix");
                if (patch == null) throw new MissingMethodException("Harmony.Patch");
                PatchHook(patch, instance, methodType, typeof(SFX).GetMethod(nameof(SFX.SetEnemyCamera)), nameof(AllowEnemyCamera), 1);
                PatchHook(patch, instance, methodType, typeof(BattleMapCameraController).GetMethod("OnPostRender", BindingFlags.Instance | BindingFlags.NonPublic), nameof(AfterBattleRender), 2);
                RestoresAfterNativeEffects = true;
                if (initialize != null)
                {
                    PatchHook(patch, instance, methodType, typeof(SFX).GetMethod(nameof(SFX.StartBattle)), nameof(AfterFormationInit), 2);
                    PatchHook(patch, instance, methodType, typeof(btl_init).GetMethod(nameof(btl_init.OrganizeEnemyData)), nameof(AfterFormationInit), 2);
                    PatchHook(patch, instance, methodType, typeof(btl_init).GetMethod(nameof(btl_init.InitPlayerData)), nameof(AfterFormationInit), 2);
                }
                PatchHook(patch, instance, methodType, typeof(SFXData).GetMethod(nameof(SFXData.LoadSFXRawInit)), nameof(RememberRawTarget), 2);
                PatchHook(patch, instance, methodType, typeof(SFXDataMesh.Raw).GetMethod(nameof(SFXDataMesh.Raw.End)), nameof(ForgetRawTarget), 2);
                PatchPair(patch, instance, methodType, typeof(SFXDataMesh.Raw).GetMethod(nameof(SFXDataMesh.Raw.Render)), nameof(BeginRawEffect), nameof(EndRawEffect));
                PatchPair(patch, instance, methodType, typeof(SFXDataMesh.Runtime).GetMethod(nameof(SFXDataMesh.Runtime.Render)), nameof(BeginNativeEffect), nameof(EndNativeEffect));
                PatchPair(patch, instance, methodType, typeof(UIFollowTarget).GetMethod(nameof(UIFollowTarget.UpdateUIPosition)), nameof(BeginFollowerProjection), nameof(EndFollowerProjection));
                PatchPair(patch, instance, methodType, typeof(BTL_DATA).GetMethod(nameof(BTL_DATA.ChangeModel)), nameof(BeginModelChange), nameof(EndModelChange));
                PatchPair(patch, instance, methodType, typeof(SFXDataMesh.Raw.RMesh.Frame).GetMethod(nameof(SFXDataMesh.Raw.RMesh.Frame.Render)), nameof(BeginRawMesh), nameof(EndRawMesh));
                PatchPair(patch, instance, methodType, typeof(SFXMesh).GetMethod(nameof(SFXMesh.Render)), nameof(BeginPluginMesh), nameof(EndWeaponMesh));
                PatchHook(patch, instance, methodType, typeof(SFXData).GetMethod(nameof(SFXData.BattleCallbackReader)), nameof(UseNativeWeaponSize), 2);
                PatchHook(patch, instance, methodType, typeof(BattleActionCode).GetMethod(nameof(BattleActionCode.TryGetArgBoolean)), nameof(RememberSequenceCameraChoice), 2);
                PatchHook(patch, instance, methodType, typeof(SFXData).GetMethod(nameof(SFXData.LoadSFX)), nameof(KeepNativeEffectCamera), 1);
                PatchPair(patch, instance, methodType, typeof(SFXData).GetMethod(nameof(SFXData.LoadLoop)), nameof(BeginStackLoading), nameof(EndStackLoading));
                PatchHook(patch, instance, methodType, typeof(SFXData).GetMethod(nameof(SFXData.Reinit)), nameof(ClearCameraStacks), 2);
                PatchHook(patch, instance, methodType, typeof(SFXDataMesh.Runtime).GetMethod(nameof(SFXDataMesh.Runtime.End)), nameof(ForgetStackOwner), 2);
                PatchHook(patch, instance, methodType, typeof(BattleActionCode).GetMethod(nameof(BattleActionCode.TryGetArgSFXInstance)), nameof(SelectStackTarget), 2);
                hooksInstalled = true;
            }
            catch (Exception ex)
            {
                Memoria.Prime.Log.Warning("[Battle Scene Layouts] Initialization hooks unavailable: " + ex.Message);
            }
        }

        private static void PatchHook(MethodInfo patch, System.Object instance, Type methodType, MethodInfo original, String callback, Int32 slot)
        {
            System.Object[] args = new System.Object[patch.GetParameters().Length];
            args[0] = original;
            args[slot] = Activator.CreateInstance(methodType, typeof(BattleLayoutRuntime).GetMethod(callback, BindingFlags.Static | BindingFlags.NonPublic));
            patch.Invoke(instance, args);
        }

        private static Boolean AllowEnemyCamera()
        {
            BattleLayoutCameraHook hook = ActiveCamera;
            return hook == null || hook.Settings == null || !hook.Settings.Enabled || !SceneAvailable() || !hook.CanOverrideCamera();
        }

        private static void PatchPair(MethodInfo patch, System.Object instance, Type methodType, MethodInfo original, String prefix, String finalizer)
        {
            ParameterInfo[] parameters = patch.GetParameters();
            System.Object[] args = new System.Object[parameters.Length];
            args[0] = original;
            args[1] = Activator.CreateInstance(methodType, typeof(BattleLayoutRuntime).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic));
            Int32 finalizerIndex = Array.FindIndex(parameters, parameter => parameter.Name == "finalizer");
            if (finalizerIndex < 0) throw new MissingMethodException("Harmony.Patch finalizer");
            args[finalizerIndex] = Activator.CreateInstance(methodType, typeof(BattleLayoutRuntime).GetMethod(finalizer, BindingFlags.Static | BindingFlags.NonPublic));
            patch.Invoke(instance, args);
        }

        private static unsafe void UseNativeWeaponSize(Int32 fullCode, void* pv)
        {
            if (ActiveCamera == null || pv == null || (fullCode >> 24) != 5 ||
                !SFXData.IsShortSpecialEffect(SFX.currentEffectID)) return;
            foreach (BTL_DATA actor in Units())
                if (actor.btl_id == (fullCode & 255))
                {
                    // Hit flashes are cosmetic. Neither HWS MODEL_SIZE nor an
                    // editor scale should enlarge their texture quads.
                    Vector3 size = PhysicalEffectSize();
                    Int32* values = (Int32*)pv;
                    values[0] = (Int32)size.x;
                    values[1] = (Int32)size.y;
                    values[2] = (Int32)size.z;
                    return;
                }
        }

        internal static Vector3 PhysicalEffectSize() { return new Vector3(4096f, 4096f, 4096f); }

        internal static Boolean UsesNativeEffectCamera(SpecialEffect effect, CMD_DATA command)
        {
            return command != null &&
                (command.cmd_no == BattleCommandId.Item || command.cmd_no == BattleCommandId.AutoPotion ||
                 (command.AbilityCategory & (2 | 16 | 128)) != 0) &&
                !SFXData.IsShortSpecialEffect(effect);
        }

        [ThreadStatic]
        private static Boolean sequenceCameraDisabled;

        private static void RememberSequenceCameraChoice(BattleActionCode __instance, String key, Boolean value, Boolean __result)
        {
            // The sequencer reads this argument immediately before calling LoadSFX.
            if (__instance.operation == "LoadSFX" && key == "UseCamera")
            {
                sequenceCameraDisabled = __result && !value;
                String stack;
                sequenceCameraStack = __instance.argument.TryGetValue("CameraStack", out stack) && String.Equals(stack, "True", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void KeepNativeEffectCamera(SFXData __instance, SpecialEffect effNum, CMD_DATA cmd, ref Boolean applyCamera)
        {
            Boolean explicitlyDisabled = sequenceCameraDisabled;
            Boolean stack = sequenceCameraStack;
            sequenceCameraDisabled = false;
            sequenceCameraStack = false;
            if (stack)
            {
                // Opt-in stacks explicitly choose one runtime owner. All companion
                // meshes must finish baking before that owner is loaded.
                applyCamera = !explicitlyDisabled;
                cameraStacks[__instance] = applyCamera;
                return;
            }
            BattleLayoutCameraHook hook = ActiveCamera;
            if (hook == null || hook.Settings == null || !hook.Settings.Enabled || !SceneAvailable()) return;
            if (explicitlyDisabled)
                applyCamera = false;
            else if (UsesNativeEffectCamera(effNum, cmd))
                applyCamera = true;
        }

        internal static Boolean IsStackOwner(SFXData effect)
        {
            Boolean owner;
            return effect != null && cameraStacks.TryGetValue(effect, out owner) && owner;
        }

        private static void ClearCameraStacks()
        {
            cameraStacks.Clear(); rawTargets.Clear(); sequenceCameraStack = sequenceCameraDisabled = false;
        }

        internal static Boolean CanPreloadStackOwner(SFXData owner, IEnumerable<SFXData> prepared)
        {
            if (!IsStackOwner(owner) || owner.cmdRef == null) return false;
            foreach (SFXData layer in prepared)
            {
                Boolean isOwner;
                Boolean marked = cameraStacks.TryGetValue(layer, out isOwner);
                if (!IsPreparedStackLayer(marked, isOwner, layer.cmdRef == owner.cmdRef,
                    layer.loadHasEnded, layer.mesh is SFXDataMesh.Raw)) return false;
            }
            return true;
        }

        internal static Boolean IsPreparedStackLayer(Boolean marked, Boolean owner, Boolean sameCommand, Boolean ended, Boolean raw)
        {
            return marked && !owner && sameCommand && ended && raw;
        }

        private static void BeginStackLoading(out HashSet<SFXData> __state)
        {
            __state = null;
            if (pendingEffects == null || effectQueue == null || SFXData.lockLoading ||
                SFXDataMesh.Runtime.RenderingCount != 0 || SFXDataMesh.Raw.RenderingCount != 0) return;
            var queue = effectQueue.GetValue(null) as Queue<SFXData>;
            SFXData owner = SFXData.LoadCur ?? (queue != null && queue.Count > 0 ? queue.Peek() : null);
            var prepared = pendingEffects.GetValue(null) as HashSet<SFXData>;
            if (prepared == null || prepared.Count == 0 || !CanPreloadStackOwner(owner, prepared)) return;
            __state = prepared;
            pendingEffects.SetValue(null, new HashSet<SFXData>());
        }

        private static void EndStackLoading(HashSet<SFXData> __state)
        {
            if (__state == null) return;
            var current = pendingEffects.GetValue(null) as HashSet<SFXData>;
            if (current != null) __state.UnionWith(current);
            pendingEffects.SetValue(null, __state);
        }

        private static void SelectStackTarget(BattleActionCode __instance, BTL_DATA caster, List<SFXData> sfxList,
            ref Int32 value, ref Boolean __result)
        {
            String text; Int32 target; SpecialEffect effect;
            if (!__instance.argument.TryGetValue("StackTarget", out text) || !Int32.TryParse(text, out target) ||
                !__instance.TryGetArgSFX("SFX", caster, out effect)) return;
            __result = false;
            for (Int32 i = 0; i < sfxList.Count; i++)
            {
                SFXData data = sfxList[i]; Boolean owner;
                if (data.id == effect && cameraStacks.TryGetValue(data, out owner) && !owner &&
                    data.sfxRequest != null && data.sfxRequest.trgno == 1 && data.sfxRequest.trg[0] != null &&
                    data.sfxRequest.trg[0].btl_id == target)
                { value = i; __result = true; return; }
            }
        }

        private static void ForgetStackOwner(SFXDataMesh.Runtime __instance) { ForgetStackMesh(__instance); }
        private static void ForgetStackMesh(SFXDataMesh mesh)
        {
            SFXData ended = null;
            foreach (SFXData effect in cameraStacks.Keys)
                if (ReferenceEquals(effect.mesh, mesh)) { ended = effect; break; }
            if (ended != null) cameraStacks.Remove(ended);
        }

        private static void RememberRawTarget(SFXData __instance)
        {
            BTL_VFX_REQ request = __instance.sfxRequest;
            Boolean casterRoot = UsesCasterRoot(__instance.cmdRef);
            if (request == null || !(__instance.mesh is SFXDataMesh.Raw mesh) ||
                (!casterRoot && (!request.useTargetAveragePosition || request.trgno <= 0)) ||
                (casterRoot && request.exe == null)) return;
            var anchor = new RawEffectAnchor
            {
                Targets = casterRoot ? new[] { request.exe } : new BTL_DATA[request.trgno],
                Launcher = IsRacketEffect(__instance.id) ? request.exe : null,
                PhysicalHit = SFXData.IsShortSpecialEffect(__instance.id) && !IsRacketEffect(__instance.id),
                CasterRoot = casterRoot
            };
            Boolean stackOwner;
            anchor.CameraStack = cameraStacks.TryGetValue(__instance, out stackOwner) && !stackOwner;
            anchor.StackCommand = __instance.cmdRef;
            if (!casterRoot) Array.Copy(request.trg, anchor.Targets, request.trgno);
            if (anchor.Launcher == null && ActiveCamera != null &&
                ActiveCamera.TryProjectRawTarget(anchor.Targets, false, out Vector3 screen, anchor.CasterRoot))
                anchor.BakedScreen = screen;
            rawTargets[mesh] = anchor;
        }

        internal static Boolean UsesCasterRoot(CMD_DATA command)
        {
            return command != null && command.cmd_no == BattleCommandId.SysTrans;
        }
        private static void BeginModelChange(BTL_DATA __instance, out GameObject __state)
        {
            __state = __instance.gameObject;
        }
        private static void EndModelChange(BTL_DATA __instance, GameObject __state)
        {
            if (ActiveCamera == null || !SceneAvailable() || __state == null ||
                __instance.gameObject == null || __instance.gameObject == __state) return;
            ActiveCamera.RefreshChangedModelScale(__instance);
            foreach (UIFollowTarget follower in UnityEngine.Object.FindObjectsOfType<UIFollowTarget>())
            {
                if (follower == null || follower.target == null ||
                    !follower.target.IsChildOf(__state.transform)) continue;
                follower.target = __instance.gameObject.transform.GetChildByName(follower.target.name) ?? __instance.gameObject.transform;
                follower.UpdateUIPosition();
            }
        }
        private static Boolean IsRacketEffect(SpecialEffect effect)
        {
            return effect == SpecialEffect.Player_Attack_Garnet_LH_Racket ||
                effect == SpecialEffect.Player_Attack_Garnet_SH_Racket ||
                effect == SpecialEffect.Player_Attack_Eiko_Racket ||
                (effect >= SpecialEffect.SFX_Attack_Air_Racket && effect <= SpecialEffect.SFX_Attack_Tiger_Racket);
        }

        private static void ForgetRawTarget(SFXDataMesh.Raw __instance)
        {
            rawTargets.Remove(__instance);
            ForgetStackMesh(__instance);
        }

        internal static void ClearRawTargets() { rawTargets.Clear(); }

        private struct FollowerProjectionState
        {
            internal Camera Camera;
            internal Matrix4x4 View, Projection;
            internal Boolean EveryFrame;
        }
        private static void BeginFollowerProjection(UIFollowTarget __instance, out FollowerProjectionState __state)
        {
            __state = default(FollowerProjectionState);
            BattleLayoutCameraHook hook = ActiveCamera;
            Matrix4x4 view, projection;
            if (hook == null || !hook.TryGetFrameCamera(__instance.worldCam, out view, out projection)) return;
            __state.Camera = __instance.worldCam;
            __state.View = __state.Camera.worldToCameraMatrix;
            __state.Projection = __state.Camera.projectionMatrix;
            __state.EveryFrame = __instance.updateEveryFrame;
            __state.Camera.worldToCameraMatrix = view;
            __state.Camera.projectionMatrix = projection;
            // UIFollowTarget's own cache does not compare projection matrices.
            __instance.updateEveryFrame = true;
        }
        private static void EndFollowerProjection(UIFollowTarget __instance, FollowerProjectionState __state)
        {
            if (__state.Camera == null) return;
            __instance.updateEveryFrame = __state.EveryFrame;
            __state.Camera.worldToCameraMatrix = __state.View;
            __state.Camera.projectionMatrix = __state.Projection;
        }
        private static void BeginNativeEffect(SFXDataMesh.Runtime __instance, out SFXDataMesh.Runtime __state)
        {
            __state = nativeEffect; nativeEffect = __instance;
        }
        private static void EndNativeEffect(SFXDataMesh.Runtime __state) { nativeEffect = __state; }
        private static void BeginRawEffect(SFXDataMesh.Raw __instance, out RawEffectAnchor __state)
        {
            __state = rawEffect;
            rawTargets.TryGetValue(__instance, out rawEffect);
        }

        private static void EndRawEffect(RawEffectAnchor __state)
        {
            rawEffect = __state;
        }

        private static void BeginRawMesh(SFXDataMesh.Raw.RMesh.Frame __instance, Int32 frame, SFXDataMesh.EffectMaterial material, out Vector3[] __state)
        {
            __state = null;
            PSXTextureMgr.Kind kind = material.textureKind;
            Texture texture = material.texture;
            // Inspect the texture that will be bound for this frame, including pending changes.
            foreach (var change in material.textureChanger)
                if (frame >= change.frame && material.textureFrameLoaded < change.frame)
                {
                    kind = change.textureKind;
                    texture = change.texture;
                }
            if ((kind != PSXTextureMgr.Kind.IMAGE && kind != PSXTextureMgr.Kind.NONE) ||
                texture is RenderTexture) return;
            if (IsFullScreenQuad(__instance.vertex, __instance.vertex == null ? 0 : __instance.vertex.Length)) return;
            SFXData owner = SFXData.LoadCur;
            if (rawEffect != null && rawEffect.CameraStack && IsStackOwner(owner) &&
                rawEffect.StackCommand == owner.cmdRef &&
                SFXDataMesh.Runtime.RenderingCount > 0 && ActiveCamera != null && RenderTexture.active == null &&
                rawEffect.BakedScreen.HasValue && ActiveCamera.TryProjectStackTarget(rawEffect.Targets, out Vector3 stackTarget))
            {
                // Bake once under the idle camera, then anchor each layer to the
                // current owner camera. Screen overlays keep their existing guard.
                __state = __instance.vertex;
                if (__state == null) return;
                Vector3 viewport = BattleLayoutCameraHook.PsxScreenSize;
                __instance.vertex = TransformScreenMesh(__state, __state.Length, __instance.index, __instance.index.Length,
                    rawEffect.BakedScreen.Value, stackTarget, ActiveCamera.NativeSfxScreenScale, viewport.x, viewport.y);
                return;
            }
            Single nativeZoom = ActiveCamera == null ? 1f : ActiveCamera.NativeSfxScreenScale;
            if (nativeZoom != 1f && RenderTexture.active == null && __instance.vertex != null)
            {
                __state = __instance.vertex;
                Vector3 screen = BattleLayoutCameraHook.PsxScreenSize;
                Vector3 zoomOffset = BattleLayoutCameraHook.ZoomSfxScreenVertex(Vector3.zero, nativeZoom);
                Vector3[] zoomed = TransformScreenMesh(__state, __state.Length, __instance.index, __instance.index.Length,
                    Vector3.zero, zoomOffset, nativeZoom, screen.x, screen.y);
                __instance.vertex = zoomed;
                return;
            }
            if (rawEffect == null || ActiveCamera == null || RenderTexture.active != null ||
                !ActiveCamera.TryProjectRawTarget(rawEffect.Targets, true, out Vector3 editedScreen, rawEffect.CasterRoot)) return;
            Vector3 targetScreen;
            if (rawEffect.BakedScreen.HasValue) targetScreen = rawEffect.BakedScreen.Value;
            else if (!ActiveCamera.TryProjectRawTarget(rawEffect.Targets, false, out targetScreen, rawEffect.CasterRoot)) return;
            Vector3 offset = editedScreen - targetScreen;
            Vector3[] original = __instance.vertex;
            if (original == null || original.Length == 0) return;
            Bounds bounds = new Bounds(original[0], Vector3.zero);
            foreach (Vector3 vertex in original)
            {
                bounds.Encapsulate(vertex);
            }
            // PSX Z is a sorting value, not proof of a target-attached 3D layer.
            // Keep unknown untextured overlays in screen space; weapon-hit layers
            // retain their established correction path.
            if ((kind == PSXTextureMgr.Kind.NONE && !rawEffect.PhysicalHit && !rawEffect.CasterRoot) ||
                bounds.size.x >= 2047f || bounds.size.y >= 2047f) return;
            if (rawEffect.Launcher != null &&
                ActiveCamera.TryGetEffectOffset(rawEffect.Launcher, out Vector3 launchOffset, out Vector3 launchScreen, true))
            {
                Vector3 center = new Vector3(bounds.center.x, bounds.center.y, 0f);
                Vector3 path = targetScreen - launchScreen;
                if (path.sqrMagnitude > 1f)
                {
                    Single travel = Mathf.Clamp01(Vector3.Dot(center - launchScreen, path) / path.sqrMagnitude);
                    offset = Vector3.Lerp(launchOffset, offset, travel);
                }
            }
            Single hitScale = rawEffect.PhysicalHit && rawEffect.Targets.Length == 1
                ? ActiveCamera.PhysicalHitScreenScale(rawEffect.Targets[0]) : 1f;
            Vector3 screenSize = BattleLayoutCameraHook.PsxScreenSize;
            Vector3[] shifted = TransformScreenMesh(original, original.Length, __instance.index, __instance.index.Length,
                targetScreen, targetScreen + offset, hitScale, screenSize.x, screenSize.y);
            __state = original;
            __instance.vertex = shifted;
        }


        private static void EndRawMesh(SFXDataMesh.Raw.RMesh.Frame __instance, Vector3[] __state)
        {
            if (__state != null) __instance.vertex = __state;
        }

        internal static Boolean IsFullScreenQuad(Vector3[] vertices, Int32 count)
        {
            Vector3 size = BattleLayoutCameraHook.PsxScreenSize;
            return IsFullScreenQuadAtSize(vertices, count, size.x, size.y);
        }

        internal static Boolean IsFullScreenQuadAtSize(Vector3[] vertices, Int32 count, Single width, Single height)
        {
            return vertices != null && count == 4 && vertices.Length >= count &&
                IsScreenRectangle(vertices[0], vertices[1], vertices[2], vertices[3], width, height);
        }
        private static Boolean IsScreenRectangle(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Single width, Single height)
        {
            Single minX = Math.Min(Math.Min(a.x, b.x), Math.Min(c.x, d.x));
            Single maxX = Math.Max(Math.Max(a.x, b.x), Math.Max(c.x, d.x));
            Single minY = Math.Min(Math.Min(a.y, b.y), Math.Min(c.y, d.y));
            Single maxY = Math.Max(Math.Max(a.y, b.y), Math.Max(c.y, d.y));
            // Some native overlays retain the centered 320-pixel PSX viewport
            // even when the world renderer expands its width for widescreen.
            Single left = Math.Max(0f, (width - 320f) * 0.5f);
            if (minX > left + 1f || maxX < width - left - 1f || minY > 1f || maxY < height - 1f) return false;
            return IsRectangleCorner(a, minX, maxX, minY, maxY) && IsRectangleCorner(b, minX, maxX, minY, maxY) &&
                IsRectangleCorner(c, minX, maxX, minY, maxY) && IsRectangleCorner(d, minX, maxX, minY, maxY);
        }
        private static Boolean IsRectangleCorner(Vector3 p, Single left, Single right, Single top, Single bottom)
        {
            return (Math.Abs(p.x - left) <= 1f || Math.Abs(p.x - right) <= 1f) &&
                (Math.Abs(p.y - top) <= 1f || Math.Abs(p.y - bottom) <= 1f);
        }
        internal static Vector3[] TransformScreenMesh(Vector3[] original, Int32 vertexCount, Int32[] indices, Int32 indexCount,
            Vector3 from, Vector3 to, Single scale, Single width, Single height)
        {
            Vector3[] result = (Vector3[])original.Clone();
            Int32 count = Math.Min(vertexCount, original.Length);
            Boolean[] screenVertex = null;
            var tileLayers = new Dictionary<Single, ScreenTileLayer>();
            if (indices != null)
                for (Int32 i = 0; i + 5 < Math.Min(indexCount, indices.Length); i += 3)
                {
                    Int32 a = indices[i], b = indices[i + 1], c = indices[i + 2], d = -1;
                    Boolean valid = a >= 0 && a < count && b >= 0 && b < count && c >= 0 && c < count;
                    for (Int32 j = 3; valid && j < 6; j++)
                    {
                        Int32 v = indices[i + j];
                        if (v < 0 || v >= count) { valid = false; break; }
                        if (v != a && v != b && v != c)
                        {
                            if (d >= 0 && d != v) valid = false;
                            else d = v;
                        }
                    }
                    if (!valid || d < 0) continue;
                    Vector3 va = original[a], vb = original[b], vc = original[c], vd = original[d];
                    Single l = Math.Min(Math.Min(va.x, vb.x), Math.Min(vc.x, vd.x));
                    Single r = Math.Max(Math.Max(va.x, vb.x), Math.Max(vc.x, vd.x));
                    Single t = Math.Min(Math.Min(va.y, vb.y), Math.Min(vc.y, vd.y));
                    Single btm = Math.Max(Math.Max(va.y, vb.y), Math.Max(vc.y, vd.y));
                    if (va.z == vb.z && va.z == vc.z && va.z == vd.z && r > l && btm > t && IsRectangleCorner(va,l,r,t,btm) && IsRectangleCorner(vb,l,r,t,btm) &&
                        IsRectangleCorner(vc,l,r,t,btm) && IsRectangleCorner(vd,l,r,t,btm))
                    {
                        ScreenTileLayer layer;
                        if (!tileLayers.TryGetValue(va.z, out layer))
                            tileLayers.Add(va.z, layer = new ScreenTileLayer(count));
                        layer.Vertices[a] = layer.Vertices[b] = layer.Vertices[c] = layer.Vertices[d] = true;
                        layer.Area += (r-l)*(btm-t);
                        layer.Left = Math.Min(layer.Left,l); layer.Right = Math.Max(layer.Right,r);
                        layer.Top = Math.Min(layer.Top,t); layer.Bottom = Math.Max(layer.Bottom,btm);
                    }
                    i += 3; // The two triangles belong to this one quad.
                    if (!IsScreenRectangle(va, vb, vc, vd, width, height)) continue;
                    if (screenVertex == null) screenVertex = new Boolean[count];
                    screenVertex[a] = screenVertex[b] = screenVertex[c] = screenVertex[d] = true;
                }
            // Screen fills at different sorting depths must not inflate each other's coverage.
            foreach (ScreenTileLayer layer in tileLayers.Values)
                if (layer.Area > 0f && layer.Area >= (layer.Right-layer.Left)*(layer.Bottom-layer.Top)*0.99f &&
                    layer.Area <= (layer.Right-layer.Left)*(layer.Bottom-layer.Top)*1.01f &&
                    IsScreenRectangle(new Vector3(layer.Left,layer.Top,0), new Vector3(layer.Right,layer.Top,0),
                        new Vector3(layer.Left,layer.Bottom,0), new Vector3(layer.Right,layer.Bottom,0), width,height))
                {
                    if (screenVertex == null) screenVertex = layer.Vertices;
                    else for (Int32 v = 0; v < count; v++) screenVertex[v] |= layer.Vertices[v];
                }
            for (Int32 i = 0; i < count; i++)
                if (screenVertex == null || !screenVertex[i]) result[i] = TransferScreenVertex(original[i], from, to, scale);
            return result;
        }
        private sealed class ScreenTileLayer
        {
            internal readonly Boolean[] Vertices;
            internal Single Area, Left = Single.MaxValue, Top = Single.MaxValue;
            internal Single Right = Single.MinValue, Bottom = Single.MinValue;
            internal ScreenTileLayer(Int32 count) { Vertices = new Boolean[count]; }
        }
        private struct NativeMeshState
        {
            internal Mesh Mesh;
            internal Vector3[] Vertices;
        }
        internal static Vector3 TransferScreenVertex(Vector3 vertex, Vector3 from, Vector3 to, Single scale)
        {
            return new Vector3(to.x + (vertex.x - from.x) * scale,
                to.y + (vertex.y - from.y) * scale, vertex.z);
        }
        private static void ApplyNativeMeshTransform(SFXMesh mesh, Vector3 from, Vector3 to, Single scale, out NativeMeshState state)
        {
            state = default(NativeMeshState);
            if (mesh._mesh == null) return;
            Vector3[] original = mesh._mesh.vertices;
            Vector3 screen = BattleLayoutCameraHook.PsxScreenSize;
            Vector3[] transformed = TransformScreenMesh(original, mesh.VbOffset, mesh.IbIndex, mesh.IbOffset,
                from, to, scale, screen.x, screen.y);
            state.Mesh = mesh._mesh; state.Vertices = original;
            // DrawMeshNow supplies its own matrix. Transform its actual mesh,
            // then restore it in the paired finalizer, rather than relying on GL.
            mesh._mesh.vertices = transformed;
        }
        private static void BeginPluginMesh(SFXMesh __instance, out NativeMeshState __state)
        {
            __state = default(NativeMeshState);
            if (ActiveCamera == null || RenderTexture.active != null) return;
            if (IsFullScreenQuad(__instance.VbPos, __instance.VbOffset)) return;
            if (SFXKey.IsTexture(__instance._key))
            {
                PSXTextureMgr.Kind kind;
                Texture texture = __instance.GetTexture(out kind);
                if (kind != PSXTextureMgr.Kind.IMAGE || texture is RenderTexture) return;
            }
            if (ActiveCamera.NativeSfxScreenScale != 1f)
            {
                Single zoom = ActiveCamera.NativeSfxScreenScale;
                Vector3 offset = BattleLayoutCameraHook.ZoomSfxScreenVertex(Vector3.zero, zoom);
                ApplyNativeMeshTransform(__instance, Vector3.zero, offset, zoom, out __state);
                return;
            }
            if (nativeEffect != null && ActiveCamera.IsNativeTailHandoff(nativeEffect))
            {
                BTL_VFX_REQ request = nativeEffect.sfxRequest;
                Vector3 from, to;
                Boolean casterRoot = UsesCasterRoot(nativeEffect.cmd);
                if (request != null && (casterRoot ? request.exe != null : request.useTargetAveragePosition && request.trgno > 0))
                {
                    BTL_DATA[] targets = casterRoot ? new[] { request.exe } : new BTL_DATA[request.trgno];
                    if (!casterRoot) Array.Copy(request.trg, targets, request.trgno);
                    if (ActiveCamera.TryProjectRawTarget(targets, false, out from, casterRoot) &&
                        ActiveCamera.TryProjectRawTarget(targets, true, out to, casterRoot))
                    {
                        Single scale = targets.Length == 1 ? ActiveCamera.PhysicalHitScreenScale(targets[0], casterRoot) : 1f;
                        ApplyNativeMeshTransform(__instance, from, to, scale, out __state);
                    }
                }
                return;
            }
            if (!SFXData.IsShortSpecialEffect(SFX.currentEffectID)) return;
            CMD_DATA command = btl_util.getCurCmdPtr();
            if (command == null || command.tar_id == 0 || (command.tar_id & (command.tar_id - 1)) != 0) return;
            foreach (BTL_DATA unit in Units())
                if (unit.btl_id == command.tar_id)
                {
                    Vector3 offset, from;
                    if (ActiveCamera.TryGetEffectOffset(unit, out offset, out from))
                    {
                        Single scale = IsRacketEffect(SFX.currentEffectID) ? 1f : ActiveCamera.PhysicalHitScreenScale(unit);
                        ApplyNativeMeshTransform(__instance, from, from + offset, scale, out __state);
                    }
                    break;
                }
        }
        private static void EndWeaponMesh(NativeMeshState __state)
        {
            if (__state.Mesh != null && __state.Vertices != null) __state.Mesh.vertices = __state.Vertices;
        }
        private static void AfterFormationInit()
        {
            formationInitialized?.Invoke();
        }

        private static void AfterBattleRender()
        {
            if (ActiveCamera != null) ActiveCamera.Restore();
        }
        public static Boolean SceneAvailable()
        {
            return SceneDirector.IsBattleScene() && FF9StateSystem.Battle != null &&
                FF9StateSystem.Battle.FF9Battle != null && FF9StateSystem.Battle.FF9Battle.btl_scene != null &&
                FF9StateSystem.Battle.FF9Battle.btl_scene.PatAddr != null;
        }
        public static Boolean Ready()
        {
            return SceneAvailable() &&
                (FF9StateSystem.Battle.FF9Battle.btl_load_status & ff9btl.LOAD_ALL) == ff9btl.LOAD_ALL;
        }
        public static Boolean Idle()
        {
            return Ready() && FF9StateSystem.Battle.FF9Battle.btl_phase == FF9StateBattleSystem.PHASE_NORMAL &&
                !btlseq.BtlSeqBusy() && !UnitsBusy();
        }
        private static Boolean UnitsBusy()
        {
            foreach (BTL_DATA b in Units()) if (btl_util.IsBtlBusy(b, btl_util.BusyMode.ANY_CURRENT)) return true;
            return false;
        }
        public static IEnumerable<BTL_DATA> Units()
        {
            if (!SceneAvailable()) yield break;
            for (BTL_DATA b = FF9StateSystem.Battle.FF9Battle.btl_list.next; b != null; b = b.next)
                if (b.btl_id != 0 && b.bi != null && b.evt != null) yield return b;
        }
        public static Int32 Slot(BTL_DATA b) { return b.bi.player != 0 ? b.bi.line_no : b.bi.slot_no; }
        public static String Key(BTL_DATA b) { return (b.bi.player != 0 ? "P" : "E") + Slot(b); }
        public static String Key(LayoutUnit b) { return (b.Party ? "P" : "E") + b.Slot; }
        public static Boolean Matches(BattleLayout layout)
        {
            return SceneAvailable() && layout.BattleId == FF9StateSystem.Battle.battleMapIndex && layout.Pattern == FF9StateSystem.Battle.FF9Battle.btl_scene.PatNum;
        }
        public static LayoutUnit Capture(BTL_DATA b)
        {
            String name;
            try { name = new BattleUnit(b).Name; } catch { name = b.bi.player != 0 ? "Party" : "Enemy"; }
            return new LayoutUnit { Party = b.bi.player != 0, Slot = Slot(b), Name = name, Home = b.base_pos, HasRotation = true, Rotation = b.evt.rotBattle.eulerAngles };
        }
        public static Boolean MoveHome(BTL_DATA b, Vector3 home)
        {
            // Never pin animated transforms each frame. Update the formation anchors
            // once, when no command is moving or targeting this actor.
            if (!CanMoveHome(b)) return false;
            Vector3 delta = home - b.base_pos;
            b.base_pos = home; b.original_pos += delta; b.evt.posBattle += delta; b.pos += delta;
            if (b.gameObject != null) b.gameObject.transform.localPosition = b.pos;
            return true;
        }
        public static Boolean ApplyUnit(BTL_DATA actor, LayoutUnit unit)
        {
            if (!MoveHome(actor, unit.Home)) return false;
            if (unit.HasRotation)
            {
                actor.rot = actor.evt.rotBattle = Quaternion.Euler(unit.Rotation);
                if (actor.gameObject != null) actor.gameObject.transform.localRotation = actor.rot;
            }
            return true;
        }
        public static Boolean CanMoveHome(BTL_DATA b) { return !btl_util.IsBtlBusy(b, btl_util.BusyMode.ANY_CURRENT); }
        public static Boolean ApplyUnits(BattleLayout layout)
        {
            if (!Matches(layout)) throw new InvalidOperationException("Battle ID or formation pattern does not match this layout.");
            Boolean complete = true;
            foreach (LayoutUnit u in layout.Units)
                foreach (BTL_DATA b in Units())
                    if (Key(b) == Key(u) && !ApplyUnit(b, u)) complete = false;
            return complete;
        }
    }

    public sealed class BattleLayoutCameraHook : MonoBehaviour
    {
        public LayoutCamera Settings;
        public Boolean Freeze;
        public Boolean Editing;
        public Boolean HoldCamera;
        public List<LayoutUnit> ModelUnits;
        public Single StageScale = 1f;
        private sealed class StageTransform
        {
            public Vector3 Position, Scale, AppliedPosition, AppliedScale;
        }
        private readonly Dictionary<Transform, StageTransform> stageTransforms = new Dictionary<Transform, StageTransform>();
        public Matrix4x4 NativeView, NativeProjection;
        public Boolean HasNative;
        private Camera cameraComponent;
        private Matrix4x4 savedView, savedProjection;
        private readonly List<KeyValuePair<Transform, Quaternion>> billboardRotations = new List<KeyValuePair<Transform, Quaternion>>();
        private static readonly FieldInfo battleSpsUtility = typeof(BattleSPSSystem).GetField("Utility", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo battleShpEffects = typeof(BattleSPSSystem).GetField("_shpEffects", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo psxWidth = typeof(FieldMap).GetField("PsxScreenWidth", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo psxHeight = typeof(FieldMap).GetField("PsxScreenHeightNative", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private Boolean applied;
        private HUDMessage messages;
        private Boolean frameAllowed;
        private Boolean nativeSfxFrame;
        private sealed class ModelScaleState
        {
            public Vector3 Native;
            public Vector3 Applied;
        }
        private readonly Dictionary<BTL_DATA, ModelScaleState> scaledActors = new Dictionary<BTL_DATA, ModelScaleState>();
        private Matrix4x4 frameView, frameProjection;
        private Int32 frameCameraNumber = -1;
        private Matrix4x4 handoffView;
        private Matrix4x4 nativeIdleView;
        private Boolean hasNativeIdle, effectMovedCamera, effectReturnedToIdle;
        private SFXData cameraEffect;
        private Single handoffStart, handoffFov, frameFov;
        private Boolean handoffStarted;
        public static BattleLayoutCameraHook Attach(Camera camera)
        {
            BattleLayoutCameraHook hook = camera.GetComponent<BattleLayoutCameraHook>();
            if (hook == null) hook = camera.gameObject.AddComponent<BattleLayoutCameraHook>();
            return hook;
        }
        public void Release()
        {
            Restore(); Settings = null; Freeze = false; Editing = false; HoldCamera = false; frameAllowed = false; nativeSfxFrame = false;
            handoffStarted = false; handoffStart = 0f;
            ModelUnits = null;
            hasNativeIdle = effectMovedCamera = effectReturnedToIdle = false;
            cameraEffect = null;
            StageScale = 1f;
            RestoreStageScale();
            if (BattleLayoutRuntime.ActiveCamera == this)
            {
                BattleLayoutRuntime.ActiveCamera = null;
                BattleLayoutRuntime.ClearRawTargets();
            }
            RestoreModelScales();
        }
        public void TakeEditorOwnership()
        {
            // Runtime and editor can live in separate mod DLLs with separate copies
            // of these types. Exchange only JSON, never cast across those assemblies.
            foreach (MonoBehaviour component in cameraComponent.GetComponents<MonoBehaviour>())
            {
                if (component == null || component == this || component.GetType().FullName != GetType().FullName) continue;
                component.GetType().GetMethod("RestoreModelScales", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(component, null);
                component.GetType().GetMethod("RestoreStageScale", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
                if (component.GetType().GetField("StageScale")?.GetValue(component) is Single stageScale)
                    StageScale = stageScale;
                System.Object value = component.GetType().GetField("Settings").GetValue(component);
                if (value == null) continue;
                String json = value.GetType().GetMethod("ToJson").Invoke(value, null).ToString();
                Settings = BattleLayout.Parse("{\"version\":1,\"battleId\":0,\"pattern\":0,\"camera\":" + json + ",\"units\":[]}").Camera;
                var units = component.GetType().GetField("ModelUnits")?.GetValue(component) as System.Collections.IEnumerable;
                if (units != null)
                {
                    List<String> entries = new List<String>();
                    foreach (System.Object unit in units)
                        entries.Add(unit.GetType().GetMethod("ToJson").Invoke(unit, null).ToString());
                    ModelUnits = BattleLayout.Parse("{\"version\":1,\"battleId\":0,\"pattern\":0,\"camera\":" + json + ",\"units\":[" + String.Join(",", entries.ToArray()) + "]}").Units;
                }
                break;
            }
            Editing = true;
        }
        private Boolean OtherEditorOwnsCamera()
        {
            if (Editing) return false;
            foreach (MonoBehaviour component in cameraComponent.GetComponents<MonoBehaviour>())
                if (component != null && component != this && component.GetType().FullName == GetType().FullName && (Boolean)component.GetType().GetField("Editing").GetValue(component)) return true;
            return false;
        }
        private void Awake() { cameraComponent = GetComponent<Camera>(); }
        internal void PrepareInitialFrame()
        {
            UpdateStageScale();
            UpdateModelScales();
            if (Settings == null || !Settings.Enabled || cameraComponent == null || !CanOverrideCamera()) return;
            UpdateFrameView();
            frameAllowed = true;
            BattleLayoutRuntime.ActiveCamera = this;
        }
        private void OnDisable() { Release(); }
        private void OnDestroy() { Release(); }
        public LayoutCamera Capture()
        {
            if (!HasNative) throw new InvalidOperationException("Wait for the battle camera to render one frame.");
            Matrix4x4 inverse = NativeView.inverse;
            return new LayoutCamera { Basis = NativeView, Position = inverse.MultiplyPoint3x4(Vector3.zero), Fov = Fov(NativeProjection), SfxZoom = Settings == null ? 1f : Settings.SfxZoom };
        }
        public LayoutCamera CaptureCurrent()
        {
            if (Settings == null || !Settings.Enabled) return Capture();
            return new LayoutCamera { Basis = View(Settings), Position = Settings.Position, Fov = Settings.Fov, SfxZoom = Settings.SfxZoom };
        }
        public static Matrix4x4 View(LayoutCamera settings)
        {
            Vector3 capturedPosition = settings.Basis.inverse.MultiplyPoint3x4(Vector3.zero);
            Matrix4x4 angles = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(settings.Angles), Vector3.one).inverse;
            return angles * settings.Basis * Matrix4x4.TRS(capturedPosition - settings.Position, Quaternion.identity, Vector3.one);
        }
        public static Single Fov(Matrix4x4 projection)
        {
            return Mathf.Clamp(2f * Mathf.Atan(1f / Mathf.Max(0.001f, Mathf.Abs(projection.m11))) * Mathf.Rad2Deg, 5, 120);
        }
        private void LateUpdate()
        {
            Restore();
            frameAllowed = false;
            if (!BattleLayoutRuntime.SceneAvailable() || cameraComponent == null || OtherEditorOwnsCamera())
            {
                RestoreStageScale();
                RestoreModelScales();
                return;
            }
            UpdateStageScale();
            UpdateModelScales();
            NativeView = cameraComponent.worldToCameraMatrix; NativeProjection = cameraComponent.projectionMatrix; HasNative = true;
            TrackEffectCamera();
            nativeSfxFrame = false;
            if (Settings == null || !Settings.Enabled || !(HoldCamera || Editing)) return;
            nativeSfxFrame = CanZoomNativeSfx();
            if (!nativeSfxFrame && !CanOverrideCamera()) return;
            BattleLayoutRuntime.ActiveCamera = this;
            if (nativeSfxFrame)
            {
                frameView = NativeView;
                frameFov = 2f * Mathf.Atan(Mathf.Tan(Fov(NativeProjection) * Mathf.Deg2Rad * 0.5f) * Settings.SfxZoom) * Mathf.Rad2Deg;
            }
            else UpdateFrameView();
            frameAllowed = true;
            ApplyMatrices();
            try
            {
                // UI work belongs to the normal update stage, never a render callback.
                if (UIManager.Battle != null && UIManager.Battle.modelButtonManager != null)
                    UIManager.Battle.modelButtonManager.UpdateModelButtonPosition();
                if (messages == null) messages = UnityEngine.Object.FindObjectOfType(typeof(HUDMessage)) as HUDMessage;
                if (messages != null) messages.UpdateChildPosition();
            }
            finally { Restore(); }
        }
        private void UpdateFrameView()
        {
            frameView = View(Settings);
            frameFov = Settings.Fov;
            if (!HoldCamera || Editing || !HasNative) return;
            if (!handoffStarted)
            {
                handoffStarted = true;
                handoffStart = Time.unscaledTime;
                handoffView = NativeView;
                handoffFov = Fov(NativeProjection);
            }
            const Single duration = 0.85f;
            Single progress = Mathf.Clamp01((Time.unscaledTime - handoffStart) / duration);
            if (progress >= 1f) return;
            Single blend = Mathf.SmoothStep(0f, 1f, progress);
            Matrix4x4 from = handoffView.inverse, to = frameView.inverse;
            Vector3 position = Vector3.Lerp(from.GetColumn(3), to.GetColumn(3), blend);
            Quaternion fromRotation = Quaternion.LookRotation(-from.GetColumn(2), from.GetColumn(1));
            Quaternion toRotation = Quaternion.LookRotation(-to.GetColumn(2), to.GetColumn(1));
            Quaternion rotation = Quaternion.Slerp(fromRotation, toRotation, blend);
            frameView = Matrix4x4.TRS(position, rotation, new Vector3(1f, 1f, -1f)).inverse;
            frameFov = Mathf.Lerp(handoffFov, frameFov, blend);
        }

        internal static Boolean NativeEffectOwnsCamera(Int32 renderingCount, Boolean useCamera)
        {
            return renderingCount > 0 && useCamera;
        }
        private Boolean CanZoomNativeSfx()
        {
            if (Settings == null || Settings.SfxZoom <= 1f || !BattleLayoutRuntime.SceneAvailable() ||
                FF9StateSystem.Battle.FF9Battle.btl_phase != FF9StateBattleSystem.PHASE_NORMAL) return false;
            SFXData effect = SFXData.LoadCur;
            return SFXDataMesh.Runtime.RenderingCount > 0 && effect != null && effect.useCamera &&
                (BattleLayoutRuntime.IsStackOwner(effect) || !(ReferenceEquals(effect, cameraEffect) && effectReturnedToIdle));
        }

        internal static Vector3 PsxScreenSize { get { return new Vector3(Convert.ToSingle(psxWidth.GetValue(null)), Convert.ToSingle(psxHeight.GetValue(null)), 0f); } }
        internal Single NativeSfxScreenScale { get { return applied && nativeSfxFrame ? 1f / Settings.SfxZoom : 1f; } }
        internal static Vector3 ZoomSfxScreenVertex(Vector3 vertex, Single scale)
        {
            Single x = Convert.ToSingle(psxWidth.GetValue(null)) * 0.5f;
            Single y = Convert.ToSingle(psxHeight.GetValue(null)) * 0.5f;
            return new Vector3(x + (vertex.x - x) * scale, y + (vertex.y - y) * scale, vertex.z);
        }

        internal Boolean CanOverrideCamera()
        {
            if (!BattleLayoutRuntime.SceneAvailable()) return false;
            Byte phase = FF9StateSystem.Battle.FF9Battle.btl_phase;
            if (phase == FF9StateBattleSystem.PHASE_MENU_OFF)
            {
                // Victory setup can wait for commands or the fanfare before SetCamera(2).
                Byte sequence = FF9StateSystem.Battle.FF9Battle.btl_seq;
                return FF9StateSystem.Battle.FF9Battle.btl_scene.Info.WinPose &&
                    (sequence == FF9StateBattleSystem.SEQ_MENU_OFF_VICTORY ||
                     sequence == FF9StateBattleSystem.SEQ_MENU_OFF_VICTORY_WITH_ENEMY);
            }
            if (phase != FF9StateBattleSystem.PHASE_NORMAL) return false;

            SFXData effect = SFXData.LoadCur;
            if (BattleLayoutRuntime.IsStackOwner(effect) && SFXDataMesh.Runtime.RenderingCount > 0) return false;
            return SFXDataMesh.Runtime.RenderingCount == 0 || effect == null || !effect.useCamera ||
                (ReferenceEquals(effect, cameraEffect) && effectReturnedToIdle);
        }

        private void TrackEffectCamera()
        {
            if (FF9StateSystem.Battle.FF9Battle.btl_phase != FF9StateBattleSystem.PHASE_NORMAL) return;
            SFXData effect = SFXData.LoadCur;
            if (effect == null || !effect.useCamera)
            {
                if (SFXDataMesh.Runtime.RenderingCount == 0)
                {
                    nativeIdleView = NativeView;
                    hasNativeIdle = true;
                }
                cameraEffect = null;
                return;
            }
            if (!ReferenceEquals(cameraEffect, effect))
            {
                cameraEffect = effect;
                effectMovedCamera = effectReturnedToIdle = false;
            }
            if (!hasNativeIdle || SFXDataMesh.Runtime.RenderingCount == 0 || effectReturnedToIdle) return;
            // The native plugin can restore its default shot before the effect renderer retires.
            for (Int32 row = 0; row < 3; row++)
                for (Int32 column = 0; column < 4; column++)
                    if (Mathf.Abs(NativeView[row, column] - nativeIdleView[row, column]) > (column == 3 ? 2f : 0.002f))
                    {
                        effectMovedCamera = true;
                        return;
                    }
            effectReturnedToIdle = effectMovedCamera;
        }

        internal static Boolean IsCurrentFrameSnapshot(Boolean allowed, Int32 preparedFrame, Int32 currentFrame)
        {
            return allowed && preparedFrame == currentFrame;
        }
        internal Boolean TryGetFrameCamera(Camera worldCamera, out Matrix4x4 view, out Matrix4x4 projection)
        {
            view = frameView; projection = frameProjection;
            return IsCurrentFrameSnapshot(frameAllowed, frameCameraNumber, Time.frameCount) && worldCamera == cameraComponent &&
                Settings != null && Settings.Enabled && !OtherEditorOwnsCamera();
        }
        internal static Boolean IsNativeTailTransfer(Boolean displayApplied, Boolean nativeShot, Boolean returnedToIdle, Boolean sameEffect)
        {
            return displayApplied && !nativeShot && returnedToIdle && sameEffect;
        }
        internal Boolean IsNativeTailHandoff(SFXDataMesh.Runtime effect)
        {
            return IsNativeTailTransfer(applied, nativeSfxFrame, effectReturnedToIdle,
                cameraEffect != null && ReferenceEquals(cameraEffect.mesh, effect));
        }
        internal Boolean TryProjectStackTarget(BTL_DATA[] targets, out Vector3 screen)
        {
            return TryProjectRawTarget(targets, applied, out screen);
        }

        internal Boolean TryProjectRawTarget(BTL_DATA[] targets, Boolean edited, out Vector3 screen, Boolean casterRoot = false)
        {
            screen = Vector3.zero;
            if (cameraComponent == null || (edited && !applied) || psxWidth == null || psxHeight == null)
                return false;
            Vector3 point = Vector3.zero;
            Int32 count = 0;
            foreach (BTL_DATA target in targets)
            {
                if (target == null || target.gameObject == null) continue;
                Transform bone = !casterRoot && targets.Length == 1
                    ? target.gameObject.transform.GetChildByName("bone" + target.tar_bone.ToString("D3")) : null;
                point += casterRoot ? target.pos : bone != null ? bone.position : targets.Length == 1 ? target.pos : target.base_pos;
                count++;
            }
            if (count == 0) return false;
            point /= count;
            Matrix4x4 view = edited ? frameView : applied ? savedView : cameraComponent.worldToCameraMatrix;
            Matrix4x4 projection = edited ? frameProjection : applied ? savedProjection : cameraComponent.projectionMatrix;
            Vector4 clip = projection * (view * new Vector4(point.x, point.y, point.z, 1f));
            if (clip.w <= 0.001f) return false;
            screen.x = (1f + clip.x / clip.w) * Convert.ToSingle(psxWidth.GetValue(null)) * 0.5f;
            screen.y = (1f - clip.y / clip.w) * Convert.ToSingle(psxHeight.GetValue(null)) * 0.5f;
            return true;
        }

        internal Boolean TryGetEffectOffset(BTL_DATA target, out Vector3 offset, out Vector3 nativeScreen, Boolean weaponBone = false)
        {
            offset = nativeScreen = Vector3.zero;
            if (!applied || target.gameObject == null || psxWidth == null || psxHeight == null) return false;
            Int32 boneId = weaponBone ? target.weapon_bone : target.tar_bone;
            Transform bone = boneId < 0 ? null : target.gameObject.transform.GetChildByName("bone" + boneId.ToString("D3"));
            Vector3 point = bone == null ? target.pos : bone.position;
            Vector4 world = new Vector4(point.x, point.y, point.z, 1f);
            Vector4 native = savedProjection * (savedView * world);
            Vector4 edited = frameProjection * (frameView * world);
            if (native.w <= 0.001f || edited.w <= 0.001f) return false;
            Single halfWidth = Convert.ToSingle(psxWidth.GetValue(null)) * 0.5f;
            Single halfHeight = Convert.ToSingle(psxHeight.GetValue(null)) * 0.5f;
            nativeScreen.x = (1f + native.x / native.w) * halfWidth;
            nativeScreen.y = (1f - native.y / native.w) * halfHeight;
            offset.x = (edited.x / edited.w - native.x / native.w) * halfWidth;
            offset.y = (native.y / native.w - edited.y / edited.w) * halfHeight;
            return true;
        }

        // Physical hit quads are already projected PSX screen geometry. Moving
        // their anchor alone leaves vanilla pixel sizes behind when zooming out.
        internal Single PhysicalHitScreenScale(BTL_DATA target, Boolean casterRoot = false)
        {
            if (!applied || cameraComponent == null || target == null || target.gameObject == null) return 1f;
            Transform bone = casterRoot ? null : target.gameObject.transform.GetChildByName("bone" + target.tar_bone.ToString("D3"));
            Vector3 point = bone == null ? target.pos : bone.position;
            Vector4 world = new Vector4(point.x, point.y, point.z, 1f);
            Single nativeDepth = (savedProjection * (savedView * world)).w;
            Single editedDepth = (frameProjection * (frameView * world)).w;
            return PhysicalHitProjectionScale(nativeDepth, editedDepth, savedProjection.m11, frameProjection.m11);
        }

        internal static Single PhysicalHitProjectionScale(Single nativeDepth, Single editedDepth, Single nativeY, Single editedY)
        {
            if (nativeDepth <= 0.001f || editedDepth <= 0.001f || Math.Abs(nativeY) <= 0.001f) return 1f;
            Single ratio = Math.Abs(editedY / nativeY) * nativeDepth / editedDepth;
            return Single.IsNaN(ratio) || Single.IsInfinity(ratio) || ratio <= 0f ? 1f : ratio;
        }
        private void OnPreCull()
        {
            Restore();
            // No native SFX commands, battle VM queries, UI updates or callbacks here.
            if (frameAllowed && Settings != null && Settings.Enabled && cameraComponent != null && (CanOverrideCamera() || (nativeSfxFrame && CanZoomNativeSfx())))
            {
                ApplyMatrices();
                FaceBattleBillboards();
            }
        }
        private void FaceBattleBillboards()
        {
            BattleSPSSystem system = HonoluluBattleMain.battleSPS;
            if (system == null) return;
            Matrix4x4 camera = frameView.inverse;
            Vector3 forward = camera.MultiplyVector(Vector3.forward);
            Vector3 right = camera.MultiplyVector(Vector3.right);
            if (battleSpsUtility?.GetValue(system) is CommonSPSSystem utility)
                foreach (SPSEffect sprite in utility.SpsList)
                    if (sprite != null && sprite.rotMode == 0 && sprite.meshRenderer != null && sprite.meshRenderer.enabled)
                        FaceBillboard(sprite.transform, forward, Quaternion.Euler(0f, 0f, sprite.rot.z) * right);
            if (battleShpEffects?.GetValue(system) is List<SHPEffect> shapes)
                foreach (SHPEffect sprite in shapes)
                    if (sprite != null && sprite.shpId >= 0 && (sprite.attr & SPSConst.ATTR_VISIBLE) != 0)
                        FaceBillboard(sprite.transform, forward, right);
        }

        private void FaceBillboard(Transform sprite, Vector3 forward, Vector3 right)
        {
            billboardRotations.Add(new KeyValuePair<Transform, Quaternion>(sprite, sprite.rotation));
            sprite.rotation = Quaternion.LookRotation(forward, -Vector3.Cross(forward, right));
        }

        private void ApplyMatrices()
        {
            savedView = cameraComponent.worldToCameraMatrix; savedProjection = cameraComponent.projectionMatrix;
            cameraComponent.worldToCameraMatrix = frameView;
            Matrix4x4 projection = savedProjection;
            Single scale = Mathf.Tan(Fov(savedProjection) * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(frameFov * Mathf.Deg2Rad * 0.5f);
            projection.m00 *= scale; projection.m11 *= scale;
            cameraComponent.projectionMatrix = projection; frameProjection = projection; frameCameraNumber = Time.frameCount; applied = true;
        }
        private void OnPostRender()
        {
            if (!BattleLayoutRuntime.RestoresAfterNativeEffects) Restore();
        }
        internal void Restore()
        {
            foreach (var entry in billboardRotations)
                if (entry.Key != null) entry.Key.rotation = entry.Value;
            billboardRotations.Clear();
            if (!applied || cameraComponent == null) return;
            cameraComponent.worldToCameraMatrix = savedView; cameraComponent.projectionMatrix = savedProjection; applied = false;
        }

        internal Boolean TryGetNativeSize(BTL_DATA actor, out Vector3 size)
        {
            size = Vector3.zero;
            if (!scaledActors.TryGetValue(actor, out ModelScaleState state) || GeometrySize(actor) != state.Applied)
                return false;
            size = state.Native;
            return true;
        }

        private void UpdateStageScale()
        {
            if (StageScale == 1f)
            {
                RestoreStageScale();
                return;
            }
            if (!BattleLayoutRuntime.SceneAvailable()) return;
            var map = FF9StateSystem.Battle.FF9Battle.map;
            if (map == null) return;
            ScaleStageObject(map.btlBGPtr);
            if (map.btlBGObjAnim != null)
                foreach (GameObject prop in map.btlBGObjAnim)
                    ScaleStageObject(prop);
        }

        private void ScaleStageObject(GameObject model)
        {
            if (model == null) return;
            Transform item = model.transform;
            if (!stageTransforms.TryGetValue(item, out StageTransform state))
            {
                state = new StageTransform { Position = item.localPosition, Scale = item.localScale };
                stageTransforms.Add(item, state);
            }
            else
            {
                if (!item.localPosition.Equals(state.AppliedPosition)) state.Position = item.localPosition;
                if (!item.localScale.Equals(state.AppliedScale)) state.Scale = item.localScale;
            }
            // All stage models share the battle background origin. Their UVs follow the meshes.
            item.localPosition = state.AppliedPosition = state.Position * StageScale;
            item.localScale = state.AppliedScale = state.Scale * StageScale;
        }

        private void RestoreStageScale()
        {
            foreach (KeyValuePair<Transform, StageTransform> pair in stageTransforms)
            {
                Transform item = pair.Key;
                if (item == null) continue;
                StageTransform state = pair.Value;
                if (item.localPosition.Equals(state.AppliedPosition)) item.localPosition = state.Position;
                if (item.localScale.Equals(state.AppliedScale)) item.localScale = state.Scale;
            }
            stageTransforms.Clear();
        }

        private static Vector3 GeometrySize(BTL_DATA actor)
        {
            return new Vector3(actor.geo_scale_x, actor.geo_scale_y, actor.geo_scale_z);
        }

        private static void WriteGeometrySize(BTL_DATA actor, Vector3 size)
        {
            geo.geoScaleSetXYZ(actor, Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y), Mathf.RoundToInt(size.z));
            Vector3 rendered = actor.gameObject.transform.localScale;
            actor._smoothUpdateScaleActual = rendered;
            actor._smoothUpdateScalePrevious = rendered;
        }

        internal void RefreshChangedModelScale(BTL_DATA actor)
        {
            ModelScaleState state;
            if (actor != null && actor.gameObject != null && scaledActors.TryGetValue(actor, out state) &&
                GeometrySize(actor) == state.Applied)
                WriteGeometrySize(actor, state.Applied);
        }
        private void UpdateModelScales()
        {
            if (ModelUnits == null)
            {
                RestoreModelScales();
                return;
            }
            foreach (BTL_DATA actor in BattleLayoutRuntime.Units())
            {
                if (actor.gameObject == null || actor.bi.slave != 0 || actor.bi.player != 0 && actor.originalGo == null) continue;
                Single scale = 1f;
                foreach (LayoutUnit unit in ModelUnits)
                    if (unit.Party == (actor.bi.player != 0) && unit.Slot == BattleLayoutRuntime.Slot(actor))
                    {
                        scale = unit.Scale;
                        break;
                    }
                Vector3 current = GeometrySize(actor);
                if (current.x == 0 || current.y == 0 || current.z == 0) continue;
                ModelScaleState state;
                if (!scaledActors.TryGetValue(actor, out state))
                {
                    if (scale == 1f) continue;
                    state = new ModelScaleState { Native = current };
                    scaledActors.Add(actor, state);
                }
                else if (current != state.Applied) state.Native = current;

                Vector3 desired = state.Native * scale;
                if (desired != current) WriteGeometrySize(actor, desired);
                state.Applied = GeometrySize(actor);
                if (scale == 1f) scaledActors.Remove(actor);
            }
        }

        private void RestoreModelScales()
        {
            foreach (KeyValuePair<BTL_DATA, ModelScaleState> pair in scaledActors)
                if (pair.Key.gameObject != null && (pair.Key.bi.player == 0 || pair.Key.originalGo != null) && GeometrySize(pair.Key) == pair.Value.Applied)
                    WriteGeometrySize(pair.Key, pair.Value.Native);
            scaledActors.Clear();
        }

    }
}
