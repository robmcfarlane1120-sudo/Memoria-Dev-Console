# Field Viewer

Open Model Viewer, then click **Field Viewer [F4]** or press F4.

- Search by exact field ID (including registered custom fields) or background resource name. Enter opens the first result.
- Left/Right cycles the filtered field list. Clear the field search to browse every field.
- Camera buttons select the background camera. Zoom and Fit adjust the preview.
- SPS lists the selected field's assets; **All SPS** includes other maps, world effects and prototypes from the Model Viewer catalog.
- Select an SPS to animate it over the background. Up/Down cycles filtered SPS or audio results when a search box isn't focused.
- Adjust SPS size and blend mode, pause animation, and save an identification label. Labels live in `Memoria Dev Console/FieldViewerSpsLabels.txt`, keyed by map and SPS ID.
- Music and Sounds offer known field usage or the full audio library. Search by ID, resource path or documented usage. Music and sound previews can play together.
- Preview music pauses the existing game BGM. Stop music or close the viewer to restore it. Only preview-owned voices are stopped.
- Esc or F4 returns to Model Viewer; a subsequent Esc leaves Model Viewer.

Selecting a field now loads its active modded event scripts and initializes them in an independent preview VM. Only the selected field runs. Switching fields releases its effects, generated textures and script state. There is no visited-field capture prerequisite or continuous background capture.

The preview supports initialization/main functions, spawned entries, requests, waits, branches, ordinary variables, private Memoria vectors/dictionaries, SPS loading and placement, field-camera selection, background-animation controls, and basic script music/sound playback. Script globals and collection values are copied; the VM never constructs native event objects or changes the live event interpreter. Scene transitions stop that execution path instead of warping the game. Execution is capped at 1,024 instructions per tick, 128 per entry, and at most three 30-Hz ticks per update.

The SPS list identifies initialized slots and their source map/ID. Toggle a slot to identify it, or restart field initialization. Standalone auditions remain available. Catalog music supplies a fallback until a supported script playback command runs.

This is a visual preview, not full gameplay emulation. Player interactions, collision/walkmesh queries, animated actor bones, some expressions, and advanced audio/movement operations are not simulated. Unsupported expressions stop the affected entry and appear under “Partial preview”; other initialized effects continue. Attached SPS use an initialized actor root when available and are hidden when no root position exists. Trigonometric expressions use mathematical approximations of the game's fixed-point routines. Preview state starts from the current save's story variables, so conditional effects can differ with story progress.

Validation: compiled against the installed FF9 assemblies. 42 automated checks cover atlas detection, field search, audio-ID matching, native API availability, preview-variable isolation, waits, branches, object initialization, bounded loops and suppressed scene transitions. Real exported field 6014 produced 19 distinct SPS commands and EVT_PEF3 produced 32, without a live field capture. Three PEF scripts report unsupported walkmesh-floor queries in separate entries. Visual placement, texture appearance and audio need an in-game smoke test after restarting FF9.
## All-scenario preview (2026-11-22)
The default All scenarios option loads literal SPS setups extracted from Complete_FS_Dump_Named.zip, across story branches: FieldViewerSpsPresets.tsv. It preserves distinct setups and slots, caps them at 256 per selected field, and keeps them separate from the game event engine. Setups whose IDs depend on expressions cannot be statically extracted. Placement expressions use approximate preview positions; matching object roots are used where available. This is an overlapping asset preview, not the field's real story state. Disable All scenarios to use the current mod's sandboxed script initialization instead. Custom fields without reference presets continue to use their current scripts.

Background SPS and Object SPS are shown together. Play All/Stop All control both groups and weather. Looping keeps loaded effects available instead of honoring script unload/visibility commands. Field 911 contains six reference Stellazzio slots (33.sps and 34.sps), including object-position associations.

Burmecia uses SetWeather as well as splash SPS. SetWeather now drives a bounded, approximate screen-space rain preview; it does not instantiate the native renderer, which would touch live game SFX/state. FieldViewerWeather.tsv also supplies weather across reference scenarios.

