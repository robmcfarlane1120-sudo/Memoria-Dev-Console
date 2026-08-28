using Memoria.Prime;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;

namespace Memoria.DevConsole
{
    public sealed class FieldStateService
    {
        private const Int32 MaxSnapshots = 10;
        private const Int32 StableFramesBeforeSnapshot = 12;

        private readonly List<FieldSnapshot> _snapshots = new List<FieldSnapshot>(MaxSnapshots);
        private FieldSnapshot _checkpoint;

        private Int32 _observedField = -1;
        private Int32 _stableFrames;
        private Int32 _suppressCaptureFrames;
        private Int32 _timelineCursor;
        private Boolean _restoringHistory;
        private Boolean _captureSuspended;
        private Int32 _automaticSnapshotLogCounter;

        public void SetCaptureSuspended(Boolean suspended)
        {
            _captureSuspended = suspended;
            if (suspended)
                _stableFrames = 0;
        }

        public Int32 Count { get { return _snapshots.Count; } }

        private static String DevConsoleDataDirectory
        {
            get
            {
                return Path.Combine(
                    Path.Combine(
                        Path.Combine(Environment.CurrentDirectory, "StreamingAssets"),
                        "Scripts"),
                    "DevConsole");
            }
        }

        private static String HistoryPath
        {
            get { return Path.Combine(DevConsoleDataDirectory, "field_history.json"); }
        }

        public void Initialize()
        {
            _observedField = GetCurrentField();
            _stableFrames = 0;
            _suppressCaptureFrames = 0;
            _timelineCursor = 0;
            _restoringHistory = false;

            LoadPersistentHistory();

            // Purge polluted snapshots produced by older passes. Memoria source identifies
            // field 70 as the NewGame opening-FMV field, not a valid timeline checkpoint.
            for (Int32 i = _snapshots.Count - 1; i >= 0; i--)
            {
                if (_snapshots[i] == null || _snapshots[i].FieldId <= 0 || _snapshots[i].FieldId == 70)
                    _snapshots.RemoveAt(i);
            }
            _timelineCursor = Math.Max(0, Math.Min(_timelineCursor, Math.Max(0, _snapshots.Count - 1)));

            // The JSON file is only a bridge across process restarts.
            // Once the history has been rebuilt into RAM, remove the disk copy.
            DeletePersistentHistoryFile();
        }

        public void Update()
        {
            if (_captureSuspended)
            {
                _stableFrames = 0;
                return;
            }

            if (_suppressCaptureFrames > 0)
            {
                _suppressCaptureFrames--;
                return;
            }

            if (PersistenSingleton<FF9StateSystem>.Instance == null ||
                PersistenSingleton<FF9StateSystem>.Instance.mode != 1 ||
                PersistenSingleton<UIManager>.Instance == null ||
                PersistenSingleton<UIManager>.Instance.UnityScene != UIManager.Scene.Field)
            {
                _stableFrames = 0;
                return;
            }

            Int32 field = GetCurrentField();
            if (field <= 0 || field == 70)
            {
                // Memoria EventEngine.NewGame() hard-codes field 70 ("Opening-For FMV").
                // Never let that startup/new-game sentinel enter the dev timeline.
                _stableFrames = 0;
                return;
            }

            if (field != _observedField)
            {
                _observedField = field;
                _stableFrames = 0;
                _restoringHistory = false;
                return;
            }

            // A field reached through timeline navigation is already represented by its
            // saved snapshot. Do not auto-capture it again, otherwise it becomes the
            // newest entry and resets the history cursor back to zero.
            if (_restoringHistory)
                return;

            if (_stableFrames < StableFramesBeforeSnapshot)
            {
                _stableFrames++;
                if (_stableFrames == StableFramesBeforeSnapshot)
                    CaptureCurrent(false);
            }
        }

