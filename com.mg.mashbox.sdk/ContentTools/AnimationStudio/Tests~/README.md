# Isolated Unity validation

These fixtures are excluded from SDK compilation. Never invoke their Run methods in a working game project: they exit the editor after testing.

They were run in Unity 6000.4.12f1 using an isolated project at `D:/ProjectX - U6/AnimationStudioValidation`. Copy the Animation Studio Editor folder and these two fixtures into `Assets/Studio/Editor` in that project. Copy the project's own licensed Final IK dependency there, and copy `Skeleton.fbx` and `SM_Body_01.fbx` (with their importer metadata) into `Assets/Character`.

Run Unity with `-batchmode -nographics -projectPath <isolated-project> -executeMethod MashBoxSDK.AnimationStudio.StudioValidation.Run -logFile <log-path>` for rig and baking validation. In a second run use `MashBoxSDK.AnimationStudio.StudioPlayValidation.Run` for Play Mode persistence. Use a fresh project or remove only the generated validation take/clip assets before rerunning; these fixture paths are fixed.

Passed: SM body remapping; no cloned gameplay scripts; Final IK initialization and target reach; repeatable evaluation; pinned feet under hip motion; Generic positions and rotations on a Generic Animator; Humanoid recognition, muscle/body curves and hand position playback; stepped key timing; Play Mode creation/solving/baking and persistence across returning to Edit Mode.

The full game project also compiled the SDK assembly successfully. Visual inspection identified and corrected the preview scene's camera culling mask and inherited scene overlays.

Outline validation now uses an actual SceneView in Unity 6000.4.12f1 with HDRP 17.4.0. In an isolated project, import the Studio editor assembly/sources and validation fixtures, copy the source project HDRP settings and Linear Player Settings, place its pipeline asset at Assets/Settings/HDRenderPipelineAsset.asset, and copy a saved workspace to Assets/WorkspaceSource.txt. Put final-native in Assets/Variant.txt and run StudioHdrpOutlineValidation.Run. It exports scene/overlay composites at two angles, checks stationary-frame differences, then runs the geometry, FPS, menu, controls, hover, and workflow regressions before exiting. StudioEdgeRenderingValidation now checks outline geometry and viewing-ray offsets; Camera.Render alone did not validate the HDRP SceneView overlay path.

StudioWorkspaceStorageValidation.Run is a two-launch fixture for a fresh, isolated Unity project. Include the Studio editor assembly and preview shaders. The first launch writes project-root .workspace files and checks independent Save As, legacy conversion and rejection of Assets paths. Restart the editor and run it again to verify meshes, blend shapes, channels, material references, subsequent import/create and creation Undo/Redo. Run only in a disposable project: it creates named test assets and workspaces.
