Scene Sculpt Copy

Select an MG Terrain tile, then click Use Scene Sculpt Copy in its inspector.
The same control is visible for managed world tiles and multi-tile selections.
It creates a private sculpt mesh and private collision meshes embedded in the scene.
Save your working scene normally. Use the saved scene or loaded tile as the Terrain Merge source later.
There is no whole-world-copy action. Materials, texture/control maps and vegetation remain shared.
The scene file holds the additional mesh data; no separate sculpt/collider asset files are created.
Regular sculpting and grid repair honor scene storage. Merge destinations in this mode retain scene storage too.

Validation: Unity 6000.4.12f1 runtime/editor compilation passed.
Scene mesh save/close/reopen probe passed with zero asset files.
Scene-copy validation passed: unique meshes, shape/UV retention, independent colliders,
grouped Undo/Redo, subsequent sculpt isolation, and zero new assets during copy/sculpt/save.
Existing full Terrain Merge suite also passed, using a saved scene-stored sculpt as its source.
No working scenes were modified by installation or validation.