        public String CaptureCurrent(Boolean manual)
        {
            try
            {
                if (!manual && _restoringHistory)
                    return "Snapshot skipped while browsing field history.";
                if (FF9StateSystem.Serializer == null || FF9StateSystem.Serializer.Parser == null)
                    return "Snapshot failed: Memoria state parser is not ready.";

                if (PersistenSingleton<FF9StateSystem>.Instance == null ||
                    PersistenSingleton<FF9StateSystem>.Instance.mode != 1)
                    return "Snapshot skipped: not currently on a field map.";

                Int32 field = GetCurrentField();
                if (field <= 0 || field == 70)
                    return "Snapshot skipped: invalid/startup field.";

                FF9StateSystem.Serializer.Parser.ParseFromFF9StateSystem();
                JSONClass root = FF9StateSystem.Serializer.Parser.RootNodeInParser;
                if (root == null)
                    return "Snapshot failed: parser returned no state.";

                FieldSnapshot snapshot = new FieldSnapshot();
                snapshot.FieldId = field;
                snapshot.LocationId = FF9StateSystem.Common.FF9.fldLocNo;
                snapshot.Scenario = FF9StateSystem.EventState.ScenarioCounter;
                snapshot.CapturedUtc = DateTime.UtcNow;
                snapshot.Data = root;
                snapshot.Manual = manual;

                Boolean isNewAutomaticField = !manual &&
                    (_snapshots.Count == 0 || _snapshots[0].FieldId != snapshot.FieldId);

                if (!manual && _snapshots.Count > 0 && _snapshots[0].FieldId == snapshot.FieldId)
                    _snapshots[0] = snapshot;
                else
                {
                    _snapshots.Insert(0, snapshot);
                    if (_snapshots.Count > MaxSnapshots)
                        _snapshots.RemoveAt(_snapshots.Count - 1);
                }

                _timelineCursor = 0;
                _restoringHistory = false;

                // Automatic snapshots are intentionally quiet: log only every tenth
                // genuinely new field so Live Log remains useful during normal play.
                if (manual)
                {
                    Log.Message("[Dev Console] Field snapshot captured. Field " +
                                snapshot.FieldId + ", scenario " + snapshot.Scenario + ".");
                }
                else if (isNewAutomaticField)
                {
                    _automaticSnapshotLogCounter++;
                    if (_automaticSnapshotLogCounter >= 10)
                    {
                        _automaticSnapshotLogCounter = 0;
                        Log.Message("[Dev Console] Field snapshot captured (10-field update). Field " +
                                    snapshot.FieldId + ", scenario " + snapshot.Scenario + ".");
                    }
                }

                return "Snapshot captured: field " + snapshot.FieldId +
                       "  scenario " + snapshot.Scenario + ".";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Field snapshot failed: " + ex);
                return "Snapshot failed: " + ex.Message;
            }
        }

        public String SetCheckpoint()
        {
            try
            {
                if (FF9StateSystem.Serializer == null || FF9StateSystem.Serializer.Parser == null)
                    return "CHECKPOINT_UNAVAILABLE";

                if (PersistenSingleton<FF9StateSystem>.Instance == null ||
                    PersistenSingleton<FF9StateSystem>.Instance.mode != 1)
                    return "CHECKPOINT_UNAVAILABLE";

                Int32 field = GetCurrentField();
                if (field <= 0)
                    return "CHECKPOINT_UNAVAILABLE";

                FF9StateSystem.Serializer.Parser.ParseFromFF9StateSystem();
                JSONClass root = FF9StateSystem.Serializer.Parser.RootNodeInParser;
                if (root == null)
                    return "CHECKPOINT_UNAVAILABLE";

                JSONNode clonedNode = JSONNode.Parse(root.ToString());
                JSONClass cloned = clonedNode as JSONClass;
                if (cloned == null)
                    return "CHECKPOINT_UNAVAILABLE";

                FieldSnapshot checkpoint = new FieldSnapshot();
                checkpoint.FieldId = field;
                checkpoint.LocationId = FF9StateSystem.Common.FF9.fldLocNo;
                checkpoint.Scenario = FF9StateSystem.EventState.ScenarioCounter;
                checkpoint.CapturedUtc = DateTime.UtcNow;
                checkpoint.Data = cloned;
                checkpoint.Manual = true;

                _checkpoint = checkpoint;

                Log.Message("[Dev Console] Field checkpoint set. Field " +
                            checkpoint.FieldId + ", scenario " + checkpoint.Scenario + ".");

                return "CHECKPOINT_SET";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to set field checkpoint: " + ex);
                return "CHECKPOINT_UNAVAILABLE";
            }
        }

