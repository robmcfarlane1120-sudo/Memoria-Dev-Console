Entrance installation workflow
==============================

Test applies an entrance immediately through MDC's already loaded runtime.
Install to Mod writes its JSON and three generated runtime source files into
the selected mod. Installation never compiles or replaces that mod's DLL.

Use Home > Compile + Restart when the runtime needs a build. This uses the
standard Memoria compiler already integrated with MDC, then verifies pending
entrance installs before requesting restart. Successful builds clear the
pending marker. Current runtimes can reload subsequent JSON-only edits.

Backups contain only the replaced source/data files plus Changes.txt. New
files are listed there; unrelated mod source trees and DLLs are not copied.
Incomplete source/data writes are rolled back. Full source completeness and
public API equivalence are not audited by this installer. Authors must supply
their complete mod sources before compiling. DLL-only mods without sources
are rejected; legacy Memoria.Scripts.dll targets require migration.

Players need the destination mod's compiled runtime and JSON, not MDC.
Templates/ contains the generated runtime source templates.
Tests/ stores MDC's own test entrance JSON.
