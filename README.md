# Memoria Dev Console

**Version 1.4.1** · A development toolkit for FINAL FANTASY IX with Memoria on Windows.

Open MDC with **F10** to inspect assets, test changes, and restart the game from one console.

## Install

1. Install [Memoria](https://github.com/Albeoris/Memoria). This MDC build targets Memoria 2026-11-09.
2. Download [Memoria Dev Console from Nexus Mods](https://www.nexusmods.com/finalfantasy9/mods/140?tab=files), or use the [installable mod folder](Memoria%20Dev%20Console) in this repository.
3. Copy the complete `Memoria Dev Console` folder into the FINAL FANTASY IX game folder.
4. Enable **Memoria Dev Console** in the Memoria mod manager and fully restart FFIX.

Moguri is optional. The Lost Chapters is optional.

## Tools

| Tool | What it does |
| --- | --- |
| Live Log, Compile + Restart | Follow `Memoria.log`, filter messages, build installed C# scripts, and restart after a successful compile. |
| Model Viewer and SFX Previewer | Inspect models and animations, export FBX by model ID, and preview battle effects with selectable targets, arenas, and supported audio. |
| Rendered Field Viewer | Browse backgrounds, animated tiles, weather, SPS, and field audio. The viewer detects stock 32 px and Moguri 64 px atlases per field. |
| Sound Browser | Search and audition music, field sounds, and battle audio. |
| Field Background Toolkit and Asset Dumper | Export field layers and other assets, edit a composite PNG, and recut the artwork using the original field layout. |
| World Entrance Creator and Battle Scene Editor | Test custom entrances and adjust supported battle formations and cameras. |
| Save States and Story Warps | Recreate test conditions quickly. Keep a normal save before experimenting. |

MDC pauses title-screen and gameplay music while open. Music resumes when you close it, and audio previews remain available.

## Source and release notes

The installable folder contains the C# sources, prebuilt script DLL, and required runtime data. Generated game asset dumps are excluded from Git. For source changes, use Memoria's compiler or MDC's **Compile + Restart**.

See the [1.4.1 changelog](CHANGELOG.md), the [full release history](Memoria%20Dev%20Console/CHANGELOG.txt), and the [field viewer guide](Memoria%20Dev%20Console/Documentation/FieldViewer.md).