MUTE ALL BACKGROUND MUSIC has a dedicated fixed row. It applies to automatic and manually selected preview BGM across field changes; original game BGM is restored on viewer exit. Field labels use HWS Database_Steam.cpp names from FieldViewerNames.tsv, with ID/name search; custom unknown fields retain a fallback label.

Reference data provenance: user-supplied Complete_FS_Dump_Named.zip and the local HWS Database_Steam.cpp. The original archive and game field scripts were not modified. Runtime visual validation is still required.

### SPS placement and controls follow-up
- Missing reference positions now use initialized actor camera depth (or scene origin depth), rather than a fixed two-view-distance depth. This preserves more appropriate perspective sizing.
- Reference effects outside the camera are repositioned for audition; their rendered depth is brought in front of the background so all-scenario effects are not occluded by unrelated layers. Positions and occlusion are intentionally approximate in this mode.
- Hill of Despair 762/764 use the later visible scales 3000/1500 from the reference script instead of initial zero-scale states. Zero reset positions no longer count as useful reference placement.
- Reference effects can update even if preview script loading fails.
- Pandemonium Generator (2709) has no SPS calls: its lights use ShowTile/SetTileColor. All-scenario mode approximates the six tile groups and staggered pulses independently of puzzle state; Play/Stop All controls them.
- Global BGM mute is a bordered ON/OFF button; the redundant standalone asset browser is removed. Cursor visibility/unlocking is enforced while viewing and restored on exit.
- Build and existing metadata/VM checks pass. In-game visual verification remains necessary, especially effect sizes and background tile pulses.

### All-field background animation playback
All Scenarios now enables every valid background animation track in the selected field, using literal speed/loop settings from FieldViewerAnimations.tsv (1,270 settings extracted from the supplied vanilla field scripts). Field 911 includes all 13 Stellazzio animation rates. Tracks without reference settings retain their asset default speed; zero rates fall back to 256. Native camera filtering and frame sequences are preserved. Script hide/freeze/stop commands cannot disable these tracks in All Scenarios mode, and one-shot end markers are cleared for continuous preview. Shared Stop All freezes the tracks; Play All resumes. Turning All Scenarios off reloads normal script-driven behavior. Mutually exclusive tracks sharing tiles may conflict visually; this is an all-scenario audition, not a reconstruction of a particular save state. Runtime appearance still needs in-game verification.

### Field rendering audit follow-up
- Tile clock now advances at 0.375x original speed (25% slower than the prior half-speed preview), independent of SPS.
- Ambiguous packed atlas dimensions choose the smallest surplus capacity rather than preferring the INI tile size. A 1836-square atlas regression check covers the field-53 ambiguity.
- Field 2933 restores its three background layers in All Scenarios, bypassing the cinematic hide phase.
- Field 763 hides ending-only overlay 28 and previews the lightning layers with a deterministic approximation of the script fade timings; fourteen rain-splash slots now use their script's ground anchor positions without random jitter.
- Field 3006 applies the script's initial overlay-20 translation. Scrolling-loop behavior remains incomplete; the reported black bar still needs runtime verification.
- SPS mesh generation temporarily supplies the selected field number to native code and restores it in finally. This enables native field-2929 special projection. Literal SPS positions are no longer arbitrarily relocated merely for being offscreen.
- 30 viewer checks and 16 script checks pass. Screenshot cases need visual rechecking in-game; these tests cannot confirm their final rendering.

### Ground rain splash placement
FieldViewerSpsGround.tsv contains 171 randomised ground-position definitions recovered from literal-anchor Sin/Cos(GetRandom) expressions across 11 reference fields. Preview SPS now preserve signed world coordinates and authored ground height, randomising only X/Z within the script's shift-derived ranges. Each splash gets a staggered frame offset and moves to a new deterministic ground position once per loop. These effects bypass generic image-scatter fallback. Fields without such expressions retain their existing placement. A regression check verifies signed heights and horizontal bounds across 20 cycles; final appearance needs in-game verification. This is an approximation of the script random sequence, not a collision/walkmesh simulation.
