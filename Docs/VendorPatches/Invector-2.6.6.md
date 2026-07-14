# Invector 2.6.6 Vendor Patch Ledger

This ledger records every intentional edit under `Assets/Invector-3rdPersonController/`. Keep BrawlArena integration code outside the vendor tree and re-audit these patches whenever Invector or Unity changes.

## Imported Package Provenance

- Package: Invector Third Person Controller - Shooter Template
- Package version: 2.6.6
- Unity Asset Store product ID: 84583
- Asset upload ID: 948174
- Imported asset root: `Assets/Invector-3rdPersonController/`
- Baseline Unity editor: 6000.3.7f1
- Baseline date: 2026-07-13
- Baseline repository state: the imported vendor tree was untracked; existing user changes in `ProjectSettings` were left untouched.

The package identity is recorded by Unity `AssetOrigin` metadata, including `vShooterMeleeInput.cs.meta`.

## Pristine Restore Source

Local Unity Asset Store cache:

```text
C:\Users\sk8r\AppData\Roaming\Unity\Asset Store-5.x\Invector\Editor ExtensionsGame Toolkits\Invector Third Person Controller - Shooter Template.unitypackage
```

Cached package SHA-256:

```text
96A311CFAACCA1C0FF048E2A774C8480C868F21899475D4106E4747C52638039
```

The cached package's two P001 script payloads are byte-exact matches for the pristine imported source, but their archive-local `asset.meta` entries report package 2.6.5/upload 845166. The current imported `.meta` files and all module changelogs report 2.6.6/upload 948174. Treat the cache as a byte-exact source-payload restore location, not standalone proof of the current imported version, and preserve current `.meta` files when restoring individual scripts.

Before restoring from the cached package, verify that its hash still matches. Prefer restoring the exact original source recorded by each patch. A full package reimport requires explicit approval: close Unity, preserve/move the whole untracked vendor root first, then reimport without allowing the bundled ProjectSettings to overwrite BrawlArena.

## P001 — Unity 6000.3 `FindObjectsByType` Compatibility

Status: validated  
Applied: 2026-07-13  
Applied by: Codex via repository `apply_patch`  
Reason: In the `UNITY_6000_2_OR_NEWER` branches, the one-argument generic overload expects `FindObjectsSortMode`, not `FindObjectsInactive`. The existing `#else` branches already contain the compatible two-argument form.

Attributable files:

- `Basic Locomotion/Scripts/CharacterController/vThirdPersonInput.cs`
- `Shooter/Scripts/Shooter/vShooterManager.cs`

### File 1

Path:

```text
Assets/Invector-3rdPersonController/Basic Locomotion/Scripts/CharacterController/vThirdPersonInput.cs
```

Location: `FindCamera()`, line 160 at the 2.6.6 baseline.  
Pristine size: 23,509 bytes.  
Patched size: 23,536 bytes.

Pristine file SHA-256:

```text
A32616B59633048E1BE738121A68E232FE6F15187DF24B658D49268D388721E7
```

Original line:

```csharp
var tpCameras = FindObjectsByType<vCamera.vThirdPersonCamera>(FindObjectsInactive.Exclude);
```

Patched line:

```csharp
var tpCameras = FindObjectsByType<vCamera.vThirdPersonCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
```

### File 2

Path:

```text
Assets/Invector-3rdPersonController/Shooter/Scripts/Shooter/vShooterManager.cs
```

Location: `GetAmmoDisplays()`, line 461 at the 2.6.6 baseline.  
Pristine size: 36,212 bytes.  
Patched size: 36,239 bytes.

Pristine file SHA-256:

```text
43AF878B1D314C6910D6689D6316CA1241F2A7E10C9378B3AF7ACC22C0AA601F
```

Original line:

```csharp
var ammoDisplays = FindObjectsByType<vAmmoDisplay>(FindObjectsInactive.Exclude);
```

Patched line:

```csharp
var ammoDisplays = FindObjectsByType<vAmmoDisplay>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
```

### Validation Record

- Validated: 2026-07-13T00:42:13+03:00
- Audit source-pattern checks: both obsolete one-argument calls absent
- Unity compilation/domain reload: controlled forced imports, `AssetDatabase.Refresh`, and clean script compilation completed
- Live Console errors after Unity settled: 0
- Active target: `StandaloneWindows64` / `NamedBuildTarget.Standalone`
- Active defines: `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY;INVECTOR_BASIC;INVECTOR_MELEE;INVECTOR_SHOOTER`
- Patched `vThirdPersonInput.cs` SHA-256: `96E2AEEFBD5C6360E1C3A2EC32425AF778DE0F0C49D344BEAC4EC9A30675AF36`
- Patched `vShooterManager.cs` SHA-256: `EA083AF3017EFEC39759808003A7BD48D5B17CF5F6BE053A60A83E4536FA324C`

### Rollback

Replace each patched line with its recorded original line, then allow Unity to refresh/recompile. Re-run the repository Invector audit and confirm that the source-pattern findings return, which proves the pristine call sites were restored. If an upstream package update already contains a correct implementation, retire P001 instead of reapplying it.

## P002 — Unity 6000.3 Hierarchy Icon Callback Compatibility

Status: validated  
Applied: 2026-07-13  
Applied by: Codex via repository `apply_patch`  
Attributable file: `Basic Locomotion/Scripts/Generic/Editor/vInvectorIcon.cs`  
Location: static constructor plus the two hierarchy callback declarations, lines 8-38 at the 2.6.6 baseline.

Reason: the `UNITY_6000_3_OR_NEWER` path subscribes to `EditorApplication.hierarchyWindowItemByEntityIdOnGUI`, but Unity 6000.3.7f1 does not expose that event and fails `Assembly-CSharp-Editor` with CS0117. The still-supported `hierarchyWindowItemOnGUI` event uses integer instance IDs and preserves the same icon behavior.

Pristine file SHA-256:

```text
68C815F39AA7E50D06C640EF5F8EC8A38DA85FEFAA42BAFF2CC24EB6EA4B9976
```

Patch:

- subscribe both callbacks to `EditorApplication.hierarchyWindowItemOnGUI` unconditionally;
- use `int instanceId` callback arguments;
- resolve objects through `EditorUtility.InstanceIDToObject(instanceId)`;
- remove the invalid `UNITY_6000_3_OR_NEWER` callback branches.

Patched file SHA-256:

```text
C6F605999BD74451ACE939A114539EDB3D0B1EC1305DFD318D02A14EBC07E3A3
```

### Validation Record

- Validated: 2026-07-13T00:42:13+03:00
- Controlled clean compilation/domain reload: passed
- Live Console errors after Unity settled: 0
- Unity script validator diagnostics: 0
- `[InitializeOnLoad]` hierarchy callback code loaded after compilation without an exception or Console error

### Rollback

Restore the pristine source payload for `vInvectorIcon.cs` or restore its recorded conditional callback branches, then refresh/recompile Unity. Retire P002 instead of reapplying it when an upstream version uses an event that exists in the target Unity editor.
