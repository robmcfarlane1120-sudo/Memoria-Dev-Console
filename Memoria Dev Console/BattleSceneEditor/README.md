# MDC Battle Scene Editor

Restart FFIX after installing this MDC build. During a fully loaded battle, open MDC with F10 and select **[B] Battle Scene Editor**. Outside battle it shows an inactive message and does not access battle actors.

Use Tab to switch between Camera, Formation, and Presets / Export. Up/Down selects a control. Left/Right adjusts a number or cycles the selected actor/preset. Enter opens exact numeric input or runs the selected action. Ctrl+A clears numeric input; Enter applies it. Plus/minus changes the arrow adjustment step. Angle/FOV arrow steps are capped at five degrees.

Camera controls include XYZ, pitch/yaw/roll offsets, vertical FOV, enable, Freeze, capture, reset, and camera-only preset save. Angle offsets use the captured native PSX camera basis. Freeze holds the edited camera across scripted phases for framing. The Battle Scene Editor page keeps battle simulation running; Freeze Camera never pauses the battle. With Freeze off, it applies only during normal idle views. Escape, F10 hiding the editor, Release Camera, and battle exit all release camera ownership. The tool never disables the native camera controller or its phase-transition logic.

Formation lists every live slot with name, native battle mask, visibility and HP. Cycle Selected Unit to edit each party member or enemy independently. Home XYZ values are formation positions, not transient attack offsets. Busy-actor edits are queued until that actor is no longer attacking or being targeted by an active command. Closing the editor permits the game to continue and finish pending moves; camera Freeze is released. The editor preserves relative front/back-row and Float offsets. It does not modify HP, targeting IDs, AI, row flags, or model size.

Capture Current Layout refreshes actor homes and keeps the current edited camera. Save Layout writes a named JSON preset with battle ID and formation-pattern ID. Select Saved Preset with Left/Right, then Load Layout. Cross-battle or cross-pattern files are rejected. Reset to Original restores the layout from the first time the editor opened in that encounter, including other mods already applied. Camera-only presets preserve current actor homes when loaded.

Presets: `Memoria Dev Console/BattleSceneEditor/Presets`

Exported mod overlays: `Memoria Dev Console/BattleSceneEditor/Exports`

Export Standalone Mod creates JSON, a reusable C# runtime, and compilation/installation instructions. The runtime must be compiled into your mod before shipping; MDC is not required by players. See [RuntimeIntegration.md](RuntimeIntegration.md).

The console follows MDC's existing native keyboard-driven interface, using stepped values and precise numeric entry rather than a separate Unity window. Other MDC pages retain their existing behaviour.

The automated checks cover data validation, preset round trips and backups, actual native-menu dispatch, camera math/render-state restoration, queued formation edits, reset, and standalone export compilation. Camera/formation execution tests use a managed battle/render fixture; a live visual play-through is still required to confirm framing and native SFX behaviour in your chosen encounter.
