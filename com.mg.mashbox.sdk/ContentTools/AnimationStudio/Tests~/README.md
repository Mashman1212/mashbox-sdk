# Isolated Unity validation

These fixtures are excluded from SDK compilation. Never invoke their Run methods in a working game project: they exit the editor after testing.

They were run in Unity 6000.4.12f1 using an isolated project at `D:/ProjectX - U6/AnimationStudioValidation`. Copy the Animation Studio Editor folder and these two fixtures into `Assets/Studio/Editor` in that project. Copy the project's own licensed Final IK dependency there, and copy `Skeleton.fbx` and `SM_Body_01.fbx` (with their importer metadata) into `Assets/Character`.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.AnimationStudio.StudioValidation.Run -logFile <log-path>` for rig and baking validation. In a second run use `MashBoxSDK.AnimationStudio.StudioPlayValidation.Run` for Play Mode persistence. Use a fresh project or remove only the generated validation take/clip assets before rerunning; these fixture paths are fixed.

Passed: SM body remapping; no cloned gameplay scripts; Final IK initialization and target reach; repeatable evaluation; pinned feet under hip motion; Generic positions and rotations on a Generic Animator; Humanoid recognition, muscle/body curves and hand position playback; stepped key timing; Play Mode creation/solving/baking and persistence across returning to Edit Mode.

The full game project also compiled the SDK assembly successfully. Visual inspection identified and corrected the preview scene's camera culling mask and inherited scene overlays.
