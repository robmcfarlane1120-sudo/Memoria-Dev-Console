using Memoria.Data;
using Memoria.Prime;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;

namespace Memoria.DevConsole
{
    public sealed class FieldStateService
    {
        public event Action BeforeSnapshotLoad;

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

        private static String SaveStatesDirectory
        {
            get { return Path.Combine(DevConsoleDataDirectory, "SaveStates"); }
        }

        private static String SaveStatesIndexPath
        {
            get { return Path.Combine(SaveStatesDirectory, "index.json"); }
        }

        private readonly List<NamedSaveState> _namedSaves = new List<NamedSaveState>();

        public void Initialize()
        {
            _observedField = GetCurrentField();
            _stableFrames = 0;
            _suppressCaptureFrames = 0;
            _timelineCursor = 0;
            _restoringHistory = false;

            LoadPersistentHistory();
            LoadNamedSaveIndex();

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

                CaptureSnapshotMusic(root);

                FieldSnapshot snapshot = new FieldSnapshot();
                snapshot.FieldId = field;
                snapshot.LocationId = FF9StateSystem.Common.FF9.fldLocNo;
                snapshot.Scenario = FF9StateSystem.EventState.ScenarioCounter;
                snapshot.CapturedUtc = DateTime.UtcNow;
                snapshot.Data = root;
                snapshot.Manual = manual;

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

                if (manual)
                {
                    Log.Message("[Dev Console] Field snapshot captured. Field " +
                                snapshot.FieldId + ", scenario " + snapshot.Scenario + ".");
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

                CaptureSnapshotMusic(root);
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

                RestoreSnapshotState(cloned);
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

        // --- Named save states -----------------------------------------------------------
        // Unlike the rolling 10-state history and single checkpoint above (both meant for
        // rapid iterative testing), these are permanent, titled saves with a screenshot,
        // written to disk immediately. They exist independently of the game's own save
        // slots and of field_history.json, and are never pruned or overwritten automatically.

        /// Captures the current field state and pairs it with an already-captured
        /// screenshot (PNG bytes; may be null/empty if the screenshot failed, in which
        /// case the save is still made without one). Must be called from Unity's main
        /// thread, same as CaptureCurrent/SetCheckpoint.
        public String SaveNamedState(String rawTitle, Byte[] screenshotPng)
        {
            try
            {
                if (FF9StateSystem.Serializer == null || FF9StateSystem.Serializer.Parser == null)
                    return "Save failed: Memoria state parser is not ready.";

                if (PersistenSingleton<FF9StateSystem>.Instance == null)
                    return "Save failed: game state is unavailable.";

                Int32 mapMode = PersistenSingleton<FF9StateSystem>.Instance.mode;
                if (mapMode != 1 && mapMode != 3)
                    return "Save failed: saves can only be created on a field or the world map.";

                Int32 field = GetCurrentField();
                if (mapMode == 1 && (field <= 0 || field == 70))
                    return "Save failed: invalid/startup field.";

                FF9StateSystem.Serializer.Parser.ParseFromFF9StateSystem();
                JSONClass root = FF9StateSystem.Serializer.Parser.RootNodeInParser;
                if (root == null)
                    return "Save failed: parser returned no state.";

                CaptureSnapshotMusic(root);

                String title = String.IsNullOrEmpty(rawTitle) ? "Untitled" : rawTitle.Trim();
                if (title.Length == 0)
                    title = "Untitled";
                if (title.Length > 60)
                    title = title.Substring(0, 60);

                String id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");

                Directory.CreateDirectory(SaveStatesDirectory);
                File.WriteAllText(Path.Combine(SaveStatesDirectory, id + ".state.json"), root.ToString());

                if (screenshotPng != null && screenshotPng.Length > 0)
                    File.WriteAllBytes(Path.Combine(SaveStatesDirectory, id + ".png"), screenshotPng);

                NamedSaveState save = new NamedSaveState();
                save.Id = id;
                save.Title = title;
                save.MapMode = mapMode;
                save.FieldId = field;
                save.WorldMapId = FF9StateSystem.Common.FF9.wldMapNo;
                save.LocationId = FF9StateSystem.Common.FF9.fldLocNo;
                save.LocationName = ResolveLocationName(mapMode, field);
                save.Scenario = FF9StateSystem.EventState.ScenarioCounter;
                save.CapturedUtc = DateTime.UtcNow;
                CapturePartyInfo(out save.PartySummary, out save.PartyCharacterIds);

                _namedSaves.Insert(0, save);
                WriteNamedSaveIndex();

                Log.Message("[Dev Console] Named save state created: \"" + title + "\" (" +
                            GetMapTypeLabel(mapMode) + ", map " + GetDisplayMapId(save) + ").");

                Boolean hasScreenshot = screenshotPng != null && screenshotPng.Length > 0;
                return "Saved \"" + title + "\" — " + save.LocationName +
                       " (" + GetMapTypeLabel(mapMode) + " " + GetDisplayMapId(save) +
                       ", scenario " + save.Scenario + ")" +
                       (hasScreenshot ? "." : " (no screenshot).");
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Named save failed: " + ex);
                return "Save failed: " + ex.Message;
            }
        }

        /// Party snapshot for display purposes only (never used for the actual save/load,
        /// which goes through the full serialized state). Reads FF9StateSystem.Common.FF9.
        /// party.member[] directly — confirmed against Memoria's own source
        /// (Assembly-CSharp/Global/ff9play.cs): FF9Play_SetParty writes into that same
        /// array, and FF9Play_GetAvgLevel reads level straight off PLAYER.level. No
        /// reflection needed; this is the real API, not a guess.
        /// Produces both the human-readable summary ("Zidane Lv12 / Vivi Lv11") and a
        /// comma-separated CharacterId list ("Zidane,Vivi") the UI layer can use to look
        /// up portraits without re-parsing the display text.
        private static void CapturePartyInfo(out String summary, out String characterIdsCsv)
        {
            try
            {
                FF9StateGlobal ff9 = FF9StateSystem.Common.FF9;
                if (ff9 == null || ff9.party == null || ff9.party.member == null)
                {
                    summary = "(party unavailable)";
                    characterIdsCsv = String.Empty;
                    return;
                }

                List<String> members = new List<String>();
                List<String> ids = new List<String>();
                for (Int32 slot = 0; slot < ff9.party.member.Length; slot++)
                {
                    PLAYER member = ff9.party.member[slot];
                    if (member == null)
                        continue;

                    members.Add(member.Index + " Lv" + member.level);
                    ids.Add(member.Index.ToString());
                }

                summary = members.Count == 0 ? "(no active party)" : String.Join(" / ", members.ToArray());
                characterIdsCsv = String.Join(",", ids.ToArray());
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Party capture failed: " + ex.Message);
                summary = "(party unavailable)";
                characterIdsCsv = String.Empty;
            }
        }

        private static void CaptureSnapshotMusic(JSONClass root)
        {
            // Normal autosaves run WillAutosave before parsing. MDC parses directly,
            // so capture live BGM explicitly without changing the engine's pending BGM.
            Int32 musicId = FF9Snd.GetCurrentMusicId();
            root["Data"]["60000_Sound"]["auto_save_bgm_id"].AsInt = musicId;
            JSONClass audio = new JSONClass();
            audio.Add("MusicId", new JSONData(musicId));
            root.Add("DevConsoleAudio", audio);
        }

        private void RestoreSnapshotState(JSONClass data)
        {
            Action beforeLoad = BeforeSnapshotLoad;
            if (beforeLoad != null) beforeLoad();
            SceneTransitionAudio.Prepare();
            FF9StateSystem.Serializer.Parser.ParseToFF9StateSystem(data);
            // Title music (including FMV000) uses MusicPlayer rather than the
            // field/world dispatcher. Stop both standalone channels before BGM restore.
            SoundLib.StopMusic(0);
            if (SoundLib.MovieAudioPlayer != null)
                SoundLib.MovieAudioPlayer.StopMusic(0);
            AllSoundDispatchPlayer player = SoundLib.GetAllSoundDispatchPlayer();
            if (player != null)
            {
                player.FF9SOUND_SONG_STOPCURRENT();
                player.StopAndClearSuspendBGM(-1);
            }
            FF9Snd.LatestWorldPlayedSong = -1;
            JSONNode audio = data["DevConsoleAudio"];
            if (audio != null && audio.AsObject != null)
                FF9StateSystem.Sound.auto_save_bgm_id = audio["MusicId"].AsInt;
            // Match the engine's normal autoload path before ReplaceLoadMap.
            SharedSerializerEvent.DidAutoload();
        }
        public String LoadNamedSave(String id)
        {
            try
            {
                NamedSaveState save = _namedSaves.Find(delegate(NamedSaveState s) { return s.Id == id; });
                if (save == null)
                    return "Save slot not found.";

                String statePath = Path.Combine(SaveStatesDirectory, id + ".state.json");
                if (!File.Exists(statePath))
                    return "Save data file is missing on disk.";

                if (FF9StateSystem.Serializer == null || FF9StateSystem.Serializer.Parser == null)
                    return "Load failed: Memoria state parser is not ready.";

                JSONNode parsed = JSONNode.Parse(File.ReadAllText(statePath));
                JSONClass data = parsed == null ? null : parsed.AsObject;
                if (data == null)
                    return "Load failed: save data is corrupt.";

                Log.Message("[Dev Console] Loading named save \"" + save.Title + "\" (" +
                            GetMapTypeLabel(save.MapMode) + " " + GetDisplayMapId(save) + ").");

                RestoreSnapshotState(data);
                PersistenSingleton<EventEngine>.Instance.ReplaceLoadMap();

                _observedField = save.FieldId;
                _stableFrames = 0;
                _suppressCaptureFrames = 60;
                _restoringHistory = true;

                return "Loaded \"" + save.Title + "\" — " + save.LocationName + " (" +
                       GetMapTypeLabel(save.MapMode) + " " + GetDisplayMapId(save) +
                       ", scenario " + save.Scenario + ").";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Named save load failed: " + ex);
                return "Load failed: " + ex.Message;
            }
        }

        public String DeleteNamedSave(String id)
        {
            try
            {
                Int32 index = _namedSaves.FindIndex(delegate(NamedSaveState s) { return s.Id == id; });
                if (index < 0)
                    return "Save slot not found.";

                String title = _namedSaves[index].Title;
                _namedSaves.RemoveAt(index);
                WriteNamedSaveIndex();

                try { File.Delete(Path.Combine(SaveStatesDirectory, id + ".state.json")); }
                catch (Exception ex) { Log.Warning("[Dev Console] Could not delete save data file: " + ex.Message); }

                try { File.Delete(Path.Combine(SaveStatesDirectory, id + ".png")); }
                catch (Exception ex) { Log.Warning("[Dev Console] Could not delete save screenshot file: " + ex.Message); }

                Log.Message("[Dev Console] Deleted named save \"" + title + "\".");
                return "Deleted \"" + title + "\".";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Named save delete failed: " + ex);
                return "Delete failed: " + ex.Message;
            }
        }

        public String GetScreenshotPath(String id)
        {
            if (String.IsNullOrEmpty(id))
                return null;

            String path = Path.Combine(SaveStatesDirectory, id + ".png");
            return File.Exists(path) ? path : null;
        }

        public List<NamedSaveSummary> GetNamedSaves()
        {
            List<NamedSaveSummary> list = new List<NamedSaveSummary>(_namedSaves.Count);
            for (Int32 i = 0; i < _namedSaves.Count; i++)
            {
                NamedSaveState s = _namedSaves[i];
                NamedSaveSummary summary = new NamedSaveSummary();
                summary.Id = s.Id;
                summary.Title = s.Title;
                summary.MapMode = s.MapMode;
                summary.FieldId = s.FieldId;
                summary.WorldMapId = s.WorldMapId;
                summary.LocationId = s.LocationId;
                summary.LocationName = String.IsNullOrEmpty(s.LocationName)
                    ? ResolveLocationName(s.MapMode, s.FieldId)
                    : s.LocationName;
                summary.Scenario = s.Scenario;
                summary.CapturedUtc = s.CapturedUtc;
                summary.PartySummary = s.PartySummary;
                summary.PartyCharacterIds = s.PartyCharacterIds;
                summary.ScreenshotPath = GetScreenshotPath(s.Id);
                list.Add(summary);
            }
            return list;
        }

        private void WriteNamedSaveIndex()
        {
            try
            {
                Directory.CreateDirectory(SaveStatesDirectory);

                JSONClass root = new JSONClass();
                root.Add("Version", "3");

                JSONArray array = new JSONArray();
                for (Int32 i = 0; i < _namedSaves.Count; i++)
                {
                    NamedSaveState s = _namedSaves[i];
                    JSONClass node = new JSONClass();
                    node.Add("Id", s.Id);
                    node.Add("Title", s.Title);
                    node.Add("MapMode", s.MapMode.ToString());
                    node.Add("FieldId", s.FieldId.ToString());
                    node.Add("WorldMapId", s.WorldMapId.ToString());
                    node.Add("LocationId", s.LocationId.ToString());
                    node.Add("LocationName", String.IsNullOrEmpty(s.LocationName)
                        ? ResolveLocationName(s.MapMode, s.FieldId)
                        : s.LocationName);
                    node.Add("Scenario", s.Scenario.ToString());
                    node.Add("CapturedUtcTicks", s.CapturedUtc.Ticks.ToString());
                    node.Add("PartySummary", s.PartySummary ?? String.Empty);
                    node.Add("PartyCharacterIds", s.PartyCharacterIds ?? String.Empty);
                    array.Add(node);
                }
                root.Add("Saves", array);

                File.WriteAllText(SaveStatesIndexPath, root.ToString());
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to write named save index: " + ex);
            }
        }

        private void LoadNamedSaveIndex()
        {
            _namedSaves.Clear();

            try
            {
                if (!File.Exists(SaveStatesIndexPath))
                    return;

                JSONNode parsed = JSONNode.Parse(File.ReadAllText(SaveStatesIndexPath));
                JSONClass root = parsed == null ? null : parsed.AsObject;
                if (root == null)
                    return;

                JSONArray array = root["Saves"].AsArray;
                if (array == null)
                    return;

                for (Int32 i = 0; i < array.Count; i++)
                {
                    JSONClass node = array[i].AsObject;
                    if (node == null)
                        continue;

                    NamedSaveState s = new NamedSaveState();
                    s.Id = node["Id"].Value;
                    s.Title = node["Title"].Value;
                    s.MapMode = node["MapMode"].AsInt;
                    if (s.MapMode != 1 && s.MapMode != 3)
                        s.MapMode = 1;
                    s.FieldId = node["FieldId"].AsInt;
                    s.WorldMapId = node["WorldMapId"].AsInt;
                    s.LocationId = node["LocationId"].AsInt;
                    s.LocationName = node["LocationName"].Value;
                    if (String.IsNullOrEmpty(s.LocationName))
                        s.LocationName = ResolveLocationName(s.MapMode, s.FieldId);
                    s.Scenario = node["Scenario"].AsInt;
                    s.PartySummary = node["PartySummary"].Value;
                    s.PartyCharacterIds = node["PartyCharacterIds"].Value;

                    Int64 ticks;
                    if (!Int64.TryParse(node["CapturedUtcTicks"].Value, out ticks))
                        ticks = DateTime.UtcNow.Ticks;
                    s.CapturedUtc = new DateTime(ticks, DateTimeKind.Utc);

                    if (!String.IsNullOrEmpty(s.Id))
                        _namedSaves.Add(s);
                }

            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to load named save index: " + ex);
            }
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

                RestoreSnapshotState(snapshot.Data);
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

        private static String ResolveLocationName(Int32 mapMode, Int32 fieldId)
        {
            try
            {
                String name;
                if (mapMode == 3 && PersistenSingleton<EventEngine>.Instance != null)
                    name = global::Assets.Sources.Scripts.UI.Common.FF9TextTool.WorldLocationText(
                        PersistenSingleton<EventEngine>.Instance.GetSysvar(192));
                else
                    name = global::Assets.Sources.Scripts.UI.Common.FF9TextTool.LocationName(fieldId);
                if (!String.IsNullOrEmpty(name))
                    return name;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console] Could not resolve location name for map " +
                            fieldId + ": " + ex.Message);
            }

            return mapMode == 3 ? "World Map" : "Field " + fieldId;
        }

        private static String GetMapTypeLabel(Int32 mapMode)
        {
            return mapMode == 3 ? "world map" : "field";
        }

        private static Int32 GetDisplayMapId(NamedSaveState save)
        {
            return save != null && save.MapMode == 3 ? save.WorldMapId : save == null ? 0 : save.FieldId;
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

        private sealed class NamedSaveState
        {
            public String Id;
            public String Title;
            public Int32 MapMode;
            public Int32 FieldId;
            public Int32 WorldMapId;
            public Int32 LocationId;
            public String LocationName;
            public Int32 Scenario;
            public DateTime CapturedUtc;
            public String PartySummary;
            public String PartyCharacterIds;
        }
    }

    /// Read-only summary handed to the UI layer. Full state JSON stays inside
    /// FieldStateService/disk and is loaded lazily by Id, not kept here.
    public sealed class NamedSaveSummary
    {
        public String Id;
        public String Title;
        public Int32 MapMode;
        public Int32 FieldId;
        public Int32 WorldMapId;
        public Int32 LocationId;
        public String LocationName;
        public Int32 Scenario;
        public DateTime CapturedUtc;
        public String PartySummary;
        public String PartyCharacterIds;
        public String ScreenshotPath;
    }
}
