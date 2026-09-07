using System;
using System.Collections;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed class DevConsoleController : MonoBehaviour
    {
        private LiveLogService _logService;
        private NativeConsoleWindow _window;
        private CompileService _compileService;
        private FieldStateService _fieldStateService;
        private ModelViewerService _modelViewerService;
        private ResolutionUiSyncService _resolutionUiSyncService;
        private AssetDumpService _assetDumpService;
        private WarpService _warpService;
        private SoundBrowserService _soundBrowserService;

        private Boolean _visible;
        private String _lastNativeError;

        private volatile Boolean _compileRequested;
        private volatile Boolean _modelViewerToggleRequested;
        private volatile Boolean _hardResetRequested;
        private volatile Boolean _resetToLauncherRequested;
        private volatile Int32 _assetDumpRequested;
        private volatile Int32 _assetLookupId = -1;
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

        private volatile Boolean _saveStateCaptureRequested;
        private String _saveStateCaptureTitle;
        private volatile Boolean _saveStateLoadRequested;
        private String _saveStateLoadId;
        private volatile Boolean _saveStateDeleteRequested;
        private String _saveStateDeleteId;
        private volatile Boolean _saveStateListRequested;

        private Boolean _runtimeFreezeApplied;
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
                _window.HardResetRequested += OnHardResetRequested;
                _window.ResetToLauncherRequested += OnResetToLauncherRequested;
                _window.FieldStateMoveBackwardRequested += OnFieldStateMoveBackwardRequested;
                _window.FieldStateMoveForwardRequested += OnFieldStateMoveForwardRequested;
            _window.FieldStateSetCheckpointRequested += OnFieldStateSetCheckpointRequested;
            _window.FieldStateLoadCheckpointRequested += OnFieldStateLoadCheckpointRequested;
                                _window.FieldStatePageOpened += OnFieldStatePageOpened;
                _window.FieldBackgroundDumpRequested += OnFieldBackgroundDumpRequested;
                _window.TextureDumpRequested += OnTextureDumpRequested;
                _window.FieldBackgroundByIdRequested += OnFieldBackgroundByIdRequested;
                _window.TextureByModelIdRequested += OnTextureByModelIdRequested;
                _window.CurrentBattleBackgroundDumpRequested += OnCurrentBattleBackgroundDumpRequested;
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
                _fieldStateService.Initialize();
                _modelViewerService = new ModelViewerService(_fieldStateService);
                _resolutionUiSyncService = new ResolutionUiSyncService();
                _assetDumpService = new AssetDumpService();
                _warpService = new WarpService();
                _soundBrowserService = new SoundBrowserService();

                if (!_window.Start())
                    Log.Error("[Dev Console] Native menu window thread failed to start.");

                _visible = false;

            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Controller Awake FAILED: " + ex);
            }
        }

        private void Update()
        {
            try
            {
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

                UpdateConsoleFreeze();

                if (_fieldStateService != null)
                {
                    Boolean suspendCapture = (_window != null && _window.IsVisible && !_window.IsLiveLogActive) ||
                                             (_modelViewerService != null && _modelViewerService.IsActive);
                    _fieldStateService.SetCaptureSuspended(suspendCapture);
                    _fieldStateService.Update();
                }

                if (_modelViewerService != null)
                    _modelViewerService.UpdateRuntime();

                if (_modelViewerToggleRequested)
                {
                    _modelViewerToggleRequested = false;

                    if (_modelViewerService != null && _window != null)
                    {
                        String modelViewerResult = _modelViewerService.Toggle();
                        _window.SetModelViewerStatus(modelViewerResult);
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
                    else if (dumpRequest == 3)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        StartCoroutine(_assetDumpService.DumpFieldBackground(id, SetAssetDumpStatus));
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
                            _window.SetSoundBrowserCatalog(catalog);
                            _window.SetSoundBrowserStatus(
                                "Audio metadata loaded. Press Enter to preview; press S to stop.");
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

        private void UpdateConsoleFreeze()
        {
            if (_window == null)
                return;

            Boolean viewerActive = _modelViewerService != null && _modelViewerService.IsActive;
            // Live Log is observational: keep FFIX fully playable while it is open.
            // Every other Dev Console page still freezes FFIX exactly as before.
            Boolean consoleNeedsFreeze = _window.IsVisible && !_window.IsLiveLogActive;
            Boolean shouldFreeze = consoleNeedsFreeze || viewerActive;

            if (shouldFreeze && !_runtimeFreezeApplied)
            {
                // Do NOT mutate UIManager.State here. UIKeyTrigger raises the Dev Console
                // update callback before it processes FFIX controls, so changing UI state
                // merely lets the same key press leak into another FFIX state.
                // Instead, disable FFIX's input dispatcher and Hono simulation loop at
                // their MonoBehaviour boundaries. The native console keeps reading its
                // global keyboard hook, and ModelViewerService drives ModelViewerScene.Update().
                _frozenUiKeyTrigger = UnityEngine.Object.FindObjectOfType(typeof(UIKeyTrigger)) as UIKeyTrigger;
                if (_frozenUiKeyTrigger != null)
                {
                    _savedUiKeyTriggerEnabled = _frozenUiKeyTrigger.enabled;
                    _frozenUiKeyTrigger.enabled = false;
                }

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
                if (_frozenUiKeyTrigger != null)
                    _frozenUiKeyTrigger.enabled = _savedUiKeyTriggerEnabled;

                _frozenHonoSystem = null;
                _frozenUiKeyTrigger = null;
                _runtimeFreezeApplied = false;
            }
        }

        private void OnFieldStatePageOpened()
        {
            if (_fieldStateService == null || _window == null)
                return;

            _window.SetFieldStateStatus(_fieldStateService.BuildStatusText());
        }

        private void OnFieldStateMoveBackwardRequested()
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

        private void OnFieldStateMoveForwardRequested()
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

        private void OnFieldStateSetCheckpointRequested()
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

        private void OnFieldStateLoadCheckpointRequested()
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

        private void OnModelViewerRequested()
        {
            // Native console events originate on the Win32 window thread.
            // Unity / ModelViewerScene work must happen on the Unity main thread.
            _modelViewerToggleRequested = true;
        }


        private void OnFieldBackgroundDumpRequested()
        {
            // Native window callbacks happen on the Win32 thread; defer Unity work to Update().
            _assetDumpRequested = 1;
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

        private void OnTextureByModelIdRequested(Int32 modelId)
        {
            _assetLookupId = modelId;
            _assetDumpRequested = 4;
        }

        private void OnCurrentBattleBackgroundDumpRequested()
        {
            _assetDumpRequested = 5;
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
            // If the game is closed while Model Viewer is open, HonoBehaviorSystem and
            // UIKeyTrigger are still disabled (see UpdateConsoleFreeze). Leaving them
            // disabled during Unity's own shutdown sequence is what was causing the game
            // to become unresponsive on quit ("locks up, have to Ctrl+Alt+Del"). Undo the
            // freeze and cleanly exit the viewer FIRST, before anything else runs, so the
            // rest of the engine's own shutdown/quit handling is never blocked on us.
            try
            {
                if (_modelViewerService != null && _modelViewerService.IsActive)
                {
                    _modelViewerService.Toggle();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Failed to cleanly exit Model Viewer during shutdown: " + ex);
            }

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
                _frozenUiKeyTrigger = null;
                _runtimeFreezeApplied = false;
            }

            if (_window != null)
            {
                _window.CompileRestartRequested -= OnCompileRestartRequested;
                _window.ModelViewerRequested -= OnModelViewerRequested;
                _window.HardResetRequested -= OnHardResetRequested;
                _window.ResetToLauncherRequested -= OnResetToLauncherRequested;
                _window.FieldBackgroundDumpRequested -= OnFieldBackgroundDumpRequested;
                _window.TextureDumpRequested -= OnTextureDumpRequested;
                _window.FieldBackgroundByIdRequested -= OnFieldBackgroundByIdRequested;
                _window.TextureByModelIdRequested -= OnTextureByModelIdRequested;
                _window.CurrentBattleBackgroundDumpRequested -= OnCurrentBattleBackgroundDumpRequested;
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

            if (_soundBrowserService != null)
            {
                _soundBrowserService.StopAll();
                _soundBrowserService = null;
            }
        }
    }
}
