# Chairlifts

Use **MashBox SDK → Map Tools → Gameplay → Chairlifts → Create Chairlift**.

The SDK prefab includes **Loading Bay (Bottom)**, **Chairlift Cable System** with tower proxies, and **Loading Bay (Top)**. Position and rotate the bay roots and move the towers to fit the map. The four cable handoff points follow the bay endpoints. The bottom bay starts at the placement handle.

Select a bay's Cable System to edit its slow path or chair count. Select Chairlift Cable System to change the main cable's sag and chair spacing. The lift root controls overall speed, direction and stopping. The cyan boxes outline the hut and transfer machinery individually. Creators supply their own landing pads and terrain beneath the stations; the SDK contains no physical bay meshes, chairs or colliders.

ProjectX resolves the proxy's definitionId through a ChairliftRuntimeDefinition in Resources/Chairlifts. The default definition uses game-owned bottom/top loading-bay prefabs extracted from Tiger Chair, plus the existing chair rig and tower prefab. Route paths and transfer triggers are generated from the proxy data; station prefabs contain only the hut and transfer-bay geometry, without landing-pad blocks, additional cable paths or chair spawners.

The previous two-tower starter is not automatically modified. Create a new lift to use the complete template. Existing physical Tiger Chair instances can be converted using **Convert Selected Legacy Lift**, with Undo support.

ProjectX provides **MashBox → Maps → Chairlifts → Validate Loading Bay Integration** to check creation, game prefab placement, transfers and route continuity after moving the bays. **Rebuild Default Station Assets from Tiger Chair** regenerates the default physical bay prefabs and SDK template from the source Tiger Chair prefab.
