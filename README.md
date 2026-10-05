# Sprocket Part Clipboard

[中文](README.zh.md) | **English**

Copy and paste a whole part subtree inside the Sprocket vehicle designer.

> ## ⚠️ This is a very dangerous mod
>
> - **Only tested on turrets.** Every in-game verification in this project was done on the turret ring
>   (`turretRing`) subtree. Copying anything else has **not** received the same level of testing and may
>   damage a vehicle or write a broken blueprint.
> - **Back up your save before copying anything else.** Once a vehicle design is broken there is usually no
>   undo available (pasting here does not enter the undo stack either).
> - The clipboard file lives at `BepInEx\config\SprocketPartClipboard\library.json`. It belongs to this mod:
>   deleting it only clears the clipboard and does not touch your game saves.
>
> See "Known issues" below for the current defect and its workaround.

## Features

- `Ctrl+C`: capture the selected part and its entire subtree into the clipboard.
- `Ctrl+V`: restore that subtree into the current vehicle, attached under the selected part, and enter
  drag placement.
- The clipboard persists across vehicles and game restarts:
  `BepInEx\config\SprocketPartClipboard\library.json` holds up to 64 entries, and `Ctrl+V` pastes the most recent one.
- Key bindings are managed by SprocketModAPI and can be changed in-game under
  "Settings → Keymapping → MOD KEYBINDINGS".

## Installation

1. BepInEx 6 (IL2CPP) must be installed and `BepInEx\plugins` must already contain `SprocketModAPI.dll` (1.0.0 or newer).
2. Drop `SprocketPartClipboard.dll` into `BepInEx\plugins`.

## Known issues

- After pasting a turret, its ammo rack is rendered as a 1×1×1 cube in the simulation.
  Available workaround: save after pasting and reload the vehicle. (The battle itself is unaffected.)
- If `BepInEx\LogOutput.log` contains
  `Plugin VehicleEditorGizmoDrawer threw an exception`, the game's editor plugin hit a null trunnion while
  drawing gizmos. This mod tries to fill in that dependency right after pasting.

## Current limitations

- Pasting does not enter the undo stack (`Instantiate` produces no operation).
- With a multi-selection, only the active part's subtree (the last clicked one) is copied.
- There is no clipboard browser: `Ctrl+V` always pastes the most recent entry.

## Implementation notes

- Capture goes through `VehicleObjectSerialization.ToBlueprints` (context `SpawnStateChange`), persistence
  through the game's own `VehicleBlueprintSerializer`: the clipboard payload is the game's vehicle blueprint
  JSON with `objects` holding only the copied subtree.
- There is a single paste path (shared by same-vehicle and cross-vehicle): remap the payload's four id spaces
  (parts / components / blueprints / meshes) into the target vehicle's id ranges → register
  `blueprints[]` / `meshes[]` into the target vehicle (definitions referenced only by an in-blueprint
  integer are additionally counted as used) → `InstantiateStructured` (context `FullSave`, ids restored by
  `LoadVuids`) → mark components dirty and build across frames until converged → repair the mantlet
  trunnion → `Reattach` for hand-held placement.
- The game's `VehicleSubAssemblySerialization` is two unimplemented stubs in 0.2.55.5; this project does not use it.

Details and reverse-engineering evidence: [docs/status.md](docs/status.md). Usage: [docs/usage.md](docs/usage.md).

## Build and test

From the repository root:

```powershell
dotnet build .\src\SprocketPartClipboard\SprocketPartClipboard.csproj --configuration Release
dotnet run --configuration Release --project .\tests\SprocketPartClipboard.ContractTests\SprocketPartClipboard.ContractTests.csproj
powershell -File .\tools\verify-package.ps1
```

The build deploys into `$(SprocketGameRoot)\BepInEx\plugins`; add `-p:SkipModDeploy=true` to only build.
