# Isolated Unity validation

## Unity version compatibility

Studio's assembly supports Unity 2022.3 and newer. The SDK package's existing minimum patch requirement still applies, as does the `MashBoxDev` access define. Unity 2022.3 uses a compatibility path for hiding built-in SceneView overlays; Unity 6 uses the public overlay API.

To check startup in either editor version, install the SDK in a disposable project, enable `MashBoxDev`, and copy `StudioCompatibilitySmoke.cs` into `Assets/Editor`. Run with `-batchmode -projectPath <isolated-project> -executeMethod StudioCompatibilitySmoke.Run -logFile <log-path>`. The fixture exits Unity and writes `compatibility-results.txt` in the project root. It checks a fresh in-memory take with the default human, isolated viewport and overlays, all four Studio modes, skinning animation sampling, and returning to Animation without losing the take. It does not validate interactive mouse input or FBX export.

Validated on 2026-10-25: all 12 startup checks passed in Unity 2022.3.62f2. Studio and its SDK dependencies compiled against 2022.3; Studio also compiled against Unity 6000.4.12f1. The 2022.3 compiler check passed both with and without `MashBoxDev`.

These fixtures are excluded from SDK compilation. Never invoke their Run methods in a working game project: they exit the editor after testing.

They were run in Unity 6000.4.12f1 using an isolated project at `D:/ProjectX - U6/AnimationStudioValidation`. Copy the Animation Studio Editor folder and these two fixtures into `Assets/Studio/Editor` in that project. Copy the project's own licensed Final IK dependency there, and copy `Skeleton.fbx` and `SM_Body_01.fbx` (with their importer metadata) into `Assets/Character`.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.AnimationStudio.StudioValidation.Run -logFile <log-path>` for rig and baking validation. In a second run use `MashBoxSDK.AnimationStudio.StudioPlayValidation.Run` for Play Mode persistence. Use a fresh project or remove only the generated validation take/clip assets before rerunning; these fixture paths are fixed.

Passed: SM body remapping; no cloned gameplay scripts; Final IK initialization and target reach; repeatable evaluation; pinned feet under hip motion; Generic positions and rotations on a Generic Animator; Humanoid recognition, muscle/body curves and hand position playback; stepped key timing; Play Mode creation/solving/baking and persistence across returning to Edit Mode.

The full game project also compiled the SDK assembly successfully. Visual inspection identified and corrected the preview scene's camera culling mask and inherited scene overlays.
