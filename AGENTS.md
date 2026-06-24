# Codex Instructions

## Project Shape

- This is a Visual Studio C# in-game mod project for Medieval Engineers / VRage.
- The project references the game's DLLs directly from the local Medieval Engineers install. To discover the current DLL paths, inspect `VehicleAI.csproj` and read the `<Reference>` / `<HintPath>` entries.
- The actual mod payload is under `mod/`. This folder contains `metadata.mod`, `Data/`, scripts, `.sbc` definitions, UI data, block definitions, and other files that are loaded by the game.
- `mod/` is soft-linked into the game's local mod directory. Treat `mod/` as the game-facing folder tree, not just a source folder.
- The game supports both `.cs` C# scripts and `.sbc` XML entity/definition files.

## Linked Dependencies

- `ref_equi_core/` is a soft-linked Steam Workshop dependency. It extends the game API and is the preferred reference for mod folder structure and style because it was written by an actual game developer team member. Do not modify it.
- `ref_pax_core/` is a soft-linked Steam Workshop dependency that adds many industrial blocks. Do not modify it.
- `ref_si_core/` is a soft link to another Visual Studio project and is editable. It is the shared core for reusable code across the user's mods.
- If a change reveals reusable code that belongs in shared infrastructure, it is acceptable to add or update code in `ref_si_core/`, following its existing structure and style.

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

## API Discovery

- First search `mod/`, then `ref_si_core/`, then read-only dependencies (`ref_equi_core/`, `ref_pax_core/`) for examples of any unknown method, type, component, or `.sbc` pattern.
- If an API is still unclear, inspect the referenced game DLLs from `VehicleAI.csproj` `<HintPath>` values. Use metadata/decompiler tools or IDE navigation against those DLLs instead of guessing signatures.
- Prefer examples from `ref_equi_core/` when several sources disagree.

## Verification

- Do not build the project as a verification step. This mod is checked in-game.
- Verify changes with static methods available in the repo: targeted code search, XML well-formedness checks for `.sbc`, careful signature checks against scripts/references/DLL metadata, and consistency checks against `ref_equi_core` structure.
- Mention when a change still needs in-game validation.