        public String LoadCheckpoint()
        {
            try
            {
                if (_checkpoint == null || _checkpoint.Data == null)
                    return "NO_CHECKPOINT";

                if (FF9StateSystem.Serializer == null || FF9StateSystem.Serializer.Parser == null)
                    return "CHECKPOINT_UNAVAILABLE";

                JSONNode clonedNode = JSONNode.Parse(_checkpoint.Data.ToString());
                JSONClass cloned = clonedNode as JSONClass;
                if (cloned == null)
                    return "CHECKPOINT_UNAVAILABLE";

                Log.Message("[Dev Console] Loading field checkpoint. Field " +
                            _checkpoint.FieldId + ", scenario " + _checkpoint.Scenario + ".");

                FF9StateSystem.Serializer.Parser.ParseToFF9StateSystem(cloned);
                PersistenSingleton<EventEngine>.Instance.ReplaceLoadMap();

                _observedField = _checkpoint.FieldId;
                _stableFrames = 0;
                _suppressCaptureFrames = 60;
                _restoringHistory = true;

                return "CHECKPOINT_LOADED";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to load field checkpoint: " + ex);
                return "CHECKPOINT_UNAVAILABLE";
            }
        }

        public String GetCheckpointStatus()
        {
            if (_checkpoint == null)
                return "Checkpoint: not set";

            return "Checkpoint: Field " + _checkpoint.FieldId +
                   "  Scenario " + _checkpoint.Scenario;
        }

        public String MoveBackward()
        {
            if (_snapshots.Count < 2 || _timelineCursor >= _snapshots.Count - 1)
                return "OUT_OF_HISTORY_BACK";

            Int32 target = _timelineCursor + 1;

            // Never mutate the cursor unless the target is known-valid.
            if (target < 0 || target >= _snapshots.Count)
                return "OUT_OF_HISTORY_BACK";

            String result = Restore(target, true);
            if (!String.IsNullOrEmpty(result) && result.StartsWith("Restoring field "))
                _timelineCursor = target;
            return result;
        }

        public String MoveForward()
        {
            if (_snapshots.Count == 0 || _timelineCursor <= 0)
                return "OUT_OF_HISTORY_FORWARD";

            Int32 target = _timelineCursor - 1;

            // Never mutate the cursor unless the target is known-valid.
            if (target < 0 || target >= _snapshots.Count)
                return "OUT_OF_HISTORY_FORWARD";

            String result = Restore(target, true);
            if (!String.IsNullOrEmpty(result) && result.StartsWith("Restoring field "))
                _timelineCursor = target;
            return result;
        }

        public String Restore(Int32 historyIndex)
        {
            return Restore(historyIndex, false);
        }

        // Model Viewer return path: restore the newest snapshot that was already captured
        // by the normal field-transition/stability tracker. Do NOT capture a new snapshot
        // when opening the viewer; that could preserve a mid-field/mid-event state.
        public String RestoreLatestStableFieldSnapshot()
        {
            if (_snapshots.Count == 0)
                return "No stable field snapshot is available.";

            return Restore(0, false);
        }

