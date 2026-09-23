# MashBox Road Tool

Open **MashBox > SDK > Map Tools > Road Tool - Road Networks**, or **MashBox > Map Tools > Road Tool**.

1. Create a Road Network. Set default width and road/shoulder materials in Network.
2. In Terrain, assign an MG Terrain World and choose the default terrain mode.
3. Create a New Road or New Loop. Click in Scene view to place points. Surfaces with colliders are used for placement; otherwise the fallback horizontal plane is used.
4. Select a point and drag its handles. Insert clicks split the nearest curve without changing its shape. Delete removes the selected point; Escape stops drawing. Unity's normal spline tools remain available for tangent editing.
5. Set width, shoulders, crown, banking, sampling, collider, materials and UV metres/offset/swapping in the road inspector. Each road uses one center spline.

Roads inherit network terrain settings unless Override Terrain is enabled. Independent leaves the spline at its authored height. Road Follows Terrain projects generated surface samples onto the assigned world's readable MG terrain meshes, preserving the source spline. Moving the road or network updates its surface. Rebuild after external terrain edits.

Terrain Follows Road is an explicit **Apply Terrain** operation, per road or for the network. It supports height offset, falloff distance/curve, strength, raise-only and lower-only modes. It uses the existing MG sculpt stroke system and supports Undo/Redo. Undo the old Apply before moving a road if its previous imprint should be removed. Overlapping roads apply in hierarchy order. Inactive roads are skipped. Terrain mesh resolution limits deformation detail.

Meshes are baked automatically to `Assets/MashBox Roads/Generated` when saving a scene, so renderers and colliders survive reload and player builds. Duplicated roads receive independent mesh assets. Save the scene before exporting it. Deleted roads may leave unused baked assets; remove those only after checking references.

The network currently groups authoring and terrain settings. It does not generate intersection meshes, traffic routing, or junction blending. Roads crossing one another retain their own surfaces. Tight curves can fold if the width exceeds the bend radius.

Run **MashBox > Validation > Validate Roads** for geometry, insertion, terrain following, terrain Apply/Undo/Redo, and independent mesh persistence checks in a temporary preview scene.
