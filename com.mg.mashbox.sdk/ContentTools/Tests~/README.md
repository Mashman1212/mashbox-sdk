# Content pack group persistence validation

These fixtures are excluded from SDK compilation. Run only in an isolated Unity project: the entry points exit the editor.

Copy `Editor/ContentPackGroupSettings.cs`, its `.meta` file, and `ContentPackGroupPersistenceValidation.cs` into the isolated project's `Assets/Editor` folder. Keep the script's metadata so the fixture can check its serialized GUID. No SDK dependencies are required.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.ContentTools.Editor.ContentPackGroupPersistenceValidation.Write -logFile <write-log>`. After that process exits, run a second process with `MashBoxSDK.ContentTools.Editor.ContentPackGroupPersistenceValidation.ReadAfterRestart` and a separate log. Do not use `-quit`: the write fixture waits for the delayed save before exiting.

Passed in Unity 2022.3.62f2: matching settings script; delayed disk save; undo persistence; legacy and null-script reference recovery; preservation of colors and asset GUIDs; idempotent migration; and fresh-process restoration of nested, prefix-style, and empty groups. This validates asset persistence and migration in isolation; it does not exercise the full Content Builder UI or its Auto Nest dialog.
