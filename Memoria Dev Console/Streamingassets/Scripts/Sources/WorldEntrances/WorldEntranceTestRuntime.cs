using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Assets.Scripts.Common;
using UnityEngine;

namespace Memoria.DevConsole.WorldEntrances
{
    // Opt-in research prototype. No Harmony, event bytecode, or AF overloads.
    public sealed class WorldEntrance
    {
        public String Id;
        // Absolute WMActor.RealPosition coordinates, in Unity world units.
        public Single WorldX, WorldZ;
        public Single InteractionRadius = 4f;
        public Int16 DestinationField;
        // A destination SCRIPT parameter, not a generic engine spawn index.
        public Int32 DestinationEntrance = 9999;
        public Int16 WorldMap;
        public String Prompt = "Enter";
        public String EntranceTitle = "";
        public Boolean DeadPepperGate;
        public Boolean IsEditorTest;
        public Func<Boolean> IsEnabled;
    }

    public static class WorldEntranceRegistry
    {
        private static readonly Dictionary<String, WorldEntrance> Entries = new Dictionary<String, WorldEntrance>();

        public static void Register(WorldEntrance entrance)
        {
            if (entrance == null || String.IsNullOrEmpty(entrance.Id))
                throw new ArgumentException("An entrance needs an ID.");
            if (!Finite(entrance.WorldX) || !Finite(entrance.WorldZ) || !Finite(entrance.InteractionRadius) || entrance.InteractionRadius <= 0)
                throw new ArgumentException("Entrance coordinates and radius must be finite; radius must be positive.");
            if ((entrance.WorldMap != 0 && (entrance.WorldMap < 9000 || entrance.WorldMap > 9012)) || entrance.DestinationField < 0 || entrance.DestinationField >= 9000 || entrance.DestinationEntrance < 0 || entrance.DestinationEntrance > UInt16.MaxValue)
                throw new ArgumentException("Invalid world, field or entrance parameter.");
            if ((entrance.EntranceTitle ?? "").Length > 80 || (entrance.EntranceTitle ?? "").IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0)
                throw new ArgumentException("Entrance title must be plain single-line text, at most 80 characters.");
            if (Entries.ContainsKey(entrance.Id)) throw new ArgumentException("Duplicate entrance ID: " + entrance.Id);
            // Own a copy: later caller mutations cannot invalidate registration.
            Entries.Add(entrance.Id, new WorldEntrance { Id = entrance.Id, WorldX = entrance.WorldX, WorldZ = entrance.WorldZ,
                InteractionRadius = entrance.InteractionRadius, WorldMap = entrance.WorldMap, DestinationField = entrance.DestinationField,
                DestinationEntrance = entrance.DestinationEntrance, Prompt = entrance.Prompt, EntranceTitle = entrance.EntranceTitle, DeadPepperGate = entrance.DeadPepperGate, IsEditorTest = entrance.IsEditorTest, IsEnabled = entrance.IsEnabled });
        }

        public static Boolean Unregister(String id) { return Entries.Remove(id); }
        internal static void ReplaceManaged(IEnumerable<String> oldIds, IEnumerable<WorldEntrance> replacements)
        {
            var previous = new Dictionary<String, WorldEntrance>(Entries);
            try { foreach (String id in oldIds) Entries.Remove(id); foreach (WorldEntrance e in replacements) Register(e); }
            catch { Entries.Clear(); foreach (var pair in previous) Entries.Add(pair.Key, pair.Value); throw; }
        }
        private static Boolean Finite(Single x) { return !Single.IsNaN(x) && !Single.IsInfinity(x); }

        public static Single WrappedDelta(Single a, Single b, Single period)
        {
            Single delta = (a - b) % period;
            if (delta > period / 2) delta -= period;
            if (delta < -period / 2) delta += period;
            return delta;
        }

        internal static Boolean Owns(WorldEntrance entrance) { WorldEntrance current; return Entries.TryGetValue(entrance.Id, out current) && ReferenceEquals(current, entrance); }
        internal static WorldEntrance Find(Single x, Single z, Int16 world, Boolean includeLocked = false)
        {
            WorldEntrance best = null;
            Single bestDistance = Single.MaxValue;
            foreach (WorldEntrance e in Entries.Values)
            {
                if ((e.WorldMap != 0 && e.WorldMap != world) || (e.IsEnabled != null && !e.IsEnabled())) continue;
                if (!includeLocked && e.DeadPepperGate && !DeadPepperGate.IsUnlocked(e.Id)) continue;
                Single dx = WrappedDelta(x, e.WorldX, 1536f), dz = WrappedDelta(z, e.WorldZ, 1280f);
                Single distance = dx * dx + dz * dz;
                if (distance > e.InteractionRadius * e.InteractionRadius) continue;
                if (distance < bestDistance || (distance == bestDistance && best != null && String.CompareOrdinal(e.Id, best.Id) < 0))
                { best = e; bestDistance = distance; }
            }
            return best;
        }
    }

