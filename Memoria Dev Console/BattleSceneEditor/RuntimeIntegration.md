# Battle Scene Layout runtime integration

The exported folder is a mod-ready overlay, containing JSON plus four C# source files. Players do not need Memoria Dev Console.

1. Merge `FF9_Data/BattleSceneLayouts` and `StreamingAssets/Scripts/Sources/BattleSceneLayouts` into your Memoria mod.
2. Compile the mod scripts with Memoria.Compiler (or MDC's Compile + Restart during development). Ship the resulting `StreamingAssets/Scripts/Memoria.Scripts.<your mod name>.dll`, JSON, and the rest of your mod. Source files alone are not a player-ready installation.
3. Enable the mod in Memoria and restart FFIX. The startup attribute creates the small runtime controller; no battle event script, `BattlePatch.txt`, or camera bytecode edit is required.

Install this runtime in only one compiled mod. It reads matching JSON from all enabled mods, in Memoria's configured priority order. First matching file wins. Do not add duplicate copies of the runtime to several enabled mods. When editing a fight in MDC, temporarily disable its exported runtime JSON to avoid two owners of the battle camera.

The filename `Battle_<battleId>_P<pattern>.json` and the internal battle ID and pattern must match. Party slots are formation positions 0–3, independent of character identity; enemies use their native enemy slots. Missing slots are skipped, and late spawns are applied once they become idle. Unit names are descriptive only. Layouts do not alter AI, targeting IDs, statistics, HP, row flags, or model size.

Camera XYZ and Euler angle offsets are based on the exact captured native PSX view basis. Projection scaling changes vertical FOV while retaining native aspect and off-centre projection. The runtime uses the existing Battle Camera's render callbacks, not a second camera. It leaves opening, event, attack, effect, and victory camera sequences under native control and applies the saved camera only during normal idle play. Freeze is an editor-only switch and is never exported. Legacy native SFX screen-space effects may not align with a forcibly frozen editor camera; use Release Camera before playing attacks.

Formation home coordinates are applied once when idle to `base_pos`, `original_pos`, `evt.posBattle`, and `pos`. This preserves ordinary attack returns and row spacing. Explicit scripted teleports, character swaps that reinitialize a unit, and custom sequences containing absolute coordinates can supersede home positions; such scripts need their own corresponding edits. The runtime deliberately does not pin transforms or fight scripted movement.

The editor saves presets under `Memoria Dev Console/BattleSceneEditor/Presets` and exports overlays under `Memoria Dev Console/BattleSceneEditor/Exports`. A camera-only preset has an empty unit list. Reset to Original means the layout captured when the editor first opened in that encounter, including any other mods already applied.

No vanilla assets or battle bytecode are modified by this tool. Camera geometry cannot extend the battle background: excessive zoom-out can reveal finite mesh edges. Move the formation deeper, adjust XYZ/angles, or use a less distant camera.
