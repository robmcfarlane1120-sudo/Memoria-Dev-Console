using System;
using System.Collections.Generic;
using System.Reflection;
using FF9;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private BattleHUD _savedBattleHud;
        private System.Object _savedBattleMap;
        private Boolean _hasBattleControlState, _savedBattlePaused;
        private String _savedBattleButtonGroup;
        private Int32 _battleResumeLogFrame = -1;
        private HonoluluBattleMain _suspendedBattleMain;
        private Boolean _savedBattleMainEnabled;
        private Boolean _savedBattleHudEnabled;
        private GameObject _savedBattleSelectedObject, _savedBattleActiveButton;
        private Int32 _battleResumeLogRemaining;
        private Single _battleResumeLogTime;
        private readonly Dictionary<String, System.Object> _savedBattleHudFields = new Dictionary<String, System.Object>();
        private readonly Dictionary<Animation, Boolean> _savedBattleAnimations = new Dictionary<Animation, Boolean>();

        private void SuspendBattleSimulation()
        {
            if (_suspendedBattleMain != null) return;
            _suspendedBattleMain = UnityEngine.Object.FindObjectOfType<HonoluluBattleMain>();
            if (_suspendedBattleMain == null)
                throw new InvalidOperationException("Cannot safely suspend battle: HonoluluBattleMain was not found.");
            _savedBattleMainEnabled = _suspendedBattleMain.enabled;
            _savedBattleHudEnabled = _savedBattleHud.enabled;
            // HonoluluBattleMain.Update is a regular Unity Update, not a Hono
            // behavior. Hono's console freeze does not stop battle/ATB/commands.
            _suspendedBattleMain.enabled = false;
            _savedBattleHud.enabled = false;
            foreach (var entry in _savedBattleAnimations)
                if (entry.Key != null) entry.Key.enabled = false;
            ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Suspended native battle Update and unit animations.");
        }

        private void RestoreBattleSimulation()
        {
            if (_suspendedBattleMain == null) return;
            if (_savedBattleHud != null) _savedBattleHud.enabled = _savedBattleHudEnabled;
            _suspendedBattleMain.enabled = _savedBattleMainEnabled;
            _suspendedBattleMain = null;
            _savedBattleHud = null;
            _savedBattleSelectedObject = _savedBattleActiveButton = null;
        }

        private static String BattleUnitSummary()
        {
            var units = new List<String>();
            foreach (BattleUnit unit in FF9StateSystem.Battle.FF9Battle.EnumerateBattleUnits())
                units.Add(unit.Id + ":hp=" + unit.Data.cur.hp + ",at=" + unit.Data.cur.at + "/" + unit.Data.max.at +
                    ",coef=" + unit.Data.cur.at_coef + ",enabled=" + unit.Data.bi.atb + ",sel=" + unit.Data.sel_mode);
            return String.Join("; ", units.ToArray());
        }

        private void SaveBattleControlState()
        {
            _hasBattleControlState = false;
            _savedBattleHudFields.Clear();
            _savedBattleAnimations.Clear();
            if (!_enteredFromBattle) return;
            _savedBattleHud = UIManager.Battle;
            _savedBattleMap = FF9StateSystem.Battle.FF9Battle.map;
            _savedBattlePaused = (FF9StateSystem.Battle.FF9Battle.attr & ff9btl.ATTR.NOPUTDISPENV) != 0;
            _savedBattleButtonGroup = ButtonGroupState.ActiveGroup;
            _savedBattleSelectedObject = UICamera.selectedObject;
            _savedBattleActiveButton = ButtonGroupState.ActiveButton;
            _savedUiState = PersistenSingleton<UIManager>.Instance.State;
            _hasSavedUiState = true;
            foreach (String name in new[] { "_commandEnable", "_beforePauseCommandEnable", "_isFromPause", "_hidingHud", "_currentButtonGroup" })
            {
                FieldInfo field = typeof(BattleHUD).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) throw new MissingFieldException(typeof(BattleHUD).FullName, name);
                _savedBattleHudFields.Add(name, field.GetValue(_savedBattleHud));
            }
            foreach (BattleUnit unit in FF9StateSystem.Battle.FF9Battle.EnumerateBattleUnits())
                if (unit.Data.animation != null) _savedBattleAnimations[unit.Data.animation] = unit.Data.animation.enabled;
            _hasBattleControlState = true;
            ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Captured controls: command=" + _savedBattleHud.FF9BMenu_IsEnable() +
                ", atb=" + _savedBattleHud.FF9BMenu_IsEnableAtb() + ", group='" + _savedBattleButtonGroup + "', paused=" + _savedBattlePaused);
            ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Captured units: " + BattleUnitSummary());
        }

        private void RestoreBattleControlState()
        {
            if (!_hasBattleControlState) return;
            try
            {
                // Never apply a snapshot to a replacement battle or a different HUD.
                if (!ReferenceEquals(_savedBattleMap, FF9StateSystem.Battle.FF9Battle.map) || _savedBattleHud != UIManager.Battle)
                    return;
                String before = "command=" + _savedBattleHud.FF9BMenu_IsEnable() + ", atb=" + _savedBattleHud.FF9BMenu_IsEnableAtb() +
                    ", group='" + ButtonGroupState.ActiveGroup + "'";
                RestoreSavedUiState();
                // Calling EnableMenu would initialize/reset the player's command menu.
                // Restore the captured fields instead, retaining the current selection.
                foreach (var entry in _savedBattleHudFields)
                    typeof(BattleHUD).GetField(entry.Key, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_savedBattleHud, entry.Value);
                if (_savedBattlePaused) FF9StateSystem.Battle.FF9Battle.attr |= ff9btl.ATTR.NOPUTDISPENV;
                else FF9StateSystem.Battle.FF9Battle.attr &= ~ff9btl.ATTR.NOPUTDISPENV;
                foreach (var entry in _savedBattleAnimations)
                    if (entry.Key != null) entry.Key.enabled = entry.Value;
                if (ButtonGroupState.ActiveGroup != _savedBattleButtonGroup)
                    ButtonGroupState.ActiveGroup = _savedBattleButtonGroup;
                if (_savedBattleActiveButton != null && _savedBattleActiveButton.activeInHierarchy)
                    ButtonGroupState.ActiveButton = _savedBattleActiveButton;
                if (_savedBattleSelectedObject != null && _savedBattleSelectedObject.activeInHierarchy)
                    UICamera.selectedObject = _savedBattleSelectedObject;
                ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Restored controls: " + before + " -> command=" +
                    _savedBattleHud.FF9BMenu_IsEnable() + ", atb=" + _savedBattleHud.FF9BMenu_IsEnableAtb() +
                    ", group='" + ButtonGroupState.ActiveGroup + "', paused=" + _savedBattlePaused);
                _battleResumeLogFrame = Time.frameCount + 3;
                _battleResumeLogTime = 0f;
                _battleResumeLogRemaining = 2;
            }
            finally
            {
                _hasBattleControlState = false;
                _hasSavedUiState = false;
                // Simulation restoration follows in the caller's finally; retain
                // the HUD reference until that component is enabled again.
                _savedBattleMap = null;
                _savedBattleHudFields.Clear();
                _savedBattleAnimations.Clear();
            }
        }

        private void LogBattleResumeState()
        {
            if (_battleResumeLogFrame < 0 || Time.frameCount < _battleResumeLogFrame || Time.realtimeSinceStartup < _battleResumeLogTime) return;
            if (IsActive || !IsBattleActive()) { _battleResumeLogFrame = -1; return; }
            if (--_battleResumeLogRemaining > 0) _battleResumeLogTime = Time.realtimeSinceStartup + 2f;
            else _battleResumeLogFrame = -1;
            HonoBehaviorSystem hono = UnityEngine.Object.FindObjectOfType<HonoBehaviorSystem>();
            HonoluluBattleMain main = UnityEngine.Object.FindObjectOfType<HonoluluBattleMain>();
            ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Resume check: hono=" + (hono != null && hono.enabled) +
                ", ui=" + PersistenSingleton<UIManager>.Instance.State + ", uiPause=" + PersistenSingleton<UIManager>.Instance.IsPause +
                ", command=" + UIManager.Battle.FF9BMenu_IsEnable() + ", atb=" + UIManager.Battle.FF9BMenu_IsEnableAtb() +
                ", nativePause=" + ((FF9StateSystem.Battle.FF9Battle.attr & ff9btl.ATTR.NOPUTDISPENV) != 0) +
                ", group='" + ButtonGroupState.ActiveGroup + "', counterATB=" + HonoluluBattleMain.counterATB +
                ", battleUpdate=" + (main != null && main.enabled) + ", debug=" + FF9StateSystem.Battle.isDebug +
                ", delta=" + Time.deltaTime + ", timeScale=" + Time.timeScale);
            UIKeyTrigger input = UnityEngine.Object.FindObjectOfType<UIKeyTrigger>();
            ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Resume input: trigger=" + (input != null && input.enabled) +
                ", hud=" + UIManager.Battle.enabled + ", selected=" + (UICamera.selectedObject == null ? "none" : UICamera.selectedObject.name) +
                ", phase=" + FF9StateSystem.Battle.FF9Battle.btl_phase + ", seq=" + FF9StateSystem.Battle.FF9Battle.btl_seq +
                ", command=" + (FF9StateSystem.Battle.FF9Battle.cur_cmd == null ? "none" : FF9StateSystem.Battle.FF9Battle.cur_cmd.cmd_no.ToString()));
            ViewerDiagnostics.Message("[Dev Console][Battle Viewer] Resume units: " + BattleUnitSummary());
        }
    }
}
