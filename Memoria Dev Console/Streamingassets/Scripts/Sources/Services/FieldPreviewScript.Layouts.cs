// Operand layouts transcribed from HWS Database_Script.cpp; execution is isolated.
using System.Collections.Generic;
namespace Memoria.DevConsole {
internal sealed partial class FieldPreviewScript {
    private sealed class Layout { internal bool Flag; internal int[] Width; internal Layout(bool flag, int[] width) { Flag=flag; Width=width; } }
    private static readonly Dictionary<int, Layout> Layouts = new Dictionary<int, Layout> {
            { 0x00, new Layout(false, new int[] {  }) }, // NOTHING
            { 0x01, new Layout(false, new int[] { 2 }) }, // JMP
            { 0x02, new Layout(false, new int[] { 2 }) }, // JMP_IFN
            { 0x03, new Layout(false, new int[] { 2 }) }, // JMP_IF
            { 0x04, new Layout(false, new int[] {  }) }, // return
            { 0x05, new Layout(false, new int[] { 0 }) }, // set
            { 0x07, new Layout(false, new int[] { 1, 1 }) }, // InitCode
            { 0x08, new Layout(false, new int[] { 1, 1 }) }, // InitRegion
            { 0x09, new Layout(false, new int[] { 1, 1 }) }, // InitObject
            { 0x0A, new Layout(false, new int[] {  }) }, // 0x0A
            { 0x0C, new Layout(false, new int[] {  }) }, // 0x0C
            { 0x0D, new Layout(false, new int[] {  }) }, // 0x0D
            { 0x0E, new Layout(false, new int[] {  }) }, // 0x0E
            { 0x0F, new Layout(false, new int[] {  }) }, // 0x0F
            { 0x10, new Layout(true, new int[] { 1, 1, 1 }) }, // RunScriptAsync
            { 0x11, new Layout(false, new int[] {  }) }, // 0x11
            { 0x12, new Layout(true, new int[] { 1, 1, 1 }) }, // RunScript
            { 0x13, new Layout(false, new int[] {  }) }, // 0x13
            { 0x14, new Layout(true, new int[] { 1, 1, 1 }) }, // RunScriptSync
            { 0x15, new Layout(false, new int[] {  }) }, // 0x15
            { 0x16, new Layout(true, new int[] { 1, 1 }) }, // RunScriptObjectAsync
            { 0x17, new Layout(false, new int[] {  }) }, // 0x17
            { 0x18, new Layout(true, new int[] { 1, 1 }) }, // RunScriptObject
            { 0x19, new Layout(false, new int[] {  }) }, // 0x19
            { 0x1A, new Layout(true, new int[] { 1, 1 }) }, // RunScriptObjectSync
            { 0x1B, new Layout(true, new int[] { 1 }) }, // ContinueBattleMusic
            { 0x1C, new Layout(true, new int[] { 1 }) }, // TerminateEntry
            { 0x1D, new Layout(true, new int[] { 2, 2 }) }, // CreateObject
            { 0x1E, new Layout(true, new int[] { 1, 2, 2, 2, 2 }) }, // SetCameraBounds
            { 0x1F, new Layout(true, new int[] { 1, 1, 2 }) }, // WindowSync
            { 0x20, new Layout(true, new int[] { 1, 1, 2 }) }, // WindowAsync
            { 0x21, new Layout(true, new int[] { 1 }) }, // CloseWindow
            { 0x22, new Layout(true, new int[] { 1 }) }, // Wait
            { 0x23, new Layout(true, new int[] { 2, 2 }) }, // Walk
            { 0x24, new Layout(true, new int[] { 1 }) }, // WalkTowardObject
            { 0x25, new Layout(false, new int[] {  }) }, // InitWalk
            { 0x26, new Layout(true, new int[] { 1 }) }, // SetWalkSpeed
            { 0x27, new Layout(true, new int[] { 1 }) }, // SetTriangleFlagMask
            { 0x28, new Layout(true, new int[] { 1, 1, 1, 1 }) }, // Cinematic
            { 0x2A, new Layout(true, new int[] { 1, 2 }) }, // Battle
            { 0x2B, new Layout(true, new int[] { 2 }) }, // Field
            { 0x2C, new Layout(false, new int[] {  }) }, // DefinePlayerCharacter
            { 0x2D, new Layout(false, new int[] {  }) }, // DisableMove
            { 0x2E, new Layout(false, new int[] {  }) }, // EnableMove
            { 0x2F, new Layout(true, new int[] { 2, 1 }) }, // SetModel
            { 0x30, new Layout(false, new int[] {  }) }, // 0x30
            { 0x31, new Layout(false, new int[] {  }) }, // 0x31
            { 0x32, new Layout(true, new int[] { 1, 1 }) }, // 0x32
            { 0x33, new Layout(true, new int[] { 2 }) }, // SetStandAnimation
            { 0x34, new Layout(true, new int[] { 2 }) }, // SetWalkAnimation
            { 0x35, new Layout(true, new int[] { 2 }) }, // SetRunAnimation
            { 0x36, new Layout(true, new int[] { 1 }) }, // TurnInstant
            { 0x37, new Layout(true, new int[] { 1, 1 }) }, // SetPitchAngle
            { 0x38, new Layout(true, new int[] { 1 }) }, // Attack
            { 0x39, new Layout(true, new int[] { 1, 1 }) }, // ShowObject
            { 0x3A, new Layout(true, new int[] { 1, 1 }) }, // HideObject
            { 0x3B, new Layout(true, new int[] { 1 }) }, // SetObjectIndex
            { 0x3C, new Layout(true, new int[] { 1, 2, 2, 2, 2 }) }, // SetRandomBattles
            { 0x3D, new Layout(true, new int[] { 1, 1 }) }, // SetAnimationInOut
            { 0x3E, new Layout(true, new int[] { 1 }) }, // SetAnimationSpeed
            { 0x3F, new Layout(true, new int[] { 1, 1 }) }, // SetAnimationFlags
            { 0x40, new Layout(true, new int[] { 2 }) }, // RunAnimation
            { 0x41, new Layout(false, new int[] {  }) }, // WaitAnimation
            { 0x42, new Layout(false, new int[] {  }) }, // StopAnimation
            { 0x43, new Layout(true, new int[] { 1 }) }, // RunSharedScript
            { 0x44, new Layout(false, new int[] {  }) }, // WaitSharedScript
            { 0x45, new Layout(false, new int[] {  }) }, // StopSharedScript
            { 0x46, new Layout(false, new int[] {  }) }, // 0x46
            { 0x47, new Layout(true, new int[] { 1 }) }, // EnableHeadFocus
            { 0x48, new Layout(true, new int[] { 2, 1 }) }, // AddItem
            { 0x49, new Layout(true, new int[] { 2, 1 }) }, // RemoveItem
            { 0x4A, new Layout(true, new int[] { 1, 2 }) }, // RunBattleCode
            { 0x4B, new Layout(true, new int[] { 1, 1, 1 }) }, // SetObjectLogicalSize
            { 0x4C, new Layout(true, new int[] { 1, 1, 1 }) }, // AttachObject
            { 0x4D, new Layout(true, new int[] { 1 }) }, // DetachObject
            { 0x4E, new Layout(false, new int[] {  }) }, // 0x4E
            { 0x4F, new Layout(true, new int[] { 1 }) }, // 0x4F
            { 0x50, new Layout(false, new int[] {  }) }, // WaitTurn
            { 0x51, new Layout(true, new int[] { 1, 1 }) }, // TurnTowardObject
            { 0x52, new Layout(true, new int[] { 2 }) }, // SetInactiveAnimation
            { 0x53, new Layout(false, new int[] {  }) }, // PreventWindowInit
            { 0x54, new Layout(true, new int[] { 1 }) }, // WaitWindow
            { 0x55, new Layout(true, new int[] { 1 }) }, // SetWalkTurnSpeed
            { 0x56, new Layout(true, new int[] { 1, 1 }) }, // TimedTurn
            { 0x57, new Layout(true, new int[] { 1 }) }, // SetRandomBattleFrequency
            { 0x58, new Layout(true, new int[] { 2, 2, 2 }) }, // SlideXZY
            { 0x59, new Layout(true, new int[] { 1, 1, 1, 1 }) }, // SetTileColor
            { 0x5A, new Layout(true, new int[] { 1, 2, 2, 2 }) }, // SetTilePositionEx
            { 0x5B, new Layout(true, new int[] { 1, 1 }) }, // ShowTile
            { 0x5C, new Layout(true, new int[] { 1, 1, 2, 2 }) }, // MoveTileLoop
            { 0x5D, new Layout(true, new int[] { 1, 1, 2, 2 }) }, // MoveTile
            { 0x5E, new Layout(true, new int[] { 1, 2, 2 }) }, // SetTilePosition
            { 0x5F, new Layout(true, new int[] { 1, 1 }) }, // RunTileAnimation
            { 0x60, new Layout(true, new int[] { 1, 1 }) }, // ActivateTileAnimation
            { 0x61, new Layout(true, new int[] { 1, 2 }) }, // SetTileAnimationSpeed
            { 0x62, new Layout(true, new int[] { 1, 1 }) }, // SetRow
            { 0x63, new Layout(true, new int[] { 1, 1, 1 }) }, // SetTileAnimationPause
            { 0x64, new Layout(true, new int[] { 1, 1 }) }, // SetTileAnimationFlags
            { 0x65, new Layout(true, new int[] { 1, 1, 1 }) }, // RunTileAnimationEx
            { 0x66, new Layout(true, new int[] { 1, 2 }) }, // SetTextVariable
            { 0x67, new Layout(true, new int[] { 1, 1 }) }, // SetControlDirection
            { 0x68, new Layout(true, new int[] { 1 }) }, // Bubble
            { 0x69, new Layout(true, new int[] { 2 }) }, // ChangeTimerTime
            { 0x6A, new Layout(false, new int[] {  }) }, // DisableRun
            { 0x6B, new Layout(true, new int[] { 1, 1, 1 }) }, // SetBackgroundColor
            { 0x6C, new Layout(false, new int[] {  }) }, // 0x6C
            { 0x6D, new Layout(false, new int[] {  }) }, // 0x6D
            { 0x6E, new Layout(false, new int[] {  }) }, // 0x6E
            { 0x6F, new Layout(true, new int[] { 2, 2, 1, 1 }) }, // MoveCamera
            { 0x70, new Layout(true, new int[] { 1, 1 }) }, // ReleaseCamera
            { 0x71, new Layout(true, new int[] { 1, 1, 1 }) }, // EnableCameraServices
            { 0x72, new Layout(true, new int[] { 2 }) }, // SetCameraFollowHeight
            { 0x73, new Layout(false, new int[] {  }) }, // EnableCameraFollow
            { 0x74, new Layout(false, new int[] {  }) }, // DisableCameraFollow
            { 0x75, new Layout(true, new int[] { 1, 1 }) }, // Menu
            { 0x76, new Layout(true, new int[] { 2, 2 }) }, // DrawRegionStart
            { 0x77, new Layout(true, new int[] { 2, 2 }) }, // DrawRegionSetLast
            { 0x78, new Layout(false, new int[] {  }) }, // DrawRegionPushNew
            { 0x79, new Layout(false, new int[] {  }) }, // 0x79
            { 0x7A, new Layout(true, new int[] { 2 }) }, // SetLeftAnimation
            { 0x7B, new Layout(true, new int[] { 2 }) }, // SetRightAnimation
            { 0x7C, new Layout(true, new int[] { 2, 1 }) }, // EnableDialogChoices
            { 0x7D, new Layout(true, new int[] { 1 }) }, // RunTimer
            { 0x7E, new Layout(true, new int[] { 1 }) }, // SetFieldCamera
            { 0x7F, new Layout(false, new int[] {  }) }, // EnableShadow
            { 0x80, new Layout(false, new int[] {  }) }, // DisableShadow
            { 0x81, new Layout(true, new int[] { 1, 1 }) }, // SetShadowSize
            { 0x82, new Layout(true, new int[] { 2, 2 }) }, // SetShadowOffset
            { 0x83, new Layout(true, new int[] { 1 }) }, // LockShadowRotation
            { 0x84, new Layout(false, new int[] {  }) }, // UnlockShadowRotation
            { 0x85, new Layout(true, new int[] { 1 }) }, // SetShadowAmplifier
            { 0x86, new Layout(true, new int[] { 1, 1, 1, 1 }) }, // SetAnimationStandSpeed
            { 0x87, new Layout(true, new int[] { 1, 1 }) }, // TurnInstantEx
            { 0x88, new Layout(true, new int[] { 1, 2, 2, 2 }) }, // RunModelCode
            { 0x89, new Layout(true, new int[] { 2, 2, 2, 1 }) }, // SetSoundPosition
            { 0x8A, new Layout(true, new int[] { 1, 1 }) }, // SetSoundObjectPosition
            { 0x8B, new Layout(true, new int[] { 1, 1 }) }, // SetHeadFocusMask
            { 0x8C, new Layout(true, new int[] { 1, 1, 2 }) }, // BattleEx
            { 0x8D, new Layout(true, new int[] { 1 }) }, // ShowTimer
            { 0x8E, new Layout(false, new int[] {  }) }, // RaiseWindows
            { 0x8F, new Layout(true, new int[] { 1, 1, 1, 1 }) }, // SetModelColor
            { 0x90, new Layout(false, new int[] {  }) }, // DisableInactiveAnimation
            { 0x91, new Layout(true, new int[] { 1 }) }, // FollowFocus
            { 0x92, new Layout(true, new int[] { 1, 2, 1, 1, 1, 1, 1 }) }, // AttachTile
            { 0x93, new Layout(true, new int[] { 1 }) }, // SetObjectFlags
            { 0x94, new Layout(true, new int[] { 2, 1, 1 }) }, // SetJumpAnimation
            { 0x95, new Layout(true, new int[] { 1, 1, 1, 2 }) }, // WindowSyncEx
            { 0x96, new Layout(true, new int[] { 1, 1, 1, 2 }) }, // WindowAsyncEx
            { 0x97, new Layout(true, new int[] { 1 }) }, // ReturnEntryFunctions
            { 0x98, new Layout(true, new int[] { 1 }) }, // MakeAnimationLoop
            { 0x99, new Layout(true, new int[] { 1 }) }, // SetTurnSpeed
            { 0x9A, new Layout(true, new int[] { 2, 1 }) }, // EnablePathTriangle
            { 0x9B, new Layout(true, new int[] { 2, 2 }) }, // TurnTowardPosition
            { 0x9C, new Layout(false, new int[] {  }) }, // RunJumpAnimation
            { 0x9D, new Layout(false, new int[] {  }) }, // RunLandAnimation
            { 0x9E, new Layout(false, new int[] {  }) }, // ExitField
            { 0x9F, new Layout(true, new int[] { 1, 1, 1, 1 }) }, // SetObjectSize
            { 0xA0, new Layout(false, new int[] {  }) }, // WalkToExit
            { 0xA1, new Layout(true, new int[] { 2, 2, 2 }) }, // MoveInstantXZY
            { 0xA2, new Layout(true, new int[] { 2, 2, 2 }) }, // WalkXZY
            { 0xA3, new Layout(true, new int[] { 1, 1, 1, 1 }) }, // 0xA3
            { 0xA4, new Layout(false, new int[] {  }) }, // CalculateExitPosition
            { 0xA5, new Layout(true, new int[] { 2, 2 }) }, // Slide
            { 0xA6, new Layout(true, new int[] { 1 }) }, // SetRunSpeedLimit
            { 0xA7, new Layout(true, new int[] { 1 }) }, // Turn
            { 0xA8, new Layout(true, new int[] { 1 }) }, // SetPathing
            { 0xA9, new Layout(true, new int[] { 1 }) }, // CalculateScreenPosition
            { 0xAA, new Layout(false, new int[] {  }) }, // EnableMenu
            { 0xAB, new Layout(false, new int[] {  }) }, // DisableMenu
            { 0xAC, new Layout(true, new int[] { 2 }) }, // ChangeDisc
            { 0xAD, new Layout(true, new int[] { 1, 2, 2, 2 }) }, // MoveInstantXZYEx
            { 0xAE, new Layout(true, new int[] { 2 }) }, // TetraMaster
            { 0xAF, new Layout(false, new int[] {  }) }, // DeleteAllCards
            { 0xB0, new Layout(true, new int[] { 2 }) }, // SetFieldName
            { 0xB1, new Layout(false, new int[] {  }) }, // ResetFieldName
            { 0xB2, new Layout(true, new int[] { 1, 2 }) }, // Party
            { 0xB3, new Layout(true, new int[] { 1, 1, 2, 2, 2 }) }, // RunSPSCode
            { 0xB4, new Layout(true, new int[] { 2 }) }, // SetPartyReserve
            { 0xB5, new Layout(true, new int[] { 1 }) }, // PretendToBe
            { 0xB6, new Layout(true, new int[] { 2 }) }, // WorldMap
            { 0xB7, new Layout(false, new int[] {  }) }, // 0xB7
            { 0xB8, new Layout(false, new int[] {  }) }, // 0xB8
            { 0xB9, new Layout(true, new int[] { 1, 2 }) }, // AddControllerMask
            { 0xBA, new Layout(true, new int[] { 1, 2 }) }, // RemoveControllerMask
            { 0xBB, new Layout(true, new int[] { 1, 1, 1 }) }, // TimedTurnEx
            { 0xBC, new Layout(true, new int[] { 1 }) }, // WaitTurnEx
            { 0xBD, new Layout(true, new int[] { 1, 2 }) }, // RunAnimationEx
            { 0xBE, new Layout(true, new int[] { 1 }) }, // WaitAnimationEx
            { 0xBF, new Layout(true, new int[] { 1, 2, 2 }) }, // MoveInstantEx
            { 0xC0, new Layout(true, new int[] { 1, 1 }) }, // EnableTextureAnimation
            { 0xC1, new Layout(true, new int[] { 1, 1 }) }, // RunTextureAnimation
            { 0xC2, new Layout(true, new int[] { 1, 1 }) }, // StopTextureAnimation
            { 0xC3, new Layout(true, new int[] { 1, 1 }) }, // SetTileCamera
            { 0xC4, new Layout(true, new int[] { 1, 2 }) }, // RunWorldCode
            { 0xC5, new Layout(true, new int[] { 2, 2 }) }, // RunSoundCode
            { 0xC6, new Layout(true, new int[] { 2, 2, 3 }) }, // RunSoundCode1
            { 0xC7, new Layout(true, new int[] { 2, 2, 3, 1 }) }, // RunSoundCode2
            { 0xC8, new Layout(true, new int[] { 2, 2, 3, 1, 1 }) }, // RunSoundCode3
            { 0xC9, new Layout(true, new int[] { 1, 2, 2, 2, 2 }) }, // SetupTileLoopingWindow
            { 0xCA, new Layout(true, new int[] { 1, 1 }) }, // ResetTileAnimation
            { 0xCB, new Layout(true, new int[] { 1, 1 }) }, // EnablePath
            { 0xCC, new Layout(true, new int[] { 2 }) }, // AddCharacterAttribute
            { 0xCD, new Layout(true, new int[] { 2 }) }, // RemoveCharacterAttribute
            { 0xCE, new Layout(true, new int[] { 3 }) }, // AddGil
            { 0xCF, new Layout(true, new int[] { 3 }) }, // RemoveGil
            { 0xD0, new Layout(true, new int[] { 2 }) }, // BattleDialog
            { 0xD1, new Layout(false, new int[] {  }) }, // 0xD1
            { 0xD2, new Layout(false, new int[] {  }) }, // 0xD2
            { 0xD3, new Layout(false, new int[] {  }) }, // 0xD3
            { 0xD4, new Layout(true, new int[] { 2, 2, 2 }) }, // AttachObjectOffset
            { 0xD5, new Layout(false, new int[] {  }) }, // HideAllObjects
            { 0xD6, new Layout(false, new int[] {  }) }, // ShowAllObjects
            { 0xD7, new Layout(true, new int[] { 1 }) }, // ATE
            { 0xD8, new Layout(true, new int[] { 1, 1 }) }, // SetWeather
            { 0xD9, new Layout(true, new int[] { 1, 1 }) }, // CureStatus
            { 0xDA, new Layout(true, new int[] { 1, 1, 1, 2, 2 }) }, // RunSPSCodeSimple
            { 0xDB, new Layout(true, new int[] { 1, 1 }) }, // EnableVictoryPose
            { 0xDC, new Layout(false, new int[] {  }) }, // Jump
            { 0xDD, new Layout(true, new int[] { 1 }) }, // RemoveParty
            { 0xDE, new Layout(true, new int[] { 1, 2 }) }, // SetName
            { 0xDF, new Layout(true, new int[] { 1 }) }, // SetObjectOvalRatio
            { 0xE0, new Layout(false, new int[] {  }) }, // AddFrog
            { 0xE1, new Layout(false, new int[] {  }) }, // TerminateBattle
            { 0xE2, new Layout(true, new int[] { 2, 2, 2, 1 }) }, // SetupJump
            { 0xE3, new Layout(true, new int[] { 1 }) }, // SetDialogProgression
            { 0xE4, new Layout(true, new int[] { 1, 1, 2, 2, 1 }) }, // MoveTileLoopWithOffset
            { 0xE5, new Layout(true, new int[] { 1 }) }, // AttackSpecial
            { 0xE6, new Layout(true, new int[] { 1, 1 }) }, // SetTileLoopType
            { 0xE7, new Layout(true, new int[] { 1, 1 }) }, // SetTileAnimationFrame
            { 0xE8, new Layout(true, new int[] { 2, 2, 2 }) }, // SideWalkXZY
            { 0xE9, new Layout(false, new int[] {  }) }, // UpdatePartyUID
            { 0xEA, new Layout(false, new int[] {  }) }, // CalculateScreenOrigin
            { 0xEB, new Layout(false, new int[] {  }) }, // CloseAllWindows
            { 0xEC, new Layout(true, new int[] { 1, 1, 1, 1, 1, 1 }) }, // FadeFilter
            { 0xED, new Layout(true, new int[] { 1, 2, 2 }) }, // SetTileLoopAlpha
            { 0xEE, new Layout(false, new int[] {  }) }, // EnableInactiveAnimation
            { 0xEF, new Layout(true, new int[] { 1 }) }, // ShowHereIcon
            { 0xF0, new Layout(false, new int[] {  }) }, // EnableRun
            { 0xF1, new Layout(true, new int[] { 1, 2 }) }, // SetHP
            { 0xF2, new Layout(true, new int[] { 1, 2 }) }, // SetMP
            { 0xF3, new Layout(true, new int[] { 1, 1 }) }, // UnlearnAbility
            { 0xF4, new Layout(true, new int[] { 1, 1 }) }, // LearnAbility
            { 0xF5, new Layout(false, new int[] {  }) }, // GameOver
            { 0xF6, new Layout(true, new int[] { 1 }) }, // VibrateController
            { 0xF7, new Layout(true, new int[] { 1 }) }, // ActivateVibration
            { 0xF8, new Layout(true, new int[] { 1, 1, 1 }) }, // RunVibrationTrack
            { 0xF9, new Layout(true, new int[] { 1, 1, 1 }) }, // ActivateVibrationTrack
            { 0xFA, new Layout(true, new int[] { 2 }) }, // SetVibrationSpeed
            { 0xFB, new Layout(true, new int[] { 1 }) }, // SetVibrationFlags
            { 0xFC, new Layout(true, new int[] { 1, 1 }) }, // SetVibrationRange
            { 0xFD, new Layout(true, new int[] { 1, 2 }) }, // PreloadField
            { 0xFE, new Layout(true, new int[] { 1, 1, 1, 1, 1 }) }, // SetCharacterData
            { 0xFF, new Layout(false, new int[] {  }) }, // EXTENDED_CODE
            { 0x100, new Layout(true, new int[] { 1, 1 }) }, // 0x100
            { 0x101, new Layout(true, new int[] { 1, 1 }) }, // 0x101
            { 0x102, new Layout(true, new int[] { 1, 1 }) }, // 0x102
            { 0x103, new Layout(true, new int[] { 1, 1 }) }, // 0x103
            { 0x104, new Layout(true, new int[] { 1, 1 }) }, // 0x104
            { 0x105, new Layout(true, new int[] { 1, 2 }) }, // 0x105
            { 0x106, new Layout(true, new int[] { 1, 1 }) }, // 0x106
            { 0x107, new Layout(true, new int[] { 1, 2, 2 }) }, // 0x107
            { 0x108, new Layout(true, new int[] { 1, 1 }) }, // 0x108
            { 0x109, new Layout(true, new int[] { 1, 1 }) }, // 0x109
            { 0x10A, new Layout(true, new int[] { 1, 1 }) }, // SetWalkpathAnimFrame
            { 0x10B, new Layout(true, new int[] { 1, 1 }) }, // 0x10B
            { 0x10C, new Layout(true, new int[] { 1, 1 }) }, // 0x10C
            { 0x10D, new Layout(true, new int[] { 1, 2 }) }, // 0x10D
            { 0x10E, new Layout(true, new int[] { 1, 1 }) }, // 0x10E
            { 0x10F, new Layout(true, new int[] { 1, 1, 1 }) }, // 0x10F
            { 0x110, new Layout(true, new int[] { 1, 1, 1 }) }, // 0x110
            { 0x111, new Layout(true, new int[] { 1, 1 }) }, // 0x111
    };
}}
