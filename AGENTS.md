# Codex Instructions

## Project Shape

- This is a Visual Studio C# in-game mod project for Medieval Engineers / VRage.
- The project references the game's DLLs directly from the local Medieval Engineers install. To discover the current DLL paths, inspect `SiUtilityAI.csproj` and read the `<Reference>` / `<HintPath>` entries.
- `stubs/CompileOnly/` contains compile-only API surfaces for external workshop scripts that are expected to be loaded in game but are too broad to compile locally. These files are not part of the game-facing `mod/` payload; keep them limited to signatures and update them when the referenced dependency API changes.
- The actual mod payload is under `mod/`. This folder contains `metadata.mod`, `Data/`, scripts, `.sbc` definitions, UI data, block definitions, and other files that are loaded by the game.
- `mod/` is soft-linked into the game's local mod directory. Treat `mod/` as the game-facing folder tree, not just a source folder.
- The game supports both `.cs` C# scripts and `.sbc` XML entity/definition files.

## Linked Dependencies
- Content re-usage permission is granted for vanilla content and the content made by PAX / Xaerthus / Equinox. Feel free to re-use the code.
- `ref_workshop` has all game mods. Each one has XML `metadata.mod` with `<ModId></ModId>` line. Below are key mods:
- `equinoxcore` extends the game API and is the preferred reference for mod folder structure and style because it was written by an actual game developer team member.
- `railsystemcore` adds the rails - flexible blocks that create colliders on their own.
- `pax scripts` adds core scripts for many industrial blocks.
- `rifledefenders` adds static defenders.
- `modern small arms` adds player held guns.
- `watercore` adds content using PAX Core.

## Shared content
- `ref_si_core/` is a soft link to another Visual Studio project and is editable. It is the shared core for reusable code across the user's mods. It's the only mod you can modify.
- If a change reveals reusable code that belongs in shared infrastructure, it is acceptable to add or update code in `ref_si_core/`, following its existing structure and style.

## Linked Vanilla Game Content
- `ref_vanilla_content/` leads to vanilla game content folder (no scripts).
- `ref_vanilla_content/Data/Characters` has in-game NPC character containers.

## Folder Layout Rules

- Preserve the general Medieval Engineers mod folder tree to avoid file loading problems.
- Prefer the structure used by `ref_equi_core/` when adding new files. In particular, place game-loaded files under `Data/` and scripts under `Data/Scripts/...` with domain-specific subfolders.
- Keep `metadata.mod` at the mod root.
- Keep `.sbc` files in appropriate `Data/` subfolders that mirror existing mod/core conventions, such as `Data/CubeBlocks/`, `Data/UI/`, or root-level `Data/*.sbc` when that matches the referenced mods.
- Add project-specific mod files under `mod/`. Add reusable shared helpers under `ref_si_core/` only when they are genuinely useful across mods.

## Coding Style

- When naming classes, follow `ref_equi_core` conventions:
  - Prefix mod-specific runtime/component classes consistently with the local domain prefix already in use.
  - Use `MyObjectBuilder_*` names for object builder classes.
  - Use `*Definition` and `MyObjectBuilder_*Definition` pairs for definitions where the game API expects them.
  - Use partial classes and nested helper types only where the surrounding `ref_equi_core` style would do so.
- Match nearby file style for namespaces, visibility, attributes, nullability, and update/event patterns.
- Avoid introducing new framework abstractions unless they fit the game's script-loading constraints and existing project style.

## Data-Driven Component Tuning

- Store exact gameplay tuning values in `.sbc` entity component definitions under `mod/Data/`, not in C# constants, property initializers, or overridable properties. Keep only true algorithmic invariants in code.
- In C#, provide the object builder, definition, and runtime component plumbing needed to read and validate those values. Do not duplicate `.sbc` tuning values as hidden code defaults.
- Attach the chosen component-definition subtype from the entity's `MyObjectBuilder_ContainerDefinition`. A new archetype or another mod should be able to reuse the C# controller by defining and attaching a different `.sbc` subtype instead of creating a tuning-only subclass.
- When adding a configurable system, follow the existing utility-brain and grounded-NPC component patterns: keep reusable logic policy-neutral, keep per-archetype values in `.sbc`, and make the component boundary clear enough for other modders to compose.

