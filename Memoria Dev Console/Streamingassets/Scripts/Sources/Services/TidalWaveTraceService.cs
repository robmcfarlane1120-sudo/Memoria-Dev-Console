using System;
using System.Collections.Generic;
using Memoria.Data;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public static class TidalWaveTraceService
    {
        private static Boolean _installed;

        public static void Install()
        {
            if (_installed)
                return;

            _installed = true;

            GameObject host = new GameObject("MDC SFX Path Trace");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<SFXPathTraceBehaviour>();

            GameLoopManager.Update -= EarlyTick;
            GameLoopManager.Update += EarlyTick;

            Log.Message("[MDC][SFXTRACE] Multi-path SFX tracer installed.");
        }

        private static Int32 _lastEarlyCurrent = Int32.MinValue;
        private static Int32 _lastEarlyLoadCur = Int32.MinValue;

        private static void EarlyTick()
        {
            TraceStaticState("EARLY", ref _lastEarlyCurrent, ref _lastEarlyLoadCur);
        }

        private static void TraceStaticState(String stage, ref Int32 lastCurrent, ref Int32 lastLoadCur)
        {
            Int32 current = (Int32)SFX.currentEffectID;

            if (current != lastCurrent)
            {
                lastCurrent = current;
                Log.Message(
                    "[MDC][SFXTRACE][" + stage + "] currentEffectID=" +
                    current + " (" + SFX.currentEffectID + ") frame=" + SFX.frameIndex
                );
            }

            SFXData loadCur = SFXData.LoadCur;
            Int32 loadCurId = loadCur == null ? -999999 : (Int32)loadCur.id;

            if (loadCurId != lastLoadCur)
            {
                lastLoadCur = loadCurId;

                if (loadCur == null)
                {
                    Log.Message("[MDC][SFXTRACE][" + stage + "] LoadCur=<null>");
                }
                else
                {
                    Log.Message(
                        "[MDC][SFXTRACE][" + stage + "] LoadCur=" +
                        loadCurId + " (" + loadCur.id + ")" +
                        " loaded=" + loadCur.loadHasEnded +
                        " mesh=" + (loadCur.mesh == null ? "<null>" : loadCur.mesh.GetType().FullName)
                    );
                }
            }
        }

        private sealed class SFXPathTraceBehaviour : MonoBehaviour
        {
            private Int32 _lastUpdateCurrent = Int32.MinValue;
            private Int32 _lastUpdateLoadCur = Int32.MinValue;

            private Int32 _lastLateCurrent = Int32.MinValue;
            private Int32 _lastLateLoadCur = Int32.MinValue;

            private Int32 _lastActionCount = -1;
            private Int32 _lastEventCount = -1;

            private void Update()
            {
                TraceStaticState("UPDATE", ref _lastUpdateCurrent, ref _lastUpdateLoadCur);
                TraceCollections("UPDATE");
            }

            private void LateUpdate()
            {
                TraceStaticState("LATE", ref _lastLateCurrent, ref _lastLateLoadCur);
                TraceCollections("LATE");
            }

            private void TraceCollections(String stage)
            {
                Int32 actionCount = UnifiedBattleSequencer.runningActions.Count;

                if (actionCount != _lastActionCount)
                {
                    _lastActionCount = actionCount;
                    Log.Message(
                        "[MDC][SFXTRACE][" + stage + "] runningActions=" + actionCount
                    );
                }

                for (Int32 a = 0; a < actionCount; a++)
                {
                    UnifiedBattleSequencer.BattleAction action =
                        UnifiedBattleSequencer.runningActions[a];

                    if (action == null || action.sfxList == null)
                        continue;

                    for (Int32 i = 0; i < action.sfxList.Count; i++)
                    {
                        SFXData sfx = action.sfxList[i];

                        if (sfx == null)
                            continue;

                        if ((Int32)sfx.id == 434)
                        {
                            Log.Message(
                                "[MDC][SFXTRACE][" + stage + "] FOUND 434 in action " +
                                a + " sfx " + i +
                                " loaded=" + sfx.loadHasEnded +
                                " running=" + (sfx.runningSFX == null ? 0 : sfx.runningSFX.Count) +
                                " mesh=" + (sfx.mesh == null ? "<null>" : sfx.mesh.GetType().FullName)
                            );
                        }
                    }
                }

                Int32 eventCount = SFXData.EventSFX.Count;

                if (eventCount != _lastEventCount)
                {
                    _lastEventCount = eventCount;
                    Log.Message(
                        "[MDC][SFXTRACE][" + stage + "] EventSFX.Count=" + eventCount
                    );
                }

                foreach (KeyValuePair<Int32, SFXData> pair in SFXData.EventSFX)
                {
                    if (pair.Value != null && (Int32)pair.Value.id == 434)
                    {
                        Log.Message(
                            "[MDC][SFXTRACE][" + stage + "] FOUND 434 in EventSFX key=" +
                            pair.Key
                        );
                    }
                }
            }
        }
    }
}