        private String Restore(Int32 historyIndex, Boolean browsingHistory)
        {
            try
            {
                if (historyIndex < 0 || historyIndex >= _snapshots.Count)
                    return "No snapshot exists for that history position.";

                FieldSnapshot snapshot = _snapshots[historyIndex];
                if (snapshot == null || snapshot.Data == null)
                    return "Snapshot is empty.";

                // Field 70 is not ordinary history: Memoria's EventEngine.NewGame()
                // explicitly assigns it as "Opening-For FMV". Reject it BEFORE ParseToFF9StateSystem
                // so an out-of-range/history sentinel can never start a new game.
                if (snapshot.FieldId <= 0 || snapshot.FieldId == 70)
                    return browsingHistory ? "OUT_OF_HISTORY_BACK" : "Snapshot is not a restorable field.";

                if (FF9StateSystem.Serializer == null || FF9StateSystem.Serializer.Parser == null)
                    return "Restore failed: Memoria state parser is not ready.";

                Log.Message("[Dev Console] Restoring field snapshot " + historyIndex +
                            ": field " + snapshot.FieldId +
                            ", scenario " + snapshot.Scenario + ".");

                FF9StateSystem.Serializer.Parser.ParseToFF9StateSystem(snapshot.Data);
                PersistenSingleton<EventEngine>.Instance.ReplaceLoadMap();

                _observedField = snapshot.FieldId;
                _stableFrames = 0;
                _suppressCaptureFrames = 60;
                _restoringHistory = browsingHistory;

                return "Restoring field " + snapshot.FieldId +
                       "  scenario " + snapshot.Scenario + "...";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Field snapshot restore failed: " + ex);
                return "Restore failed: " + ex.Message;
            }
        }

        public String PersistHistoryForRestart()
        {
            try
            {
                // Refresh the newest snapshot right before leaving the game when possible.
                if (PersistenSingleton<FF9StateSystem>.Instance != null &&
                    PersistenSingleton<FF9StateSystem>.Instance.mode == 1)
                    CaptureCurrent(false);

                if (_snapshots.Count == 0)
                    return "No field snapshots are available to preserve.";

                Directory.CreateDirectory(DevConsoleDataDirectory);

                JSONClass root = new JSONClass();
                root.Add("Version", "1");
                root.Add("TimelineCursor", _timelineCursor.ToString());

                JSONArray array = new JSONArray();
                for (Int32 i = 0; i < _snapshots.Count; i++)
                {
                    FieldSnapshot snap = _snapshots[i];
                    JSONClass node = new JSONClass();
                    node.Add("FieldId", snap.FieldId.ToString());
                    node.Add("LocationId", snap.LocationId.ToString());
                    node.Add("Scenario", snap.Scenario.ToString());
                    node.Add("CapturedUtcTicks", snap.CapturedUtc.Ticks.ToString());
                    node.Add("Manual", snap.Manual ? "1" : "0");
                    node.Add("Data", snap.Data);
                    array.Add(node);
                }
                root.Add("Snapshots", array);

                if (_checkpoint != null && _checkpoint.Data != null)
                {
                    root.Add("HasCheckpoint", "1");

                    JSONClass checkpointNode = new JSONClass();
                    checkpointNode.Add("FieldId", _checkpoint.FieldId.ToString());
                    checkpointNode.Add("LocationId", _checkpoint.LocationId.ToString());
                    checkpointNode.Add("Scenario", _checkpoint.Scenario.ToString());
                    checkpointNode.Add("CapturedUtcTicks", _checkpoint.CapturedUtc.Ticks.ToString());
                    checkpointNode.Add("Data", _checkpoint.Data);
                    root.Add("Checkpoint", checkpointNode);
                }
                else
                {
                    root.Add("HasCheckpoint", "0");
                }

                File.WriteAllText(HistoryPath, root.ToString());
                Log.Message("[Dev Console] Persisted " + _snapshots.Count +
                            " field snapshots across restart.");

                return "Saved " + _snapshots.Count + " field snapshots across restart.";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to persist field history across restart: " + ex);
                return "Failed to save field history: " + ex.Message;
            }
        }