## API Discovery

- First search `mod/`, then `ref_si_core/`, then read-only dependencies (`ref_equi_core/`, `ref_pax_core/`) for examples of any unknown method, type, component, or `.sbc` pattern.
- If an API is still unclear, inspect the referenced game DLLs from `SiUtilityAI.csproj` `<HintPath>` values. Use metadata/decompiler tools or IDE navigation against those DLLs instead of guessing signatures.
- Prefer examples from `ref_equi_core/` when several sources disagree.

## Runtime Debugging

- When an in-game issue is silent or only reproducible inside Medieval Engineers, prefer adding temporary runtime logging over guessing.
- Use `NamedLogger(MySession.Static.Log, nameof(YourComponentType))` and emit a short, grep-friendly prefix such as `[YourSystem]` in each message so the user can filter `C:\Users\SicH\AppData\Roaming\MedievalEngineers\MedievalEngineers.log`.
- Any temporary game-log output line added for debugging in game-loaded C# scripts must end with the exact marker comment `// AGENT-DEBUG-LOG`. This is mandatory for agent-added temporary logging.
- Keep each marked temporary log emission on a single physical line so it can be removed mechanically. Example: `_log.Warning($"[SiCover] entityId={Entity?.EntityId ?? 0} ..."); // AGENT-DEBUG-LOG`
- Use `powershell -ExecutionPolicy Bypass -File .\tools\Remove-AgentDebugLogs.ps1` from the repo root to remove every marked debug log line under `mod/Data/`. Use `-WhatIf` first to preview the files and counts without editing, and pass `-Root .\some\other\Data` to target a different Data tree when needed.
- Include the entity id, entity name, key definition subtype, and the exact branch outcome being tested. One good log line with concrete state is better than many vague ones.
- For wiring problems, log the component or inventory lookup results directly. If useful, dump the runtime component type list once on first failure instead of spamming it every retry.
- After the issue is understood or fixed, remove or reduce verbose success-path logging and keep only the diagnostics that are likely to help with future regressions.

## Verification

- Build `SiUtilityAI.sln` as the primary verification step after changes. From the repository root, run `dotnet build .\SiUtilityAI.sln --no-restore --nologo --verbosity:minimal "-consoleloggerparameters:ErrorsOnly;Summary"`; if restore inputs are missing or stale, run the same command once without `--no-restore`.
- The project build runs `tools/Check-IngameScriptApi.ps1` before C# compilation. This guard scans editable game-loaded scripts under `mod/Data/Scripts` and `ref_si_core/Data/Scripts` for reflection APIs rejected by the Medieval Engineers script compiler, such as `System.Reflection`, `MethodInfo`, `MethodBase`, `BindingFlags`, `Type.GetType`, and `GetMethod`. Treat `SIUAI001` diagnostics as actionable in-game script errors even when Roslyn would otherwise compile the code.
- The solution compiles scripts reached through the linked dependency folders as well as code under `mod/` and `ref_si_core/`, so the build may fail because of unrelated dependency diagnostics. Review the complete compiler output and attribute each error to its root cause.
- Treat an error as actionable when it is caused by code under `mod/` or `ref_si_core/`, including errors reported in another folder when a change in one of those two trees caused them. Fix actionable errors before considering verification complete.
- Ignore errors that are not caused by `mod/` or `ref_si_core/`; in particular, do not modify `ref_equi_core/`, `ref_pax_core/`, or `ref_pax_defenders/` to make the build pass. A nonzero build exit code is acceptable only when every remaining error has been reviewed and classified as unrelated.
- Static checks such as targeted search, XML well-formedness checks for `.sbc`, and signature comparison may supplement the build where useful, but they do not replace it for C# changes.
- Report the build command and result, summarize any ignored unrelated errors, and mention when the change still needs in-game validation.
