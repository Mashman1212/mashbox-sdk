# Tree distance controls

The installed SDK changes add per-prototype mid/far density for density-painted
trees. The settings are independent of the number of mesh LODs. Near density and
existing scenes remain unchanged; 0.65 mid / 0.35 far is a useful starting point
to try. The slider values are fractions of the painted population, not fractions
of the preceding band. Each threshold uses cell distance, so this is currently
a hard population change rather than dithered fading.

CPU, BRG/GPU and reflection submission honor the reduced count. Resident GPU
transforms remain allocated so restoring density does not regenerate or move trees.
This reduces rendering work, not resident transform memory. Appearance captures
retain the full painted population. Individually placed trees are unaffected.

Impostor material size settings are described in `ImpostorBaker/README.md`.
Terrain cell and GPU occlusion bounds include both normal and far sizes. After
changing material size settings, use **Rebuild Render Cache** on the terrain to
refresh cached materials and bounds. Neutral multipliers do not inflate bounds.

`staged/` contains the four installed SDK files. `compile.ps1` validates them using
this project's Unity compiler responses. `install.ps1` verifies hashes before
copying, refusing to overwrite unrelated subsequent SDK edits. Tests live in
`Editor/MGTerrainTreeLodValidation.cs` and cover actual GPU population changes,
retained transforms, independent mesh counts, zero density, migration and bounds.
