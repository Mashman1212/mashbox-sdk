# World prototype removal

Fixed in the linked SDK: Editor/Maps/MGTerrainEditor.SharedWorldDetails.cs.

The world inspector merged prototype definitions from every tile, but committed only density-layer deletions. A removed prototype therefore survived on sibling tiles and returned when the inspector was recreated. Commits now track removed prototype IDs and remove matching prototypes, placed instances, and density layers on sibling tiles before rebuilding index mappings. Surviving local paint maps remain attached to their own tiles.

Validation: compiled the updated editor code and preview-scene regression with Unity 6000.4.12f1's compiler and the project's editor response file (exit 0; existing warnings). git diff --check passed. The live regression has not run because no MappyX editor window was available.

Run Tools > MashBox > MG Terrain > Validate World Prototype Removal in MappyX. This uses a temporary preview scene with four differently ordered tiles and checks used/unused prototype removal, remaining layer and instance indices, local maps, Undo/Redo, and inspector recreation. It writes validation.txt here.

The before file is a change snapshot, not an installer; do not copy it over later SDK changes.