        private void LoadPersistentHistory()
        {
            if (!File.Exists(HistoryPath))
                return;

            try
            {
                JSONNode parsed = JSONNode.Parse(File.ReadAllText(HistoryPath));
                JSONClass root = parsed == null ? null : parsed.AsObject;
                if (root == null)
                    return;

                JSONArray array = root["Snapshots"].AsArray;
                if (array == null)
                    return;

                _snapshots.Clear();

                for (Int32 i = 0; i < array.Count && _snapshots.Count < MaxSnapshots; i++)
                {
                    JSONClass node = array[i].AsObject;
                    if (node == null)
                        continue;

                    JSONClass data = node["Data"].AsObject;
                    if (data == null)
                        continue;

                    FieldSnapshot snap = new FieldSnapshot();
                    snap.FieldId = node["FieldId"].AsInt;
                    snap.LocationId = node["LocationId"].AsInt;
                    snap.Scenario = node["Scenario"].AsInt;

                    Int64 ticks;
                    if (!Int64.TryParse(node["CapturedUtcTicks"].Value, out ticks))
                        ticks = DateTime.UtcNow.Ticks;
                    snap.CapturedUtc = new DateTime(ticks, DateTimeKind.Utc);
                    snap.Manual = node["Manual"].Value == "1";
                    snap.Data = data;
                    _snapshots.Add(snap);
                }

                Int32 cursor = root["TimelineCursor"].AsInt;
                if (cursor == 0 && root["BackCursor"] != null)
                    cursor = root["BackCursor"].AsInt;

                _timelineCursor = Math.Max(0, Math.Min(cursor, Math.Max(0, _snapshots.Count - 1)));

                _checkpoint = null;
                if (root["HasCheckpoint"].Value == "1")
                {
                    JSONClass checkpointNode = root["Checkpoint"] as JSONClass;
                    if (checkpointNode != null)
                    {
                        FieldSnapshot checkpoint = new FieldSnapshot();
                        checkpoint.FieldId = checkpointNode["FieldId"].AsInt;
                        checkpoint.LocationId = checkpointNode["LocationId"].AsInt;
                        checkpoint.Scenario = checkpointNode["Scenario"].AsInt;

                        Int64 checkpointTicks;
                        if (!Int64.TryParse(checkpointNode["CapturedUtcTicks"].Value, out checkpointTicks))
                            checkpointTicks = DateTime.UtcNow.Ticks;

                        checkpoint.CapturedUtc = new DateTime(checkpointTicks, DateTimeKind.Utc);
                        checkpoint.Manual = true;
                        checkpoint.Data = checkpointNode["Data"] as JSONClass;

                        if (checkpoint.Data != null)
                            _checkpoint = checkpoint;
                    }
                }

                Log.Message("[Dev Console] Reloaded " + _snapshots.Count +
                            " persisted field snapshots into RAM.");
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to reload persistent field history: " + ex);
            }
        }

        private static void DeletePersistentHistoryFile()
        {
            try
            {
                if (File.Exists(HistoryPath))
                    File.Delete(HistoryPath);
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not delete persistent field history: " + ex.Message);
            }
        }

        public String BuildStatusText()
        {
            if (_snapshots.Count == 0)
                return "No field snapshots yet.";

            Int32 displayPosition = _timelineCursor + 1;
            FieldSnapshot active = _snapshots[_timelineCursor];

            String text =
                "Stored snapshots: " + _snapshots.Count + " / " + MaxSnapshots + "\r\n" +
                "Timeline position: " + displayPosition + " / " + _snapshots.Count +
                "  -> Field " + active.FieldId +
                "  Scenario " + active.Scenario + "\r\n" +
                "\r\n" +
                "1 is newest. Move Back travels toward older fields; Move Forward returns toward newer fields.\r\n" +
                GetCheckpointStatus() + "\r\n" +
                "\r\n" +
                "FIELD TIMELINE:\r\n";

            for (Int32 i = 0; i < _snapshots.Count; i++)
            {
                FieldSnapshot snap = _snapshots[i];
                text += "  " + (i + 1) + ": Field " + snap.FieldId +
                        "  Scenario " + snap.Scenario;

                if (i == _timelineCursor)
                    text += "  [YOU ARE HERE]";

                text += "\r\n";
            }

            return text.TrimEnd('\r', '\n');
        }

        private static Int32 GetCurrentField()
        {
            try
            {
                if (FF9StateSystem.Common == null || FF9StateSystem.Common.FF9 == null)
                    return -1;
                return FF9StateSystem.Common.FF9.fldMapNo;
            }
            catch
            {
                return -1;
            }
        }

        private sealed class FieldSnapshot
        {
            public Int32 FieldId;
            public Int32 LocationId;
            public Int32 Scenario;
            public DateTime CapturedUtc;
            public JSONClass Data;
            public Boolean Manual;
        }
    }
}
