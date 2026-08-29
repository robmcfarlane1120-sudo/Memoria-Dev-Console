using System;
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

        private Boolean _visible;
        private String _lastNativeError;

        private volatile Boolean _compileRequested;
        private volatile Boolean _modelViewerToggleRequested;
        private volatile Boolean _hardResetRequested;
        private volatile Boolean _resetToLauncherRequested;
        private volatile Int32 _assetDumpRequested;
        private volatile Int32 _assetLookupId = -1;
        private volatile Boolean _restartPending;
        private DateTime _restartNotBeforeUtc = DateTime.MinValue;

        private Boolean _runtimeFreezeApplied;
        private UIKeyTrigger _frozenUiKeyTrigger;
        private Boolean _savedUiKeyTriggerEnabled;
        private HonoBehaviorSystem _frozenHonoSystem;
        private Boolean _savedHonoSystemEnabled;

        private void Awake()
        {
            Log.Message("[Dev Console] Controller Awake entered.");

            try
            {
                _logService = new LiveLogService(800);
                _logService.Initialize();
                Log.Message("[Dev Console] LiveLogService ready.");

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

                _compileService = new CompileService(
                    AppendCompileOutput,
                    OnCompileCompleted);

                _fieldStateService = new FieldStateService();
                _fieldStateService.Initialize();
                _modelViewerService = new ModelViewerService(_fieldStateService);
                _resolutionUiSyncService = new ResolutionUiSyncService();
                _assetDumpService = new AssetDumpService();

                if (_window.Start())
                    Log.Message("[Dev Console] Native menu window thread started.");
                else
                    Log.Error("[Dev Console] Native menu window thread failed to start.");

                _visible = false;

                Log.Message("[Dev Console] Controller ready. Global F10 toggles menu.");
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

                        Log.Message("[Dev Console] Reset to Launcher requested.");
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

                        Log.Message("[Dev Console] Controller hard reset chord triggered.");
                        HardResetService.Restart();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Dev Console] Controller hard reset failed: " + ex);
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
                    else
                    {
                        Log.Message("[Dev Console] Compile All started.");
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
                        Log.Message("[Dev Console] Complete field background dump started.");
                        StartCoroutine(_assetDumpService.DumpFieldBackgrounds(SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 2)
                    {
                        Log.Message("[Dev Console] Complete model texture dump started.");
                        StartCoroutine(_assetDumpService.DumpModelTextures(SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 3)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        Log.Message("[Dev Console] Single field background dump requested: " + id);
                        StartCoroutine(_assetDumpService.DumpFieldBackground(id, SetAssetDumpStatus));
                    }
                    else if (dumpRequest == 4)
                    {
                        Int32 id = _assetLookupId;
                        _assetLookupId = -1;
                        Log.Message("[Dev Console] Single model texture dump requested: " + id);
                        StartCoroutine(_assetDumpService.DumpModelTexture(id, SetAssetDumpStatus));
                    }
                }

                if (_restartPending && DateTime.UtcNow >= _restartNotBeforeUtc)
                {
                    _restartPending = false;

                    Log.Message("[Dev Console] Compilation succeeded. Starting hard restart.");

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
                // merely lets the same controller press leak into another FFIX state.
                // Instead, disable FFIX's input dispatcher and Hono simulation loop at
                // their MonoBehaviour boundaries. The native console still reads XInput,
                // and ModelViewerService manually drives only ModelViewerScene.Update().
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
                Log.Message("[Dev Console][PASS14_17] FFIX input dispatcher + Hono simulation frozen without changing UIManager.State.");
            }
            else if (!shouldFreeze && _runtimeFreezeApplied)
            {
                // Restore exactly what was enabled before capture. No UI state transition,
                // scene reload, EventEngine replacement, or synthetic controller input.
                if (_frozenHonoSystem != null)
                    _frozenHonoSystem.enabled = _savedHonoSystemEnabled;
                if (_frozenUiKeyTrigger != null)
                    _frozenUiKeyTrigger.enabled = _savedUiKeyTriggerEnabled;

                _frozenHonoSystem = null;
                _frozenUiKeyTrigger = null;
                _runtimeFreezeApplied = false;
                Log.Message("[Dev Console][PASS14_17] FFIX input dispatcher + Hono simulation resumed in place.");
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
        }
    }
}
