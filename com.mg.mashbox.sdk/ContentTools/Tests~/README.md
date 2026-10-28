# Content pack group persistence validation

These fixtures are excluded from SDK compilation. Run only in an isolated Unity project: the entry points exit the editor.

Copy `Editor/ContentPackGroupSettings.cs`, its `.meta` file, and `ContentPackGroupPersistenceValidation.cs` into the isolated project's `Assets/Editor` folder. Keep the script's metadata so the fixture can check its serialized GUID. No SDK dependencies are required.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.ContentTools.Editor.ContentPackGroupPersistenceValidation.Write -logFile <write-log>`. After that process exits, run a second process with `MashBoxSDK.ContentTools.Editor.ContentPackGroupPersistenceValidation.ReadAfterRestart` and a separate log. Do not use `-quit`: the write fixture waits for the delayed save before exiting.

Passed in Unity 2022.3.62f2: matching settings script; delayed disk save; undo persistence; legacy and null-script reference recovery; preservation of colors and asset GUIDs; idempotent migration; and fresh-process restoration of nested, prefix-style, and empty groups. This validates asset persistence and migration in isolation; it does not exercise the full Content Builder UI or its Auto Nest dialog.

## Skinned mesh probe anchors

In a separate empty Unity project, copy `ContentTools/ContentPackProbeAnchors.cs` and `ContentPackProbeAnchorValidation.cs` into `Assets/Editor`. Copy `ContentPackDefinition.stub.txt` there as `ContentPackDefinition.cs`. The harness substitutes only pack data so the production prefab repair and migration code can run without Addressables or the rest of the SDK. Do not install this stand-in alongside the SDK.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.ContentTools.ContentPackProbeAnchorValidation.Run -logFile <run-log>`, then run a second process with `MashBoxSDK.ContentTools.ContentPackProbeAnchorValidation.AfterRestart`. Do not add `-quit`; the restart test waits for the migration's editor update. Both entry points exit with code 0 on success and 1 on failure. One read-only-prefab error is intentional and tests retry behavior.

Coverage: null/custom anchors, inactive renderers, unchanged root bones, ordinary mesh renderers, variants, nested prefab overrides without changing their source, duplicate/shared references, core items, idempotence, additions after migration, failed-save retry, startup discovery, and migration persistence across editor restarts. Pack add/sync/build/export integration should also be checked in a full SDK project.

Both entry points passed in Unity 2022.3.62f2 (25 assertions across two editor processes).

## Pack definition script recovery

In a separate empty Windows Unity project, copy `Editor/PackDefinitionScriptRecovery.cs`, `Editor/MapContentPackDefinition.cs` and its `.meta`, and `PackDefinitionScriptRecoveryValidation.cs` into `Assets/Editor`. Copy `PackDefinitionScriptRecovery.stub.txt` as `ContentPackDefinition.cs`, alongside the production `ContentPackDefinition.cs.meta`. This substitutes the content definition and authentication dependencies; the map definition and recovery code are production sources. Never install these stubs alongside the SDK.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.ContentTools.PackDefinitionScriptRecoveryValidation.Run -logFile <run-log>`, followed by a second process with `MashBoxSDK.ContentTools.PackDefinitionScriptRecoveryValidation.AfterRestart`. Do not add `-quit`. The fixture deletes and recreates `Assets/Content.asset` and `Assets/Map.asset`. Failure cases intentionally log warnings and errors; the entry points exit 0 on success and 1 on failure.

Coverage: healthy pack creation, original script GUID serialization, missing/wrong script association rejection, bounded reimport attempts, existing asset byte preservation, restart persistence, and creation after compilation settles. This models an invalid association; it does not reproduce the intermittent Unity import-cache failure or exercise the full SDK UI and manifest pipeline.

Passed in Unity 2022.3.62f2: 13 assertions across two editor processes.
