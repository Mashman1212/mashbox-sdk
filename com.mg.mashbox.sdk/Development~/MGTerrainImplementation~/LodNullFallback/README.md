# Tree LOD empty-override fallback

GetRenderParts used C# null coalescing to choose an optional prefab override. Unity serialized empty/destroyed Object references can have a managed wrapper while comparing equal to null. Such an override bypassed the source prefab, returned no parts, and caused medium/far to reuse the close mesh.

The fix uses Unity's overloaded null comparison. No prefab, distance, density, or placement changes are required. LOD2 is the conifer's source mesh name; its position in the prefab's LODGroup determines selection.

Runtime and editor compilation passed. MGTerrainTreeLodValidation now exercises a destroyed Unity override reference as well as existing mesh selection and GPU LOD tests. The validator also logs raw source meshes on loaded conifer terrain layers to check the actual scene configuration.