    // Observes native pepper use; never consumes items or replaces Choco's scripts.
    internal static class DeadPepperGate
    {
        private const Int32 SaveKey = 0x4D445047; // MDPG, separate from entrance-return state.
        private static WorldEntrance candidate;
        private static System.Object world;
        private static System.Object saveState;
        private static Int32 previousCount = -1;
        private static Single deadline;
        private static Boolean spent, effectFinished, sawPepperAnimation, sawNativeBusy;
        private static Single stableSince = -1f;
        private static SPSEffect effect;
        internal static String Hint;

        private static Int32[] Fingerprint(String id)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                Byte[] digest = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id));
                Int32[] words = new Int32[8];
                for (Int32 i = 0; i < words.Length; i++)
                    words[i] = digest[i * 4] | digest[i * 4 + 1] << 8 | digest[i * 4 + 2] << 16 | digest[i * 4 + 3] << 24;
                return words;
            }
        }

        internal static Boolean IsUnlocked(String id)
        {
            Dictionary<Int32, Int32> data;
            if (!FF9StateSystem.EventState.gScriptDictionary.TryGetValue(SaveKey, out data)) return false;
            return Contains(data, Fingerprint(id));
        }

        private static Boolean Contains(Dictionary<Int32, Int32> data, Int32[] digest)
        {
            Int32 schema, count;
            if (!data.TryGetValue(-1, out schema) || schema != 1 || !data.TryGetValue(-2, out count) || count < 0 || count > 4096) return false;
            for (Int32 row = 0; row < count; row++)
            {
                Boolean match = true;
                for (Int32 i = 0; i < 8; i++)
                {
                    Int32 value;
                    if (!data.TryGetValue(row * 8 + i, out value) || value != digest[i]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }

        private static void Unlock(String id)
        {
            var saves = FF9StateSystem.EventState.gScriptDictionary;
            Dictionary<Int32, Int32> data;
            if (!saves.TryGetValue(SaveKey, out data))
            {
                data = new Dictionary<Int32, Int32> { { -1, 1 }, { -2, 0 } };
                saves.Add(SaveKey, data);
            }
            Int32 schema, count;
            if (!data.TryGetValue(-1, out schema) || schema != 1 || !data.TryGetValue(-2, out count) || count < 0 || count >= 4096)
                throw new InvalidOperationException("Dead Pepper gate save namespace is occupied or invalid.");
            Int32[] digest = Fingerprint(id);
            if (Contains(data, digest)) return;
            for (Int32 i = 0; i < 8; i++) data.Add(count * 8 + i, digest[i]);
            data[-2] = count + 1;
            Memoria.Prime.Log.Message("[World Entrance] Dead Pepper gate unlocked: " + id);
        }

        private static void Reset()
        { candidate = null; world = null; saveState = null; previousCount = -1; spent = false; effectFinished = false; effect = null; sawPepperAnimation = false; sawNativeBusy = false; stableSince = -1f; }

        private static Boolean PepperEffect(SPSEffect sps)
        {
            return sps != null && sps.spsBin != null &&
                (sps.worldSpsId == SPSConst.WorldSPSEffect.CHOCOBO_PEPPER_GROUND ||
                 sps.worldSpsId == SPSConst.WorldSPSEffect.CHOCOBO_PEPPER_WATER ||
                 sps.worldSpsId == SPSConst.WorldSPSEffect.CHOCOBO_PEPPER_SKY);
        }

        internal static void Update()
        {
            Hint = null;
            try { Observe(); }
            catch (Exception ex) { Reset(); Memoria.Prime.Log.Warning("[World Entrance] Dead Pepper gate: " + ex.Message); }
        }

        private static Boolean NativePepperAnimation(WMActor actor)
        {
            if (actor.originalActor == null) return false;
            String name = FF9DBAll.AnimationDB.GetValue(actor.originalActor.anim) ?? "";
            return name.StartsWith("ANH_SUB_W0_003_", StringComparison.Ordinal) &&
                (name.Contains("EAT_CHO") || name.Contains("CRASH_CHO") || name.Contains("DIVE_CHO") ||
                 name.Contains("ROCKET_CHO") || name.Contains("COMEOUT_CHO"));
        }

        private static void Observe()
        {
            var system = PersistenSingleton<FF9StateSystem>.Instance;
            if (system == null || system.mode != 3 || ff9.world == null || WorldEntranceTransition.IsBusy)
            { Reset(); return; }
            if (candidate != null && (!ReferenceEquals(world, ff9.world) ||
                !ReferenceEquals(saveState, FF9StateSystem.EventState.gScriptDictionary) ||
                Time.realtimeSinceStartup > deadline || !WorldEntranceRegistry.Owns(candidate))) Reset();

            var actor = ff9.GetControlChar();
            if (actor == null) { Reset(); return; }
            Int32 count = ff9item.FF9Item_GetCount(Memoria.Data.RegularItem.DeadPepper);
            Boolean ready = WorldEntranceController.CaptureBlockReason(false) == null;
            Boolean pepperAnimation = NativePepperAnimation(actor);
            Int16 map = FF9StateSystem.Common.FF9.wldMapNo;
            if (candidate != null)
            {
                // Latch the native use BEFORE its scripted movement leaves the radius.
                if (pepperAnimation && !sawPepperAnimation)
                {
                    sawPepperAnimation = true;
                    Memoria.Prime.Log.Message("[World Entrance] Dead Pepper animation started: " + candidate.Id);
                }
                if (previousCount >= 0 && count < previousCount && !spent)
                {
                    spent = true;
                    Memoria.Prime.Log.Message("[World Entrance] Dead Pepper consumed: " + candidate.Id);
                }
                Boolean usingPepper = spent || sawPepperAnimation;
                if (usingPepper && !ready) sawNativeBusy = true;
                Single dx = WorldEntranceRegistry.WrappedDelta(actor.RealPosition.x, candidate.WorldX, 1536f);
                Single dz = WorldEntranceRegistry.WrappedDelta(actor.RealPosition.z, candidate.WorldZ, 1280f);
                if ((candidate.WorldMap != 0 && map != candidate.WorldMap) ||
                    (!usingPepper && dx * dx + dz * dz > candidate.InteractionRadius * candidate.InteractionRadius) ||
                    (candidate.IsEnabled != null && !candidate.IsEnabled())) { Reset(); return; }
                if (effect != null && (effect.spsBin == null || effect.curFrame >= effect.frameCount)) effectFinished = true;
                if (effect == null)
                {
                    foreach (SPSEffect sps in ff9.world.WorldSPSSystem.SpsList)
                    {
                        if (!PepperEffect(sps)) continue;
                        Single ex = WorldEntranceRegistry.WrappedDelta(sps.pos.x, actor.RealPosition.x, 1536f);
                        Single ez = WorldEntranceRegistry.WrappedDelta(sps.pos.z, actor.RealPosition.z, 1280f);
                        if (ex * ex + ez * ez <= 64f) { effect = sps; break; }
                    }
                }
                // The SPS may start and finish between observer ticks. The native
                // pepper animation / control handoff is also a completion signal.
                Boolean complete = spent && ready && !pepperAnimation &&
                    (effectFinished || sawPepperAnimation || sawNativeBusy);
                if (!complete) stableSince = -1f;
                else if (stableSince < 0f) stableSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - stableSince >= 0.25f)
                {
                    Unlock(candidate.Id);
                    if (candidate.IsEditorTest) WorldEntranceController.ShowGateDiscovery();
                    Reset(); return;
                }
                if (ready && !usingPepper && effect == null) deadline = Time.realtimeSinceStartup + 60f;
            }
            if (candidate == null && ready && ff9.w_moveCHRControl_No >= 1 && ff9.w_moveCHRControl_No <= 6)
            {
                WorldEntrance found = WorldEntranceRegistry.Find(actor.RealPosition.x, actor.RealPosition.z, map, true);
                if (found != null && found.DeadPepperGate && !IsUnlocked(found.Id))
                {
                    candidate = found; world = ff9.world; saveState = FF9StateSystem.EventState.gScriptDictionary;
                    deadline = Time.realtimeSinceStartup + 60f;
                }
            }
            previousCount = count;
            if (candidate != null && candidate.IsEditorTest && ready && !spent && !sawPepperAnimation)
                Hint = "Use a Dead Pepper here";
        }

    }

    public static class WorldEntranceTransition
    {
        // MDWE. Save-serialized by Memoria's gScriptDictionary. Versioned and
        // collision checked; this experimental namespace must be allocated upstream.
        internal const Int32 TicketKey = 0x4D445745;
        private const Int32 Schema = 1;
        private const UInt32 SuspendCommon = 0x100;
        private const UInt32 WorldToField = 0x1000;
        private const UInt32 FieldToWorld = 2;
        private static Int32 suspendedForMode;
        private static Int32 arrivalMode;
        // General_FieldEntrance is signed Int16 in field scripts: -2 stores
        // 0xFFFE, read as 65534 by EventState.FieldEntrance. This is a request
        // only while this runtime owns a valid return ticket.
        public const Int32 FieldScriptReturnSignal = 65534;
        internal static Boolean IsBusy { get { return arrivalMode != 0; } }
        internal static String Progress { get { return suspendedForMode != 0 ? "Fading / changing scene..." : "Waiting for destination control..."; } }

        internal static void ReleaseOwnedSuspension()
        {
            if (!IsBusy) return;
            String expectedScene = arrivalMode == 1 ? "FieldMap" : "WorldMap";
            var scene = PersistenSingleton<SceneDirector>.Instance;
            if (scene.CurrentScene != expectedScene) return;
            if (suspendedForMode != 0)
            {
                FF9StateSystem.Common.FF9.attr &= ~SuspendCommon;
                suspendedForMode = 0;
            }
            if (!scene.IsReady || scene.IsFading) return;
            if (!(arrivalMode == 1 ? WorldEntranceController.FieldReady() : WorldEntranceController.WorldReady())) return;
            Memoria.Prime.Log.Message("[MDC World Entrance] Arrival ready: " + expectedScene);
            arrivalMode = 0;
        }

        public const Int32 ExitFlagDictionary = 988;
        public const Int32 ExitFlagEntry = 0;
        private const Int32 ExitOwnerEntry = 1;

        private static void ArmDictionaryExit()
        {
            Dictionary<Int32, Int32> flags;
            if (!FF9StateSystem.EventState.gScriptDictionary.TryGetValue(ExitFlagDictionary, out flags))
            {
                flags = new Dictionary<Int32, Int32>();
                FF9StateSystem.EventState.gScriptDictionary.Add(ExitFlagDictionary, flags);
            }
            flags[ExitFlagEntry] = 1;
            flags[ExitOwnerEntry] = TicketKey;
        }

        // Both the flag and saved return ticket persist through Memoria's save dictionary.
        // Missing/cleared dictionaries are not requests; only an explicit zero with our owner is.
        internal static Boolean IsDictionaryExitRequest(Dictionary<Int32, Int32> flags, Int32 owner)
        {
            Int32 value, actualOwner;
            return flags != null && flags.TryGetValue(ExitFlagEntry, out value) && value == 0 &&
                flags.TryGetValue(ExitOwnerEntry, out actualOwner) && actualOwner == owner;
        }

        private static Boolean DictionaryExitRequested()
        {
            Dictionary<Int32, Int32> flags;
            return FF9StateSystem.EventState.gScriptDictionary.TryGetValue(ExitFlagDictionary, out flags) && IsDictionaryExitRequest(flags, TicketKey);
        }

        private static void ClearDictionaryExit()
        {
            Dictionary<Int32, Int32> flags;
            Int32 owner;
            if (FF9StateSystem.EventState.gScriptDictionary.TryGetValue(ExitFlagDictionary, out flags) &&
                flags.TryGetValue(ExitOwnerEntry, out owner) && owner == TicketKey)
            {
                flags[ExitFlagEntry] = 0;
                flags.Remove(ExitOwnerEntry);
            }
        }

        internal static void AbandonTicket()
        {
            FF9StateSystem.EventState.gScriptDictionary.Remove(TicketKey);
            ClearDictionaryExit();
        }

        internal static Boolean HasTicket
        {
            get { Dictionary<Int32, Int32> ticket; return FF9StateSystem.EventState.gScriptDictionary.TryGetValue(TicketKey, out ticket) && ValidTicket(ticket); }
        }

        public static Boolean TryEnter(String id)
        {
            if (IsBusy || !WorldEntranceController.WorldReady() || FF9StateSystem.EventState.gScriptDictionary.ContainsKey(TicketKey)) return false;
            Vector3 p = ff9.GetControlChar().RealPosition;
            WorldEntrance e = WorldEntranceRegistry.Find(p.x, p.z, FF9StateSystem.Common.FF9.wldMapNo);
            if (e == null || e.Id != id || !FF9DBAll.EventDB.ContainsKey(e.DestinationField)) return false;
            Byte[] globals = FF9StateSystem.EventState.gEventGlobal;
            var ticket = new Dictionary<Int32, Int32> { { 0, Schema }, { 1, FF9StateSystem.Common.FF9.wldMapNo }, { 2, e.DestinationField },
                { 3, FF9StateSystem.EventState.ScenarioCounter } };
            // The stock on-foot position record: signed Int24 X at 64, Int16 Y
            // at 67, signed Int24 Z at 69, byte facing at 72. Refresh the three
            // coordinates explicitly rather than depending on the script's last tick.
            Byte[] position = new Byte[9];
            Array.Copy(globals, 64, position, 0, 9);
            WriteLittleEndian(position, 0, 3, (Int32)(p.x * 256f));
            WriteLittleEndian(position, 3, 2, (Int32)(p.y * 256f));
            WriteLittleEndian(position, 5, 3, (Int32)(p.z * 256f));
            for (Int32 i = 0; i < position.Length; i++) ticket[10 + i] = position[i];
            FF9StateSystem.EventState.gScriptDictionary.Add(TicketKey, ticket);
            ArmDictionaryExit();
            FF9StateSystem.EventState.FieldEntrance = e.DestinationEntrance;
            PrepareTransition(1, e.DestinationField);
            // WMScriptDirector consumes these flags on its next tick, performs
            // ff9ShutdownStateWorldMap and sound cleanup, then SceneDirector.Replace.
            return true;
        }

        public static Boolean TryReturn()
        { return TryReturnCore(false); }

        internal static Boolean PollFieldScriptReturn()
        {
            Boolean dictionaryRequest = DictionaryExitRequested();
            Boolean legacyRequest = FF9StateSystem.EventState.FieldEntrance == FieldScriptReturnSignal;
            if ((!dictionaryRequest && !legacyRequest) || IsBusy || PersistenSingleton<FF9StateSystem>.Instance.mode != 1 ||
                !HasTicket || !WorldEntranceController.FieldReadyForScript()) return false;
            if (!TryReturnCore(true))
            {
                if (legacyRequest) FF9StateSystem.EventState.FieldEntrance = 9999;
                if (dictionaryRequest) ArmDictionaryExit(); // consume refusal; avoid a per-frame retry loop
                Memoria.Prime.Log.Warning("[World Entrance] Script return refused: progression changed since entry; return flag rearmed.");
            }
            else Memoria.Prime.Log.Message("[World Entrance] Field-script return accepted" + (dictionaryRequest ? " via dictionary 988[0]: 1 -> 0." : " via legacy signal."));
            return true;
        }
        private static Boolean TryReturnCore(Boolean fieldScript)
        {
            if (IsBusy || !(fieldScript ? WorldEntranceController.FieldReadyForScript() : WorldEntranceController.FieldReady())) return false;
            Dictionary<Int32, Int32> t;
            if (!FF9StateSystem.EventState.gScriptDictionary.TryGetValue(TicketKey, out t) || !ValidTicket(t)) return false;
            if ((!fieldScript && FF9StateSystem.Common.FF9.fldMapNo != t[2]) || FF9StateSystem.EventState.ScenarioCounter != t[3]) return false;
            for (Int32 i = 0; i < 9; i++) FF9StateSystem.EventState.gEventGlobal[64 + i] = (Byte)t[10 + i];
            // Nonzero preserves the position record during world player Init.
            FF9StateSystem.EventState.FieldEntrance = 9999;
            PrepareTransition(3, t[1]);
            AbandonTicket();
            // HonoluluFieldMain performs the normal field shutdown and world load.
            return true;
        }

        internal static Boolean ValidTicket(Dictionary<Int32, Int32> t)
        {
            if (t == null || t.Count != 13) return false;
            for (Int32 i = 0; i < 4; i++) if (!t.ContainsKey(i)) return false;
            if (t[0] != Schema || t[1] < 9000 || t[1] > 9012 || t[2] < 0 || t[2] >= 9000 || t[3] < 0 || t[3] > UInt16.MaxValue) return false;
            for (Int32 i = 10; i < 19; i++) if (!t.ContainsKey(i) || t[i] < 0 || t[i] > 255) return false;
            return true;
        }

        private static void WriteLittleEndian(Byte[] bytes, Int32 start, Int32 count, Int32 value)
        { for (Int32 i = 0; i < count; i++) bytes[start + i] = (Byte)(value >> (i * 8)); }

        private static void PrepareTransition(Int32 destinationMode, Int32 destinationMap)
        {
            PersistenSingleton<EventEngine>.Instance.SetUserControl(false);
            PersistenSingleton<UIManager>.Instance.SetMenuControlEnable(false);
            PersistenSingleton<UIManager>.Instance.Dialogs.CloseAll(true);
            FF9StateSystem.Common.FF9.attr |= SuspendCommon;
            suspendedForMode = destinationMode;
            arrivalMode = destinationMode;
            Memoria.Prime.Log.Message("[MDC World Entrance] Request mode " + destinationMode + ", map " + destinationMap);
            WorldEntranceController.Instance.StartCoroutine(FadeAndCommit(destinationMode, destinationMap));
        }

        private static IEnumerator FadeAndCommit(Int32 destinationMode, Int32 destinationMap)
        {
            // Match the world's FadeFilter(Sub, white) choreography. The common
            // suspension bit stops the ordinary fade service, so this coroutine
            // advances the native shader fade at 20 Hz before raising jump flags.
            SceneDirector.FF9Wipe_FadeOutEx(12);
            for (Int32 frame = 0; frame < 12; frame++)
            {
                SceneDirector.ServiceFade();
                yield return new WaitForSeconds(0.05f);
            }
            PersistenSingleton<EventEngine>.Instance.SetNextMap(destinationMap);
            if (destinationMode == 1)
            {
                FF9StateSystem.World.FF9World.map.nextMode = 1;
                PersistenSingleton<FF9StateSystem>.Instance.attr |= WorldToField;
            }
            else
            {
                FF9StateSystem.Field.FF9Field.loc.map.nextMode = 3;
                PersistenSingleton<FF9StateSystem>.Instance.attr |= FieldToWorld;
            }
            String target = destinationMode == 1 ? "FieldMap" : "WorldMap";
            while (PersistenSingleton<SceneDirector>.Instance.CurrentScene != target)
                yield return null;
            // Give target initialization ownership of its normal scripts/control.
            // Request a native fade-in once the replacement has finished loading.
            while (!PersistenSingleton<SceneDirector>.Instance.IsReady) yield return null;
            SceneDirector.FF9Wipe_FadeInEx(12);
            // The transition still owns the common suspension bit here. Just as
            // with fade-out, advance the native fade explicitly so it cannot
            // remain black while waiting for destination control.
            for (Int32 frame = 0; frame < 12; frame++)
            {
                SceneDirector.ServiceFade();
                yield return new WaitForSeconds(0.05f);
            }
            SceneDirector.ServiceFade();
        }
    }

    public sealed class WorldEntranceController : MonoBehaviour
    {
        internal static WorldEntranceController Instance;
        internal static Boolean PrototypeControls { get { return false; } }
        private static readonly Action<BubbleUI.IconType> PollNativeIcon = ResolveIconPoll();
        private WorldEntrance nearby;
        private Boolean registered;
        private String status = "Ctrl+Shift+F11: place test entrance at your current on-foot position.";

        private Dialog entranceTitleDialog;
        private String shownEntranceTitle;
        private Single discoveryUntil;
        private Boolean discoverySoundPending;
        private Int32 TitleDialogId { get { return DialogManager.UIDialogId; } }

        internal static void ShowGateDiscovery()
        {
            if (Instance == null) return;
            Instance.discoveryUntil = Time.realtimeSinceStartup + 4f;
            Instance.discoverySoundPending = true;
        }

        private void UpdateEntranceTitle()
        {
            Boolean discovery = Time.realtimeSinceStartup < discoveryUntil &&
                !WorldEntranceTransition.IsBusy && CaptureBlockReason(false) == null;
            String text = discovery ? "A secret area has been found!" :
                DeadPepperGate.Hint ?? (nearby == null || WorldEntranceTransition.IsBusy || !WorldReady() ? null : nearby.EntranceTitle);
            if (String.IsNullOrEmpty((text ?? "").Trim())) { CloseEntranceTitle(); return; }
            if (entranceTitleDialog != null && entranceTitleDialog.Id == TitleDialogId && entranceTitleDialog.IsActive && shownEntranceTitle == text) return;
            CloseEntranceTitle();
            // The same pooled Dialog/NGUI renderer used by MES/MESN, with native
            // window skin, font, automatic text sizing and no input capture.
            // Keep the native UI ID: it bypasses field-dialog suppression on the world map.
            // Vector2.zero requests the selected corner; 10000/10000 is off-screen.
            String phrase = "[STRT=0,1][NANI][IMME]" + text + "[TIME=-1]";
            entranceTitleDialog = Singleton<DialogManager>.Instance.AttachDialog(phrase, 0, 1,
                Dialog.TailPosition.UpperCenter, Dialog.WindowStyle.WindowStyleNoTail,
                Vector2.zero, Dialog.CaptionType.None);
            if (entranceTitleDialog != null)
            {
                // Retain ownership through pool release; never find/close all UI-ID dialogs.
                Dialog owned = entranceTitleDialog;
                owned.AfterDialogHidden += choice => {
                    if (ReferenceEquals(entranceTitleDialog, owned)) { entranceTitleDialog = null; shownEntranceTitle = null; }
                };
                entranceTitleDialog.FlagButtonInh = true;
                shownEntranceTitle = text;
                if (discovery && discoverySoundPending)
                {
                    discoverySoundPending = false;
                    SoundLib.PlaySoundEffect(683, 1f, 0f, 1f);
                }
                Memoria.Prime.Log.Message("[World Entrance Title] Showing '" + text + "' at native upper-center (angular nameplate); ID=" + entranceTitleDialog.Id + ", active=" + entranceTitleDialog.IsActive);
            }
        }

        private void CloseEntranceTitle()
        {
            // Dialogs are pooled; never close a window the engine has recycled.
            if (entranceTitleDialog != null && entranceTitleDialog.Id == TitleDialogId && entranceTitleDialog.IsActive)
                entranceTitleDialog.ForceClose();
            entranceTitleDialog = null;
            shownEntranceTitle = null;
        }
        private void OnDisable() { CloseEntranceTitle(); }
        private void OnDestroy() { CloseEntranceTitle(); if (Instance == this) Instance = null; }

        private static Action<BubbleUI.IconType> ResolveIconPoll()
        {
            // EIcon is internal, but its public polling entry point is the same
            // one used by the FICON opcode. Resolve once; no bytecode/patching.
            Type type = typeof(EventEngine).Assembly.GetType("EIcon");
            MethodInfo method = type == null ? null : type.GetMethod("PollFIcon", BindingFlags.Public | BindingFlags.Static);
            return method == null ? null : (Action<BubbleUI.IconType>)Delegate.CreateDelegate(typeof(Action<BubbleUI.IconType>), method);
        }

        private void Awake() { Instance = this; }

        private void LateUpdate()
        {
            UpdateEntranceTitle();
            // Poll only; the event engine owns display/hiding on its next tick.
            // Never call ProcessFIcon twice or forcibly hide another event's icon.
            if (!WorldEntranceTransition.IsBusy && nearby != null && WorldReady() && PollNativeIcon != null)
                PollNativeIcon(BubbleUI.IconType.Exclamation);
        }

        private static Boolean Ready(Int32 mode, UIManager.UIState hud, Boolean requireControl = true)
        {
            var system = PersistenSingleton<FF9StateSystem>.Instance;
            var ui = PersistenSingleton<UIManager>.Instance;
            var scene = PersistenSingleton<SceneDirector>.Instance;
            UInt32 pendingMask = mode == 3 ? 0x3001u : 0xFu;
            return system.mode == mode && (system.attr & pendingMask) == 0 && (FF9StateSystem.Common.FF9.attr & 0x12F) == 0 &&
                ui.State == hud && !ui.IsPause && !ui.IsLoading && scene.IsReady && !scene.IsFading &&
                (!requireControl || PersistenSingleton<EventEngine>.Instance.GetUserControl());
        }

        // Position capture is independent of terrain event tags: a tag is not proof
        // of a functioning stock entrance in the active world script.
        internal static String CaptureBlockReason(Boolean requireOnFoot = true)
        {
            Int32 world = FF9StateSystem.Common.FF9.wldMapNo;
            if (world < 9000 || world > 9012 || world == 9001 || world == 9004 || world == 9006 || world == 9012) return "Not a free-roam world state.";
            if (!Ready(3, UIManager.UIState.WorldHUD)) return "World HUD/control unavailable, paused, loading, fading, or a transition is pending.";
            if (FF9StateSystem.World.IsBeeScene) return "Special world scene is active.";
            if (!ff9.w_blockReady) return "World terrain is still loading.";
            if (ff9.w_framePhase != 2) return "World movement phase is not ready.";
            if (ff9.w_naviMode == 2) return "World navigation is active.";
            if (ff9.w_isMogActive) return "World moogle is active.";
            if (!ff9.w_frameEventEnable) return "World events are disabled.";
            if (requireOnFoot && ff9.w_moveCHRControl_No != 0) return "Capture requires walking on foot.";
            if (FF9StateSystem.EventState.gEventGlobal[190] != 0 && (requireOnFoot || FF9StateSystem.EventState.gEventGlobal[190] > 6)) return "World transport state does not allow this interaction.";
            if (FF9StateSystem.World.FF9World.map.nextMode != 3) return "A world-to-scene transition is pending.";
            WMActor actor = ff9.GetControlChar();
            if (actor == null || actor.originalActor == null) return "No controlled world actor.";
            return null;
        }

        internal static Boolean WorldReady()
        {
            if (CaptureBlockReason() != null) return false;
            WMActor actor = ff9.GetControlChar();
            if (ff9.m_GetIDEvent(ff9.w_moveCHRStatus[actor.originalActor.index].id) == 0) return true;
            // Only a deliberately registered, enabled entrance for this world and
            // radius can override the event-tag veto. All transition/control gates remain.
            Vector3 p = actor.RealPosition;
            return WorldEntranceRegistry.Find(p.x, p.z, FF9StateSystem.Common.FF9.wldMapNo) != null;
        }
        internal static Boolean FieldReady()
        { return Ready(1, UIManager.UIState.FieldHUD) && FF9StateSystem.Field.FF9Field.loc.map.nextMode == 1; }

        internal static Boolean FieldReadyForScript()
        { return Ready(1, UIManager.UIState.FieldHUD, false) && FF9StateSystem.Field.FF9Field.loc.map.nextMode == 1; }

        private static Boolean Chord(KeyCode key)
        { return (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(key); }

        private void Update()
        {
            WorldEntranceTransition.ReleaseOwnedSuspension();
            DeadPepperGate.Update();
            nearby = null;
            if (WorldEntranceTransition.IsBusy) return;
            if (WorldEntranceTransition.PollFieldScriptReturn()) return;
            if (WorldReady())
            {
                // A normal exit abandons the prototype ticket. Only the explicit
                // return below promises exact origin-state restoration.
                if (WorldEntranceTransition.HasTicket) WorldEntranceTransition.AbandonTicket();
                Vector3 p = ff9.GetControlChar().RealPosition;
                if (PrototypeControls && Chord(KeyCode.F11))
                {
                    WorldEntranceRegistry.Unregister("MDC_TestEntrance");
                    WorldEntranceRegistry.Register(new WorldEntrance { Id = "MDC_TestEntrance", WorldX = p.x, WorldZ = p.z,
                        WorldMap = FF9StateSystem.Common.FF9.wldMapNo, DestinationField = 2800, DestinationEntrance = 9999,
                        Prompt = "Test entrance: Daguerreo", IsEnabled = () => FF9StateSystem.EventState.ScenarioCounter >= 8000 });
                    registered = true;
                    status = "Test entrance placed here (requires ScenarioCounter >= 8000).";
                }
                nearby = WorldEntranceRegistry.Find(p.x, p.z, FF9StateSystem.Common.FF9.wldMapNo);
                if (nearby != null && (Chord(KeyCode.F12) || PersistenSingleton<HonoInputManager>.Instance.IsInputDown(Control.Confirm)))
                    status = WorldEntranceTransition.TryEnter(nearby.Id) ? "Test entrance placed. Ctrl+Shift+F12 to enter." : "Entry refused.";
            }
            else if (FieldReady() && WorldEntranceTransition.HasTicket && Chord(KeyCode.F12))
                status = WorldEntranceTransition.TryReturn() ? "Returned entrance remains at its registered location." : "Return refused: destination or progression changed.";
        }

        // No debug HUD: native entrance prompts and the world editor UI remain available.
        // The entrance controller still handles interactions and transitions in Update().

    }
}
