using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed class DevConsoleController : MonoBehaviour
    {
        private WorldEntranceEditorService _worldEntranceEditor;
        private BattleSceneEditorService _battleSceneEditor;
        private LiveLogService _logService;
        private NativeConsoleWindow _window;
        private CompileService _compileService;
        private FieldStateService _fieldStateService;
        private ModelViewerService _modelViewerService;
        private ResolutionUiSyncService _resolutionUiSyncService;
        private AssetDumpService _assetDumpService;
        private WarpService _warpService;
        private SoundBrowserService _soundBrowserService;
        private SoundBrowserView _soundBrowserView;

        private static readonly FieldInfo UpcomingMusic = typeof(MusicPlayer)
            .GetField("upcomingSoundProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DispatchMusic = typeof(AllSoundDispatchPlayer)
            .GetField("musicImmediateDB", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly HashSet<Int32> _pausedGameMusic = new HashSet<Int32>();

        private Boolean _visible;
        private String _lastNativeError;

        private volatile Boolean _compileRequested;
        private volatile Boolean _modelViewerToggleRequested;
        private volatile Int32 _viewerKindRequested;
        private volatile Boolean _hardResetRequested;
        private volatile Boolean _resetToLauncherRequested;
        private volatile Int32 _assetDumpRequested;
        private volatile Int32 _assetLookupId = -1;
        private String _assetCutImagePath;
        private volatile Boolean _warpRequested;
        private StoryWarpPreset _warpPreset;
        private volatile Boolean _soundCatalogRequested;
        private volatile Boolean _soundPreviewRequested;
        private volatile Boolean _soundStopRequested;
        private volatile Boolean _soundSfxLoadRequested;
        private DevSoundEntry _soundPreviewEntry;
        private Int32 _soundSfxLoadId = -1;
        private volatile Boolean _soundAliasSaveRequested;
        private DevSoundEntry _soundAliasEntry;
        private String _soundAliasText;
        private volatile Boolean _restartPending;
        private DateTime _restartNotBeforeUtc = DateTime.MinValue;

        private volatile Boolean _fieldStateMoveBackwardRequested;
        private volatile Boolean _fieldStateMoveForwardRequested;
        private volatile Boolean _fieldStateSetCheckpointRequested;
        private volatile Boolean _fieldStateLoadCheckpointRequested;
        private volatile Boolean _saveStateCaptureRequested;
        private String _saveStateCaptureTitle;
        private volatile Boolean _saveStateLoadRequested;
        private String _saveStateLoadId;
        private volatile Boolean _saveStateDeleteRequested;
        private String _saveStateDeleteId;
        private volatile Boolean _saveStateListRequested;

        private volatile Boolean _shuttingDown;
        private Boolean _runtimeFreezeApplied;
        private Boolean _viewerExitInputPending;
        private Int32 _viewerExitFrame;
        private UIKeyTrigger _frozenUiKeyTrigger;
        private Boolean _savedUiKeyTriggerEnabled;
        private HonoBehaviorSystem _frozenHonoSystem;
        private Boolean _savedHonoSystemEnabled;

        private void Awake()
        {
            try
            {
                _logService = new LiveLogService(800);
                _logService.Initialize();

                _window = new NativeConsoleWindow();
                _window.CompileRestartRequested += OnCompileRestartRequested;
                _window.ModelViewerRequested += OnModelViewerRequested;
                _window.ViewerRequested += OnViewerRequested;
                _window.HardResetRequested += OnHardResetRequested;
                _window.ResetToLauncherRequested += OnResetToLauncherRequested;
                _window.FieldStateMoveBackwardRequested += OnFieldStateMoveBackwardRequested;
                _window.FieldStateMoveForwardRequested += OnFieldStateMoveForwardRequested;
            _window.FieldStateSetCheckpointRequested += OnFieldStateSetCheckpointRequested;
            _window.FieldStateLoadCheckpointRequested += OnFieldStateLoadCheckpointRequested;
                                _window.FieldStatePageOpened += OnFieldStatePageOpened;
                _window.CurrentFieldDumpRequested += OnCurrentFieldDumpRequested;
                _window.TextureDumpRequested += OnTextureDumpRequested;
                _window.FieldBackgroundByIdRequested += OnFieldBackgroundByIdRequested;
                _window.FieldCompositeByIdRequested += OnFieldCompositeByIdRequested;
                _window.FieldCompositeCutRequested += OnFieldCompositeCutRequested;
                _window.TextureByModelIdRequested += OnTextureByModelIdRequested;
                _window.FbxByModelIdRequested += OnFbxByModelIdRequested;
                _window.CurrentBattleBackgroundDumpRequested += OnCurrentBattleBackgroundDumpRequested;
                _window.BattleScriptDumpRequested += OnBattleScriptDumpRequested;
                _window.SfxDumpRequested += OnSfxDumpRequested;
                _window.WorldMapTextureDumpRequested += OnWorldMapTextureDumpRequested;
                _window.WarpRequested += OnWarpRequested;
                _window.SoundBrowserOpened += OnSoundBrowserOpened;
                _window.SoundPreviewRequested += OnSoundPreviewRequested;
                _window.SoundStopRequested += OnSoundStopRequested;
                _window.SoundSfxLoadRequested += OnSoundSfxLoadRequested;
                _window.SoundAliasSaveRequested += OnSoundAliasSaveRequested;
                _window.SaveStateCaptureRequested += OnSaveStateCaptureRequested;
                _window.SaveStateLoadRequested += OnSaveStateLoadRequested;
                _window.SaveStateDeleteRequested += OnSaveStateDeleteRequested;
                _window.SaveStatesPageOpened += OnSaveStatesPageOpened;

                _compileService = new CompileService(
                    AppendCompileOutput,
                    OnCompileCompleted);

                _fieldStateService = new FieldStateService();
                _fieldStateService.BeforeSnapshotLoad += PrepareForSnapshotLoad;
                _fieldStateService.Initialize();
                _modelViewerService = new ModelViewerService(_fieldStateService);
                _resolutionUiSyncService = new ResolutionUiSyncService();
                _assetDumpService = new AssetDumpService();
                _warpService = new WarpService();
                _soundBrowserService = new SoundBrowserService();

                if (!_window.Start())
                    Log.Error("[Dev Console] Native menu window thread failed to start.");

                _worldEntranceEditor = new WorldEntranceEditorService(_window);
                _battleSceneEditor = new BattleSceneEditorService(_window);
                _visible = false;

            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Controller Awake FAILED: " + ex);
            }
        }

        private void OnGUI()
        {
            if (_soundBrowserView != null) { _soundBrowserView.Draw(); return; }
            if (_modelViewerService != null) { _modelViewerService.DrawSpsSearch(); _modelViewerService.DrawSfxPreview(); _modelViewerService.DrawFieldViewer(); }
        }

        private void Update()
        {
            if (_shuttingDown) return;
            try
            {
                // Process release/input before observational work such as log IO.
                // An editor error or a flooded log must not strand MDC's pause.
                UpdateConsoleFreeze();
                if (_soundBrowserView != null)
                {
                    _soundBrowserView.Update();
                    if (_soundBrowserView.ExitRequested || (_window != null && _window.IsVisible))
                    {
                        _soundBrowserView.Dispose();
                        _soundBrowserView = null;
                        _viewerExitInputPending = true;
                        _viewerExitFrame = Time.frameCount;
                        _window.ShowHome();
                    }
                }
                if (_battleSceneEditor != null) _battleSceneEditor.Update();
                SceneTransitionAudio.Update();
                if (_resolutionUiSyncService != null)
                    _resolutionUiSyncService.Update();

                if (_logService != null)
                {
                    _logService.Update();

                    if (_window != null && _window.IsReady)
                    {
                        String chunk = _logService.DrainPendingText();

                        if (!String.IsNullOrEmpty(chunk))
                            _window.AppendLiveLog(chunk);
                    }
                }

                if (_window != null)
                {
                    String nativeError = _window.LastError;

                    if (!String.IsNullOrEmpty(nativeError) &&
                        nativeError != _lastNativeError)
                    {
                        _lastNativeError = nativeError;
                        Log.Error("[Dev Console] Native window error: " + nativeError);
                    }
                }

                if (_worldEntranceEditor != null) _worldEntranceEditor.Update();
                if (_fieldStateMoveBackwardRequested)
                {
                    _fieldStateMoveBackwardRequested = false;
                    ProcessFieldStateMoveBackward();
                }

                if (_fieldStateMoveForwardRequested)
                {
                    _fieldStateMoveForwardRequested = false;
                    ProcessFieldStateMoveForward();
                }
                if (_fieldStateSetCheckpointRequested)
                {
                    _fieldStateSetCheckpointRequested = false;
                    ProcessFieldStateSetCheckpoint();
                }
                if (_fieldStateLoadCheckpointRequested)
                {
                    _fieldStateLoadCheckpointRequested = false;
                    ProcessFieldStateLoadCheckpoint();
                }

                if (_fieldStateService != null)
                {
                    Boolean suspendCapture = _soundBrowserView != null || (_window != null && _window.IsVisible && !_window.IsLiveLogActive) ||
                                             (_modelViewerService != null && _modelViewerService.IsActive);
                    _fieldStateService.SetCaptureSuspended(suspendCapture);
                    _fieldStateService.Update();
                }

                // F10 can reveal MDC while a viewer still owns the scene.
                // Fully close it before MDC can return to the game.
                if (_modelViewerService != null && _modelViewerService.IsActive &&
                    _window != null && _window.IsVisible && !_modelViewerToggleRequested)
                    _modelViewerService.ReturnHomeRequested = true;

                if (_modelViewerService != null && _modelViewerService.ExitRequested)
                    _modelViewerToggleRequested = true;

                if (_modelViewerService != null && !_modelViewerToggleRequested)
                    _modelViewerService.UpdateRuntime();

                if (_modelViewerToggleRequested)
                {
                    _modelViewerToggleRequested = false;

                    if (_modelViewerService != null && _window != null)
                    {
                        Boolean returnHome = _modelViewerService.ReturnHomeRequested;
                        _modelViewerService.ReturnHomeRequested = false;
                        Boolean wasActive = _modelViewerService.IsActive;
                        Int32 viewerKind = _viewerKindRequested;
                        _viewerKindRequested = 0;
                        String modelViewerResult = viewerKind == 0 ? _modelViewerService.Toggle() : _modelViewerService.OpenViewer(viewerKind);
                        _window.SetModelViewerStatus(modelViewerResult);
                        if (returnHome && !_modelViewerService.IsActive) _window.ShowHome();
                        else if (_modelViewerService.IsActive || wasActive) _window.Hide();
                        else _window.ShowHome();
                        if (wasActive && !_modelViewerService.IsActive)
                        {
                            // A native-console Escape can arrive on key-down. Do not
                            // pass that same press to the restored battle pause menu.
                            _viewerExitInputPending = true;
                            _viewerExitFrame = Time.frameCount;
                        }
                        UpdateConsoleFreeze();
                    }
                }

                if (_resetToLauncherRequested)
                {
                    _resetToLauncherRequested = false;
                    _hardResetRequested = false;

                    try
                    {
                        if (_fieldStateService != null)
                            _fieldStateService.PersistHistoryForRestart();

                        HardResetService.RestartToLauncher();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Dev Console] Reset to Launcher failed: " + ex);
                    }
                }
                else if (_hardResetRequested)
                {
                    _hardResetRequested = false;

                    try
                    {
                        if (_fieldStateService != null)
                            _fieldStateService.PersistHistoryForRestart();

                        HardResetService.Restart();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Dev Console] Hard reset failed: " + ex);
                    }
                }

                if (_compileRequested)
                {
                    _compileRequested = false;

                    if (_compileService == null)
                    {
                        AppendCompileOutput("Compile service is not available.\r\n");
                    }
                    else if (!_compileService.Start())
                    {
                        AppendCompileOutput("Compiler is already running.\r\n");
                    }
                }


                if (_assetDumpRequested != 0)
                {
                    Int32 dumpRequest = _assetDumpRequested;
                    _assetDumpRequested = 0;

                    if (_assetDumpService == null || _window == null)
                    {
                        SetAssetDumpStatus("Asset dump service is not available.");
                    }
                    else if (_assetDumpService.IsRunning)
                    {
                        SetAssetDumpStatus("A complete-game asset dump is already running. Please let it finish.");
                    }
                    else if (dumpRequest == 1)
                    {
                        StartCoroutine(_assetDumpService.DumpFieldBackgrounds(SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 2)
                    {
                        StartCoroutine(_assetDumpService.DumpModelTextures(SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 11)
                    {
                        if (PersistenSingleton<FF9StateSystem>.Instance == null ||
                            PersistenSingleton<FF9StateSystem>.Instance.mode != 1 ||
                            PersistenSingleton<UIManager>.Instance == null ||
                            PersistenSingleton<UIManager>.Instance.UnityScene != UIManager.Scene.Field)
                            SetAssetDumpStatus("Load a field before using Export Current Loaded Field.");
                        else
                        {
                            Int32 id = FF9StateSystem.Common.FF9.fldMapNo;
                            _window.SetFieldCreatorSource(id);
                            StartCoroutine(_assetDumpService.DumpFieldBackground(id, SetAssetDumpStatus));
                        }
                    }
                    else if (dumpRequest == 3)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        StartCoroutine(_assetDumpService.DumpFieldBackground(id, SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 8)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        StartCoroutine(_assetDumpService.ExportFieldComposite(id, SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 9)
                    {
                        Int32 id = _assetLookupId;
                        String imagePath = _assetCutImagePath;
                        _assetLookupId = -1;
                        _assetCutImagePath = null;
                        StartCoroutine(_assetDumpService.CutFieldComposite(id, imagePath, SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 12)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        SetAssetDumpStatus(_modelViewerService == null
                            ? "Model export service is not available."
                            : _modelViewerService.DumpFbxByModelId(id));
                    }
                    else if (dumpRequest == 4)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        StartCoroutine(_assetDumpService.DumpModelTexture(id, SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 5)
                    {
                        StartCoroutine(_assetDumpService.DumpCurrentBattleBackground(SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 6)
                    {
                        SetAssetDumpStatus("Dumping battle script bytes. The game may pause briefly...");
                        BattleIDByteDumpService.Update();
                        SetAssetDumpStatus("Battle Script byte dump complete.\r\nOutput: Memoria Dev Console\\Asset Dump\\BattleScriptDump");
                    }
                    else if (dumpRequest == 10)
                    {
                        StartCoroutine(_assetDumpService.DumpWorldMapTextures(SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 7)
                    {
                        SetAssetDumpStatus("Dumping native SFX reference data. The game may pause while assets are scanned...");
                        SFXDumpService.Update();
                        SetAssetDumpStatus("SFX dump complete.\r\nOutput: Memoria Dev Console\\Asset Dump\\SFXDump");
                    }
                }

                if (_warpRequested)
                {
                    _warpRequested = false;

                    String result;
                    if (_warpService == null)
                        result = "Warp service is not available.";
                    else
                        result = _warpService.WarpToPreset(_warpPreset);

                    _warpPreset = null;

                    Log.Message("[Dev Console][Warp] " + result);
                    if (_window != null)
                        _window.SetWarpStatus(result);
                }

                if (_soundCatalogRequested)
                {
                    _soundCatalogRequested = false;

                    if (_soundBrowserService == null || _window == null)
                    {
                        if (_window != null)
                            _window.SetSoundBrowserStatus("Sound browser service is unavailable.");
                    }
                    else
                    {
                        try
                        {
                            DevSoundCatalog catalog = _soundBrowserService.BuildCatalog();
                            if (_soundBrowserView != null) _soundBrowserView.Dispose();
                            _soundBrowserView = new SoundBrowserView(_soundBrowserService, catalog);
                            _window.HideForGame();
                            UpdateConsoleFreeze();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Dev Console][Sound Browser] Catalog failed: " + ex);
                            _window.SetSoundBrowserStatus("Could not build audio catalog: " + ex.Message);
                        }
                    }
                }

                if (_soundSfxLoadRequested)
                {
                    _soundSfxLoadRequested = false;
                    Int32 sfxId = _soundSfxLoadId;
                    _soundSfxLoadId = -1;

                    String result = _soundBrowserService != null
                        ? _soundBrowserService.LoadSfxGroup(sfxId)
                        : "Sound browser service is unavailable.";

                    if (_window != null)
                        _window.SetSoundBrowserStatus(result);
                }

                if (_soundPreviewRequested)
                {
                    _soundPreviewRequested = false;
                    DevSoundEntry entry = _soundPreviewEntry;
                    _soundPreviewEntry = null;

                    String result = _soundBrowserService != null
                        ? _soundBrowserService.Preview(entry)
                        : "Sound browser service is unavailable.";

                    if (_window != null)
                        _window.SetSoundBrowserStatus(result);
                }

                if (_soundStopRequested)
                {
                    _soundStopRequested = false;

                    String result = _soundBrowserService != null
                        ? _soundBrowserService.StopAll()
                        : "Sound browser service is unavailable.";

                    if (_window != null)
                        _window.SetSoundBrowserStatus(result);
                }

                if (_soundBrowserService != null)
                {
                    String previewStatus = _soundBrowserService.UpdatePreview();
                    if (previewStatus != null && _soundBrowserView != null) _soundBrowserView.Status = previewStatus;
                    if (previewStatus != null && _window != null && _soundBrowserView == null)
                        _window.SetSoundBrowserStatus(previewStatus);
                }

                if (_soundAliasSaveRequested)
                {
                    _soundAliasSaveRequested = false;
                    DevSoundEntry entry = _soundAliasEntry;
                    String alias = _soundAliasText;
                    _soundAliasEntry = null;
                    _soundAliasText = null;

                    String result = _soundBrowserService != null
                        ? _soundBrowserService.SaveAlias(entry, alias)
                        : "Sound browser service is unavailable.";
                    if (_window != null)
                        _window.SetSoundBrowserStatus(result);
                }

                if (_saveStateListRequested)
                {
                    _saveStateListRequested = false;

                    if (_window != null && _fieldStateService != null)
                        _window.SetNamedSaveList(_fieldStateService.GetNamedSaves());
                }

                if (_saveStateCaptureRequested)
                {
                    _saveStateCaptureRequested = false;
                    String title = _saveStateCaptureTitle;
                    _saveStateCaptureTitle = null;

                    StartCoroutine(CaptureNamedSaveScreenshotThenSave(title));
                }

                if (_saveStateLoadRequested)
                {
                    _saveStateLoadRequested = false;
                    String loadId = _saveStateLoadId;
                    _saveStateLoadId = null;

                    String result = _fieldStateService != null
                        ? _fieldStateService.LoadNamedSave(loadId)
                        : "Save state service is unavailable.";

                    if (_window != null)
                    {
                        _window.SetNamedSaveStatus(result);
                        _window.SetNamedSaveList(_fieldStateService != null
                            ? _fieldStateService.GetNamedSaves()
                            : new System.Collections.Generic.List<NamedSaveSummary>());
                    }
                }

                if (_saveStateDeleteRequested)
                {
                    _saveStateDeleteRequested = false;
                    String deleteId = _saveStateDeleteId;
                    _saveStateDeleteId = null;

                    String result = _fieldStateService != null
                        ? _fieldStateService.DeleteNamedSave(deleteId)
                        : "Save state service is unavailable.";

                    if (_window != null)
                    {
                        _window.SetNamedSaveStatus(result);
                        _window.SetNamedSaveList(_fieldStateService != null
                            ? _fieldStateService.GetNamedSaves()
                            : new System.Collections.Generic.List<NamedSaveSummary>());
                    }
                }

                if (_restartPending && DateTime.UtcNow >= _restartNotBeforeUtc)
                {
                    _restartPending = false;


                    try
                    {
                        HardResetService.Restart();
                    }
                    catch (Exception ex)
                    {
                        AppendCompileOutput(
                            "\r\nHard restart FAILED.\r\n" +
                            ex + "\r\n");

                        Log.Error("[Dev Console] Hard restart failed.");
                        Log.Error(ex.ToString());
                    }
                }


            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Update FAILED: " + ex);
            }
        }

        private void RestoreConsoleInput()
        {
            if(_modelViewerService!=null)_modelViewerService.SetConsoleMenuInputBlocked(false);
            if(_frozenUiKeyTrigger!=null)_frozenUiKeyTrigger.enabled=_savedUiKeyTriggerEnabled;
            _frozenUiKeyTrigger=null;
        }
        private void LateUpdate()
        {
            Boolean consoleOpen = !_shuttingDown &&
                ((_window != null && _window.IsVisible) || _soundBrowserView != null ||
                 (_modelViewerService != null && _modelViewerService.IsActive));
            if (!consoleOpen)
            {
                ReleaseGameMusic();
                return;
            }

            ISdLibAPI api = ISdLibAPIProxy.Instance;
            _pausedGameMusic.RemoveWhere(id => api.SdSoundSystem_SoundCtrl_IsExist(id) == 0);

            // Title/menu tracks and their crossfades use MusicPlayer.
            MusicPlayer player = SoundLib.MusicPlayer;
            if (player != null)
            {
                PauseGameMusic(api, player.GetActiveSoundID());
                SoundProfile upcoming = UpcomingMusic?.GetValue(player) as SoundProfile;
                if (upcoming != null) PauseGameMusic(api, upcoming.SoundID);
            }

            // Field, world and battle scripts dispatch BGM through a separate player.
            AllSoundDispatchPlayer dispatch = SoundLib.AllSoundDispatchPlayer;
            SoundDatabase songs = dispatch == null ? null : DispatchMusic?.GetValue(dispatch) as SoundDatabase;
            if (songs != null)
                foreach (SoundProfile song in songs.ReadAll().Values)
                    PauseGameMusic(api, song.SoundID);
        }

        private void PauseGameMusic(ISdLibAPI api, Int32 id)
        {
            if (id <= 0 || api.SdSoundSystem_SoundCtrl_IsExist(id) == 0 || _pausedGameMusic.Contains(id))
                return;
            api.SdSoundSystem_SoundCtrl_SetPause(id, 1, 0);
            _pausedGameMusic.Add(id);
        }

        private void ReleaseGameMusic()
        {
            if (_pausedGameMusic.Count == 0) return;
            ISdLibAPI api = ISdLibAPIProxy.Instance;
            // Release only the pause added by MDC; the audio backend stacks pauses.
            foreach (Int32 id in _pausedGameMusic)
                if (api.SdSoundSystem_SoundCtrl_IsExist(id) != 0)
                    api.SdSoundSystem_SoundCtrl_SetPause(id, 0, 0);
            _pausedGameMusic.Clear();
        }

        private void OnDisable()
        {
            ReleaseGameMusic();
        }

        private void UpdateConsoleFreeze()
        {
            if (_window == null)
                return;

            Boolean viewerActive = _modelViewerService != null && _modelViewerService.IsActive;
            if (_viewerExitInputPending && !Input.GetKey(KeyCode.Escape) && !Input.GetKey(KeyCode.F12) && !Input.GetKey(KeyCode.F10) && Time.frameCount > _viewerExitFrame)
                _viewerExitInputPending = false;
            // The battle editor is live: Freeze Camera owns only the rendered view,
            // not HonoBehaviorSystem or battle input. Other tool pages keep their pause.
            Boolean blockMenu = _window.IsVisible || viewerActive || _soundBrowserView != null || _viewerExitInputPending;
            if (_modelViewerService != null) _modelViewerService.SetConsoleMenuInputBlocked(blockMenu);
            if (blockMenu)
            {
                UIKeyTrigger current=UnityEngine.Object.FindObjectOfType(typeof(UIKeyTrigger)) as UIKeyTrigger;
                if(current!=_frozenUiKeyTrigger)
                {
                    if(_frozenUiKeyTrigger!=null)_frozenUiKeyTrigger.enabled=_savedUiKeyTriggerEnabled;
                    _frozenUiKeyTrigger=current;
                    if(current!=null)_savedUiKeyTriggerEnabled=current.enabled;
                }
                if(_frozenUiKeyTrigger!=null)_frozenUiKeyTrigger.enabled=false;
            }
            else if(_frozenUiKeyTrigger!=null)
            {
                _frozenUiKeyTrigger.enabled=_savedUiKeyTriggerEnabled;
                _frozenUiKeyTrigger=null;
            }            Boolean consoleNeedsFreeze = _window.IsVisible && !_window.IsLiveLogActive && !_window.IsBattleSceneEditorActive;
            Boolean shouldFreeze = consoleNeedsFreeze || viewerActive || _soundBrowserView != null || _viewerExitInputPending;

            if (shouldFreeze && !_runtimeFreezeApplied)
            {
                // Disable only the components owned at entry. Re-freezing replacement
                // components during a scene load can prevent that scene from starting.
                _frozenHonoSystem = UnityEngine.Object.FindObjectOfType(typeof(HonoBehaviorSystem)) as HonoBehaviorSystem;
                if (_frozenHonoSystem != null)
                {
                    _savedHonoSystemEnabled = _frozenHonoSystem.enabled;
                    _frozenHonoSystem.enabled = false;
                }
                _runtimeFreezeApplied = true;
            }
            else if (!shouldFreeze && _runtimeFreezeApplied)
            {
                // Restore exactly what was enabled before capture. No UI state transition,
                // scene reload, EventEngine replacement, or synthetic input.
                if (_frozenHonoSystem != null)
                    _frozenHonoSystem.enabled = _savedHonoSystemEnabled;


                _frozenHonoSystem = null;

                _runtimeFreezeApplied = false;
            }
        }

        private void PrepareForSnapshotLoad()
        {
            if (_soundBrowserView != null) { _soundBrowserView.Dispose(); _soundBrowserView = null; }
            // Complete the UI hide before releasing freeze so the next Update cannot
            // freeze the new scene again. All snapshot loads reach this on Unity's thread.
            if (_modelViewerService != null && _modelViewerService.IsActive)
                _modelViewerService.Shutdown();
            if (_window != null) _window.Hide();
            UpdateConsoleFreeze();
        }
        private void OnFieldStatePageOpened()
        {
            if (_fieldStateService == null || _window == null)
                return;

            _window.SetFieldStateStatus(_fieldStateService.BuildStatusText());
        }

        private void OnFieldStateMoveBackwardRequested() { _fieldStateMoveBackwardRequested = true; }

        private void ProcessFieldStateMoveBackward()
        {
            if (_fieldStateService == null || _window == null)
                return;

            String result = _fieldStateService.MoveBackward();

            if (result == "OUT_OF_HISTORY_BACK")
            {
                _window.ShowFieldStateNotice(
                    "Out of Field Snapshots",
                    "No older field snapshots are saved.");
            }

            _window.SetFieldStateStatus(_fieldStateService.BuildStatusText());
        }

        private void OnFieldStateMoveForwardRequested() { _fieldStateMoveForwardRequested = true; }

        private void ProcessFieldStateMoveForward()
        {
            if (_fieldStateService == null || _window == null)
                return;

            String result = _fieldStateService.MoveForward();

            if (result == "OUT_OF_HISTORY_FORWARD")
            {
                _window.ShowFieldStateNotice(
                    "Out of Field Snapshots",
                    "No newer field snapshots are saved.");
            }

            _window.SetFieldStateStatus(_fieldStateService.BuildStatusText());
        }

        private void OnFieldStateSetCheckpointRequested() { _fieldStateSetCheckpointRequested = true; }

        private void ProcessFieldStateSetCheckpoint()
        {
            if (_fieldStateService == null || _window == null)
                return;

            String result = _fieldStateService.SetCheckpoint();

            if (result == "CHECKPOINT_SET")
            {
                _window.ShowFieldStateNotice(
                    "Checkpoint Set",
                    _fieldStateService.GetCheckpointStatus());
            }
            else
            {
                _window.ShowFieldStateNotice(
                    "Checkpoint Unavailable",
                    "A checkpoint can only be set while a valid field state is loaded.");
            }

            _window.SetFieldStateStatus(_fieldStateService.BuildStatusText());
        }

        private void OnFieldStateLoadCheckpointRequested() { _fieldStateLoadCheckpointRequested = true; }

        private void ProcessFieldStateLoadCheckpoint()
        {
            if (_fieldStateService == null || _window == null)
                return;

            String result = _fieldStateService.LoadCheckpoint();

            if (result == "NO_CHECKPOINT")
            {
                _window.ShowFieldStateNotice(
                    "No Checkpoint",
                    "Set a checkpoint before trying to load one.");
            }
            else if (result == "CHECKPOINT_UNAVAILABLE")
            {
                _window.ShowFieldStateNotice(
                    "Checkpoint Unavailable",
                    "The saved checkpoint could not be loaded.");
            }

            _window.SetFieldStateStatus(_fieldStateService.BuildStatusText());
        }

        private void OnHardResetRequested()
        {
            // Native console events originate on the Win32 window thread.
            // Persisting live FFIX state and quitting Unity must happen in Update().
            _hardResetRequested = true;
        }

        private void OnResetToLauncherRequested()
        {
            // Same rule as Hard Reset: marshal all Unity work to the main thread.
            _resetToLauncherRequested = true;
        }

        private void OnViewerRequested(Int32 kind)
        {
            if (_window != null) _window.Hide();
            _viewerKindRequested = kind;
            _modelViewerToggleRequested = true;
        }
        private void OnModelViewerRequested()
        {
            // Native console events originate on the Win32 window thread.
            // Hide immediately on the window thread; load Unity assets on its main thread.
            if (_window != null) _window.Hide();
            _modelViewerToggleRequested = true;
        }


        private void OnCurrentFieldDumpRequested()
        {
            // Read the active field on Unity's thread, never in the native UI callback.
            _assetDumpRequested = 11;
        }

        private void OnTextureDumpRequested()
        {
            _assetDumpRequested = 2;
        }

        private void OnFieldBackgroundByIdRequested(Int32 fieldId)
        {
            _assetLookupId = fieldId;
            _assetDumpRequested = 3;
        }

        private void OnFieldCompositeByIdRequested(Int32 fieldId)
        {
            _assetLookupId = fieldId;
            _assetDumpRequested = 8;
        }

        private void OnFieldCompositeCutRequested(Int32 fieldId, String imagePath)
        {
            _assetLookupId = fieldId;
            _assetCutImagePath = imagePath;
            _assetDumpRequested = 9;
        }

        private void OnFbxByModelIdRequested(Int32 modelId)
        {
            _assetLookupId = modelId;
            _assetDumpRequested = 12;
        }
        private void OnTextureByModelIdRequested(Int32 modelId)
        {
            _assetLookupId = modelId;
            _assetDumpRequested = 4;
        }

        private void OnCurrentBattleBackgroundDumpRequested()
        {
            _assetDumpRequested = 5;
        }

        private void OnBattleScriptDumpRequested()
        {
            _assetDumpRequested = 6;
        }

        private void OnWorldMapTextureDumpRequested() { _assetDumpRequested = 10; }

        private void OnSfxDumpRequested()
        {
            _assetDumpRequested = 7;
        }

        private void OnWarpRequested(StoryWarpPreset preset)
        {
            // Native console callbacks run on the Win32 window thread.
            // Defer every FFIX/Unity state mutation to Update().
            _warpPreset = preset;
            _warpRequested = true;
        }

        private void OnSoundBrowserOpened()
        {
            _soundCatalogRequested = true;
        }

        private void OnSoundPreviewRequested(DevSoundEntry entry)
        {
            _soundPreviewEntry = entry;
            _soundPreviewRequested = true;
        }

        private void OnSoundStopRequested()
        {
            _soundStopRequested = true;
        }

        private void OnSoundSfxLoadRequested(Int32 sfxId)
        {
            _soundSfxLoadId = sfxId;
            _soundSfxLoadRequested = true;
        }

        private void OnSoundAliasSaveRequested(DevSoundEntry entry, String alias)
        {
            _soundAliasEntry = entry;
            _soundAliasText = alias;
            _soundAliasSaveRequested = true;
        }

        private void OnSaveStateCaptureRequested(String title)
        {
            _saveStateCaptureTitle = title;
            _saveStateCaptureRequested = true;
        }

        private void OnSaveStateLoadRequested(String id)
        {
            _saveStateLoadId = id;
            _saveStateLoadRequested = true;
        }

        private void OnSaveStateDeleteRequested(String id)
        {
            _saveStateDeleteId = id;
            _saveStateDeleteRequested = true;
        }

        private void OnSaveStatesPageOpened()
        {
            _saveStateListRequested = true;
        }

        // Screenshot capture needs a real frame boundary (WaitForEndOfFrame), so it can
        // only happen from a coroutine on Unity's main thread/render loop - never from the
        // Win32 console thread that raised the request.
        private IEnumerator CaptureNamedSaveScreenshotThenSave(String title)
        {
            yield return new WaitForEndOfFrame();

            Byte[] png = null;
            try
            {
                // ScreenCapture.CaptureScreenshotAsTexture() isn't available under Memoria's
                // compiler references (its module isn't on the referenced assembly list).
                // Texture2D.ReadPixels against the backbuffer right after WaitForEndOfFrame
                // is the older, more widely-available way to get the same result.
                Int32 width = Screen.width;
                Int32 height = Screen.height;
                Texture2D shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();
                png = shot.EncodeToPNG();
                UnityEngine.Object.Destroy(shot);
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Save state screenshot capture failed: " + ex);
            }

            String result = _fieldStateService != null
                ? _fieldStateService.SaveNamedState(title, png)
                : "Save state service is unavailable.";

            if (_window != null)
            {
                _window.SetNamedSaveStatus(result);
                _window.SetNamedSaveList(_fieldStateService != null
                    ? _fieldStateService.GetNamedSaves()
                    : new System.Collections.Generic.List<NamedSaveSummary>());
            }
        }

        private void SetAssetDumpStatus(String text)
        {
            if (_window != null)
                _window.SetAssetDumpStatus(text);
        }

        private void OnCompileRestartRequested()
        {
            _compileRequested = true;
        }

        private void OnCompileCompleted(Boolean success)
        {
            if (_shuttingDown) return;
            if (success)
            {
                if (_fieldStateService != null)
                {
                    String historyResult = _fieldStateService.PersistHistoryForRestart();
                    AppendCompileOutput("\r\n" + historyResult + "\r\n");
                }

                _restartNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(1200);
                _restartPending = true;
            }
        }

        private void AppendCompileOutput(String text)
        {
            if (_window != null)
                _window.AppendCompileOutput(text);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (_shuttingDown) return;
            if (_battleSceneEditor != null) _battleSceneEditor.Dispose();
            _shuttingDown = true;
            ReleaseGameMusic();
            _restartPending = false;
            _hardResetRequested = false;
            _resetToLauncherRequested = false;
            _compileRequested = false;
            _modelViewerToggleRequested = false;
            HardResetService.BeginShutdown();

            // Release frozen systems and viewer state without loading scenes or
            // launching another process. Both quit and destroy can call this method.
            try
            {
                if (_modelViewerService != null && _modelViewerService.IsActive)
                {
                    _modelViewerService.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to cleanly exit Model Viewer during shutdown: " + ex);
            }

            if(_modelViewerService!=null || !System.Object.ReferenceEquals(_frozenUiKeyTrigger,null)) RestoreConsoleInput();
            if (_runtimeFreezeApplied)
            {
                try
                {
                    if (_frozenHonoSystem != null)
                        _frozenHonoSystem.enabled = _savedHonoSystemEnabled;
                    if (_frozenUiKeyTrigger != null)
                        _frozenUiKeyTrigger.enabled = _savedUiKeyTriggerEnabled;
                }
                catch (Exception ex)
                {
                    Log.Error("[Dev Console] Failed to restore frozen systems during shutdown: " + ex);
                }

                _frozenHonoSystem = null;

                _runtimeFreezeApplied = false;
            }

            if (_fieldStateService != null)
                _fieldStateService.BeforeSnapshotLoad -= PrepareForSnapshotLoad;

            if (_window != null)
            {
                _window.CompileRestartRequested -= OnCompileRestartRequested;
                _window.ModelViewerRequested -= OnModelViewerRequested;
                _window.ViewerRequested -= OnViewerRequested;
                _window.HardResetRequested -= OnHardResetRequested;
                _window.ResetToLauncherRequested -= OnResetToLauncherRequested;
                _window.CurrentFieldDumpRequested -= OnCurrentFieldDumpRequested;
                _window.TextureDumpRequested -= OnTextureDumpRequested;
                _window.FieldBackgroundByIdRequested -= OnFieldBackgroundByIdRequested;
                _window.FieldCompositeByIdRequested -= OnFieldCompositeByIdRequested;
                _window.FieldCompositeCutRequested -= OnFieldCompositeCutRequested;
                _window.TextureByModelIdRequested -= OnTextureByModelIdRequested;
                _window.FbxByModelIdRequested -= OnFbxByModelIdRequested;
                _window.CurrentBattleBackgroundDumpRequested -= OnCurrentBattleBackgroundDumpRequested;
                _window.BattleScriptDumpRequested -= OnBattleScriptDumpRequested;
                _window.SfxDumpRequested -= OnSfxDumpRequested;
                _window.WorldMapTextureDumpRequested -= OnWorldMapTextureDumpRequested;
                _window.WarpRequested -= OnWarpRequested;
                _window.SoundBrowserOpened -= OnSoundBrowserOpened;
                _window.SoundPreviewRequested -= OnSoundPreviewRequested;
                _window.SoundStopRequested -= OnSoundStopRequested;
                _window.SoundSfxLoadRequested -= OnSoundSfxLoadRequested;
                _window.SoundAliasSaveRequested -= OnSoundAliasSaveRequested;
                _window.SaveStateCaptureRequested -= OnSaveStateCaptureRequested;
                _window.SaveStateLoadRequested -= OnSaveStateLoadRequested;
                _window.SaveStateDeleteRequested -= OnSaveStateDeleteRequested;
                _window.SaveStatesPageOpened -= OnSaveStatesPageOpened;
                _window.Dispose();
                _window = null;
            }

            if (_compileService != null)
            {
                _compileService.Dispose();
                _compileService = null;
            }

            if (_logService != null)
            {
                _logService.Dispose();
                _logService = null;
            }

            if (_soundBrowserView != null) { _soundBrowserView.Dispose(); _soundBrowserView = null; }
            if (_soundBrowserService != null)
            {
                _soundBrowserService.StopAll();
                _soundBrowserService = null;
            }
        }
    }
}
