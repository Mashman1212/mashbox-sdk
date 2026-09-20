const fs = require('fs'), path = require('path');
const root = 'D:/mashbox-sdk/com.mg.mashbox.sdk';
const out = __dirname;
const files = {};
function read(p) { const original=path.join(out,'original',p); return files[p] ??= fs.readFileSync(fs.existsSync(original)?original:path.join(root,p),'utf8').replace(/\r\n/g,'\n'); }
function replace(p,a,b) { const s=read(p); if(!s.includes(a)) throw Error('Missing '+p+': '+a.slice(0,120)); files[p]=s.replace(a,b); }
const core='Runtime/Maps/Terrain/MGTerrain.cs', detail='Runtime/Maps/Terrain/MGTerrain.Details.cs', brg='Runtime/Maps/Terrain/MGTerrain.BRG.cs', residency='Runtime/Maps/Terrain/MGTerrain.Residency.cs', editor='Editor/Maps/MGTerrainEditor.Prototypes.cs';
replace(core,'public sealed class Prototype','public sealed class Prototype : ISerializationCallbackReceiver');
replace(core,'            public GameObject Prefab => m_Prefab;',`            [SerializeField, Range(1, 3), Tooltip("Maximum tree mesh LODs. Uses the prefab LODGroup; missing levels reuse the last available mesh.")]
            int m_TreeLodCount = 3;
            [SerializeField, Min(0f)] float m_TreeLod1Distance = 60f;
            [SerializeField, Min(0f)] float m_TreeLod2Distance = 200f;
            [SerializeField, Min(0f)] float m_TreeLodHysteresis = 5f;
            [SerializeField, Tooltip("Optional medium-distance prefab. Empty uses the source prefab's LODGroup.")]
            GameObject m_TreeLod1Prefab;
            [SerializeField, Tooltip("Optional far-distance prefab or mesh impostor. Empty uses the source prefab's last mesh LOD.")]
            GameObject m_TreeLod2Prefab;
            public int TreeLodCount => Kind == InstanceKind.Tree ? Mathf.Clamp(m_TreeLodCount, 1, 3) : 1;
            public float TreeLod1Distance => Mathf.Max(0f, m_TreeLod1Distance);
            public float TreeLod2Distance => Mathf.Max(TreeLod1Distance, m_TreeLod2Distance);
            public float TreeLodHysteresis => Mathf.Clamp(m_TreeLodHysteresis, 0f, TreeLod1Distance * .5f);
            internal GameObject TreeLodPrefab(int lod) => lod == 1 ? m_TreeLod1Prefab : lod == 2 ? m_TreeLod2Prefab : null;

            public void OnBeforeSerialize() { }
            public void OnAfterDeserialize()
            {
                // Existing nested prototypes may deserialize new fields as zero.
                // Zero is reserved for migration; one remains an explicit no-LOD choice.
                if (m_TreeLodCount > 0) return;
                m_TreeLodCount = 3;
                m_TreeLod1Distance = 60f;
                m_TreeLod2Distance = 200f;
                m_TreeLodHysteresis = 5f;
            }

            public GameObject Prefab => m_Prefab;`);
replace(core,'            internal Mesh mesh;\n            internal int subMesh;', '            internal int treeLodMask = 7;\n            internal Mesh mesh;\n            internal int subMesh;');
replace(core,'            internal readonly Matrix4x4 relativeMatrix;\n\n            internal RenderPart(Mesh mesh, int subMesh, Material material, Matrix4x4 relativeMatrix)', '            internal readonly Matrix4x4 relativeMatrix;\n            internal readonly int treeLodMask;\n\n            internal RenderPart(Mesh mesh, int subMesh, Material material, Matrix4x4 relativeMatrix, int treeLodMask = 7)');
replace(core,'                this.relativeMatrix = relativeMatrix;','                this.relativeMatrix = relativeMatrix;\n                this.treeLodMask = treeLodMask;');
replace(core,'List<RenderPart> GetRenderParts(Prototype prototype)','List<RenderPart> GetRenderParts(Prototype prototype, int treeLod = 0)');
replace(core,'            if (prototype.Mesh != null && prototype.Material != null)','            GameObject prefab = prototype.TreeLodPrefab(treeLod) ?? prototype.Prefab;\n            if (prototype.TreeLodPrefab(treeLod) == null && prototype.Mesh != null && prototype.Material != null)');
replace(core,`            if (prototype.Prefab == null) return result;
            HashSet<MeshRenderer> allowedRenderers = GetHighestDetailRenderers(prototype.Prefab);
            MeshRenderer[] renderers = prototype.Prefab.GetComponentsInChildren<MeshRenderer>(true);
            Matrix4x4 rootInverse = prototype.Prefab.transform.worldToLocalMatrix;`, `            if (prefab == null) return result;
            HashSet<MeshRenderer> allowedRenderers = GetHighestDetailRenderers(prefab, prototype.TreeLodPrefab(treeLod) != null ? 0 : treeLod);
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;`);
replace(core,'GetHighestDetailRenderers(GameObject prefab)','GetHighestDetailRenderers(GameObject prefab, int requestedLod = 0)');
replace(core,'                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)',`                // Mesh impostors are supported. Skip a terminal BillboardRenderer/cull-only
                // level and retain the last drawable mesh instead of losing the tree.
                int lastMeshLod = -1;
                for (int i = 0; i < lods.Length; i++)
                    foreach (Renderer renderer in lods[i].renderers)
                        if (renderer is MeshRenderer) { lastMeshLod = i; break; }
                int selectedLod = requestedLod >= 2 ? lastMeshLod : Mathf.Min(requestedLod, lastMeshLod);
                while (selectedLod > 0 && !Array.Exists(lods[selectedLod].renderers, r => r is MeshRenderer)) selectedLod--;
                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)`);
replace(core,'if (lodIndex == 0) highest.Add(meshRenderer);','if (lodIndex == selectedLod) highest.Add(meshRenderer);');
// Cache all variants without baking child transforms into tree vertices: object-space wind remains intact.
replace(detail,'            var result = new DenseDetailPrototypeParts();\n            List<RenderPart> sourceParts = GetRenderParts(prototype);',`            var result = new DenseDetailPrototypeParts();
            if (prototype.Kind == InstanceKind.Tree)
            {
                List<RenderPart> previous = null;
                for (int lod = 0; lod < prototype.TreeLodCount; lod++)
                {
                    var lodParts = GetRenderParts(prototype, lod);
                    if (lodParts.Count == 0 && previous != null) lodParts = previous;
                    previous = lodParts;
                    result.sourcePartCount += lodParts.Count;
                    foreach (var part in lodParts)
                    {
                        int match = result.parts.FindIndex(p => p.mesh == part.mesh && p.subMesh == part.subMesh
                            && p.material == part.material && p.relativeMatrix.Equals(part.relativeMatrix));
                        int mask = 1 << lod;
                        if (match >= 0) { mask |= result.parts[match].treeLodMask; result.parts.RemoveAt(match); }
                        result.parts.Add(new RenderPart(part.mesh, part.subMesh, part.material, part.relativeMatrix, mask));
                    }
                }
                m_DenseDetailPrototypeParts[prototype] = result;
                return result;
            }
            List<RenderPart> sourceParts = GetRenderParts(prototype);`);
replace(detail,'            if (prototype == null) return false;\n            if (m_DetailFadeEligibility', '            if (prototype == null || prototype.Kind == InstanceKind.Tree) return false;\n            if (m_DetailFadeEligibility');
replace(detail,'            using var profile = s_DetailCellBuildMarker.Auto();','            using var profile = s_DetailCellBuildMarker.Auto();\n            if (prototype.Kind == InstanceKind.Tree) densityScale = 1f;');
replace(detail,'                if (!m_BuildingReflectionDetails && m_CombineDenseDetailMeshes','                if (prototype.Kind != InstanceKind.Tree && !m_BuildingReflectionDetails && m_CombineDenseDetailMeshes');
replace(detail,'                    mesh = part.mesh,\n                    subMesh = part.subMesh,','                    treeLodMask = part.treeLodMask,\n                    mesh = part.mesh,\n                    subMesh = part.subMesh,');
replace(detail,'            if (!UseFixedDetailCells)\n                return visible.chunk.instanceCount;','            if (visible.prototype.Kind == InstanceKind.Tree || !UseFixedDetailCells)\n                return visible.chunk.instanceCount;');
replace(detail,'            if (!m_UseDetailDensityLod)\n                return 0;\n\n            float nearEnd = Mathf.Max(0f, m_FullDetailDensityDistance);\n            float midEnd = Mathf.Max(nearEnd, m_MidDetailDensityDistance);',`            Prototype prototype = m_Prototypes[m_DensityDetailLayers[layerIndex].PrototypeIndex];
            bool tree = prototype.Kind == InstanceKind.Tree;
            if (tree && prototype.TreeLodCount == 1) return 0;
            if (!tree && !m_UseDetailDensityLod) return 0;

            float nearEnd = tree ? prototype.TreeLod1Distance : Mathf.Max(0f, m_FullDetailDensityDistance);
            float midEnd = tree ? (prototype.TreeLodCount > 2 ? prototype.TreeLod2Distance : float.MaxValue)
                : Mathf.Max(nearEnd, m_MidDetailDensityDistance);`);
replace(detail,'            float hysteresis = Mathf.Max(0f, m_DetailDensityLodHysteresis);','            float hysteresis = tree ? prototype.TreeLodHysteresis : Mathf.Max(0f, m_DetailDensityLodHysteresis);');
replace(detail,'                if (EffectiveDetailDistance > 0f)\n                    maximumDistance', '                if (prototype.Kind != InstanceKind.Tree && EffectiveDetailDistance > 0f)\n                    maximumDistance');
replace(residency,'if (EffectiveDetailDistance > 0f) limit', 'if (sector.prototype.Kind != InstanceKind.Tree && EffectiveDetailDistance > 0f) limit');
replace(detail,'                for (int batchIndex = 0; batchIndex < visible.chunk.batches.Count; batchIndex++)\n                {\n                    submittedInstances', '                for (int batchIndex = 0; batchIndex < visible.chunk.batches.Count; batchIndex++)\n                {\n                    if (!TreeBatchVisible(visible.chunk.batches[batchIndex], visible.densityLod)) continue;\n                    submittedInstances');
// Tree shadows follow the prototype, independently of the grass shadow switch.
files[detail]=read(detail).replace('m_AppearanceCaptureCamera != null ? ShadowCastingMode.On : m_DenseDetailShadows','m_AppearanceCaptureCamera != null ? ShadowCastingMode.On : (visible.prototype.Kind == InstanceKind.Tree || m_DenseDetailShadows)');
replace(brg,'                signature = unchecked(signature * 31 + allowed);','                signature = unchecked(signature * 31 + allowed);\n                signature = unchecked(signature * 31 + visible.densityLod);');
replace(brg,'ShadowCastingMode shadowCasting = m_DenseDetailShadows','ShadowCastingMode shadowCasting = visible.prototype.Kind == InstanceKind.Tree || m_DenseDetailShadows');
replace(brg,'                    AppendBrgMatrices(visible.chunk.batches[batchIndex], shadowCasting, allowed, visible.chunk.layerIndex, visible.chunk.instanceCount, CanFadeDetail(visible.prototype));','                    if (TreeBatchVisible(visible.chunk.batches[batchIndex], visible.densityLod))\n                        AppendBrgMatrices(visible.chunk.batches[batchIndex], shadowCasting, allowed, visible.chunk.layerIndex, visible.chunk.instanceCount, CanFadeDetail(visible.prototype));');
// Persistent aliases share a GPU matrix range for matching child transforms across all tree LODs/submeshes.
replace(brg,'            internal DensityDetailChunk chunk;\n            internal GpuProceduralBuildGroup group;', '            internal DensityDetailChunk chunk;\n            internal ResidentGpuCell transformSource;\n            internal GpuProceduralBuildGroup group;');
replace(brg,'        readonly List<ResidentGpuCell> m_ResidentGpuCells', '        readonly Dictionary<(DensityDetailChunk, Matrix4x4), ResidentGpuCell> m_TreeTransformSources = new Dictionary<(DensityDetailChunk, Matrix4x4), ResidentGpuCell>();\n        readonly Dictionary<DensityDetailChunk, int> m_ResidentTreeLods = new Dictionary<DensityDetailChunk, int>();\n        readonly List<ResidentGpuCell> m_ResidentGpuCells');
replace(brg,'                if (cell.capacity > 0) m_FreeGpuRanges.Add(new FreeGpuRange(cell.start, cell.capacity));\n                m_ResidentGpuCells.RemoveAt(i);', `                if (cell.transformSource == null)
                {
                    if (cell.capacity > 0) m_FreeGpuRanges.Add(new FreeGpuRange(cell.start, cell.capacity));
                    if (cell.group.batch.prototype.Kind == InstanceKind.Tree)
                        m_TreeTransformSources.Remove((cell.chunk, cell.group.relativeMatrix));
                }
                m_ResidentGpuCells.RemoveAt(i);`);
replace(brg,'                m_ResidentVisibleCounts.Clear();\n                int remaining = budget', '                m_ResidentVisibleCounts.Clear();\n                m_ResidentTreeLods.Clear();\n                int remaining = budget');
replace(brg,'                    m_ResidentVisibleCounts[visible.chunk] = allowed;\n                    remaining', '                    m_ResidentVisibleCounts[visible.chunk] = allowed;\n                    m_ResidentTreeLods[visible.chunk] = visible.densityLod;\n                    remaining');
replace(brg,'int population = keepAllResident ? resident.chunk.instanceCount', 'int population = keepAllResident || resident.prototype.Kind == InstanceKind.Tree ? resident.chunk.instanceCount');
replace(brg,'var shadow = m_DenseDetailShadows ? resident.prototype.ShadowCasting', 'var shadow = resident.prototype.Kind == InstanceKind.Tree || m_DenseDetailShadows ? resident.prototype.ShadowCasting');
replace(brg,'                            prototypeGroups.groups.Add(group);', '                            group.batch.treeLodMask |= part.treeLodMask;\n                            if (!prototypeGroups.groups.Contains(group)) prototypeGroups.groups.Add(group);');
replace(brg,'                            cell = new ResidentGpuCell { chunk = resident.chunk, group = group };', `                            cell = new ResidentGpuCell { chunk = resident.chunk, group = group };
                            if (resident.prototype.Kind == InstanceKind.Tree)
                            {
                                var transformKey = (resident.chunk, group.relativeMatrix);
                                if (m_TreeTransformSources.TryGetValue(transformKey, out var source)) cell.transformSource = source;
                                else m_TreeTransformSources.Add(transformKey, cell);
                            }`);
replace(brg,'                        cell.visible = allowed;\n                        group.outputCount += allowed;\n                        visibleInstances += allowed;', `                        m_ResidentTreeLods.TryGetValue(resident.chunk, out int selectedLod);
                        cell.visible = TreeBatchVisible(group.batch, selectedLod) ? allowed : 0;
                        group.outputCount += cell.visible;
                        visibleInstances += cell.visible;`);
replace(brg,'                    if (cell.capacity >= cell.requested) continue;', '                    if (cell.transformSource != null || cell.capacity >= cell.requested) continue;');
replace(brg,'                    if (cell.generation == m_DetailGpuGeneration && cell.population >= cell.requested) continue;', '                    if (cell.transformSource != null || (cell.generation == m_DetailGpuGeneration && cell.population >= cell.requested)) continue;');
replace(brg,'                bool prepared = FinalizeResidentGpuVisibility(submitted, false);', `                foreach (var cell in m_ResidentGpuCells)
                    if (cell.transformSource != null)
                    {
                        var source = cell.transformSource;
                        cell.start = source.start; cell.capacity = source.capacity;
                        cell.population = source.population; cell.generation = source.generation;
                    }
                bool prepared = FinalizeResidentGpuVisibility(submitted, false);`);
replace(brg,'                if (!m_ResidentVisibleCounts.TryGetValue(visible.chunk, out int previous) || previous != allowed) return false;', '                if (!m_ResidentVisibleCounts.TryGetValue(visible.chunk, out int previous) || previous != allowed) return false;\n                if (visible.prototype.Kind == InstanceKind.Tree && (!m_ResidentTreeLods.TryGetValue(visible.chunk, out int lod) || lod != visible.densityLod)) return false;');
replace(brg,'            m_ResidentVisibleCounts.Clear();\n            foreach (var group', '            m_ResidentVisibleCounts.Clear();\n            m_ResidentTreeLods.Clear();\n            foreach (var group');
replace(brg,'                m_ResidentVisibleCounts[visible.chunk] = allowed;\n                foreach (var group', '                m_ResidentVisibleCounts[visible.chunk] = allowed;\n                m_ResidentTreeLods[visible.chunk] = visible.densityLod;\n                foreach (var group');
replace(brg,'                    if (!group.residentCells.TryGetValue(visible.chunk, out var cell)) continue;', '                    if (!TreeBatchVisible(group.batch, visible.densityLod) || !group.residentCells.TryGetValue(visible.chunk, out var cell)) continue;');
replace(brg,'                group.batch.mesh = mesh;','                group.batch.treeLodMask = 0;\n                group.batch.mesh = mesh;');
replace(brg,'            m_GpuPrototypeGroups.Clear();','            m_GpuPrototypeGroups.Clear();\n            m_TreeTransformSources.Clear();\n            m_ResidentTreeLods.Clear();');
// CPU reflection fallback must select one variant, and reflection BRG must use the probe's own distance.
replace(core,'if (EffectiveDetailDistance > 0) distance = Mathf.Min(distance, EffectiveDetailDistance);','if (prototype.Kind != InstanceKind.Tree && EffectiveDetailDistance > 0) distance = Mathf.Min(distance, EffectiveDetailDistance);');
replace(core,'                        foreach (var batch in chunk.batches)\n                            QueueDenseDetailBatch', '                        int treeLod = SelectTreeLod(prototype, candidate.distance);\n                        foreach (var batch in chunk.batches)\n                            if (TreeBatchVisible(batch, treeLod)) QueueDenseDetailBatch');
replace(core,'            Bounds bounds = cell.chunk.worldBounds;\n            Vector3 center', `            Bounds bounds = cell.chunk.worldBounds;
            if (cell.group.batch.prototype.Kind == InstanceKind.Tree)
            {
                float distance = Mathf.Sqrt(DetailBoundsDistanceSquared(bounds, context.lodParameters.cameraPosition));
                var prototype = cell.group.batch.prototype;
                if (prototype.MaximumDrawDistance > 0f && distance > prototype.MaximumDrawDistance) return false;
                if (!TreeBatchVisible(cell.group.batch, SelectTreeLod(prototype, distance))) return false;
            }
            Vector3 center`);
replace(editor,'            DrawPrototypeFields(selected, "m_Prefab", "m_Mesh", "m_Material");', `            bool isTree = selected.FindPropertyRelative("m_Kind").enumValueIndex == (int)MGTerrain.InstanceKind.Tree;
            DrawPrototypeFields(selected, "m_Prefab", "m_Mesh", "m_Material", "m_TreeLodCount", "m_TreeLod1Distance", "m_TreeLod2Distance", "m_TreeLodHysteresis", "m_TreeLod1Prefab", "m_TreeLod2Prefab");
            if (isTree)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Tree LODs", EditorStyles.boldLabel);
                var lodCount = selected.FindPropertyRelative("m_TreeLodCount");
                EditorGUILayout.PropertyField(lodCount, new GUIContent("LOD Count"));
                if (lodCount.intValue > 1)
                {
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Distance"), new GUIContent("Medium Distance"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Prefab"), new GUIContent("Medium Prefab Override"));
                }
                if (lodCount.intValue > 2)
                {
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Distance"), new GUIContent("Far Distance"));
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Prefab"), new GUIContent("Far Prefab Override"));
                }
                if (lodCount.intValue > 1)
                {
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLodHysteresis"), new GUIContent("Transition Hysteresis"));
                    EditorGUILayout.HelpBox("Uses the prefab's LOD0, LOD1 and last mesh LOD. Optional overrides must share its pivot and scale. Missing mesh levels reuse the last available mesh; lower-detail geometry is not generated automatically.", MessageType.Info);
                }
            }`);
// CPU fallback also shares matrix chunks for matching transforms across LODs.
replace(detail,`            var matricesByPart = new List<Matrix4x4>[parts.Count];
            for (int index = 0; index < parts.Count; index++)
                matricesByPart[index] = new List<Matrix4x4>();`, `            var matricesByPart = new List<Matrix4x4>[parts.Count];
            var matrixOwners = new int[parts.Count];
            var treeMatrices = new Dictionary<Matrix4x4, int>();
            for (int index = 0; index < parts.Count; index++)
            {
                int owner = index;
                if (prototype.Kind == InstanceKind.Tree && treeMatrices.TryGetValue(parts[index].relativeMatrix, out int shared)) owner = shared;
                else treeMatrices[parts[index].relativeMatrix] = index;
                matrixOwners[index] = owner;
                matricesByPart[index] = owner == index ? new List<Matrix4x4>() : matricesByPart[owner];
            }`);
replace(detail,'                            matricesByPart[partIndex].Add(instanceMatrix * parts[partIndex].relativeMatrix);','                            if (matrixOwners[partIndex] == partIndex) matricesByPart[partIndex].Add(instanceMatrix * parts[partIndex].relativeMatrix);');
replace(detail,'            int permutationStride = GetCoprimeStride(generated, Hash(orderHash + 0x9E3779B9u));','            int permutationStride = GetCoprimeStride(generated, Hash(orderHash + 0x9E3779B9u));\n            var treeBatchesByTransform = new Dictionary<Matrix4x4, DrawBatch>();');
replace(detail,'                for (int start = 0; start < matrices.Count; start += 1023)\n                {', `                if (prototype.Kind == InstanceKind.Tree && treeBatchesByTransform.TryGetValue(part.relativeMatrix, out var sharedBatch))
                {
                    batch.matrixChunks.AddRange(sharedBatch.matrixChunks);
                    batch.grassSliceChunks.AddRange(sharedBatch.grassSliceChunks);
                    if (batch.matrixChunks.Count > 0) chunk.batches.Add(batch);
                    continue;
                }
                if (prototype.Kind == InstanceKind.Tree) treeBatchesByTransform.Add(part.relativeMatrix, batch);
                for (int start = 0; start < matrices.Count; start += 1023)
                {`);
// Conservative bounds include every tree LOD's authored geometry and painted scale.
replace(detail,'            internal int sourcePartCount;','            internal int sourcePartCount;\n            internal Bounds treeBounds;');
replace(detail,'                m_DenseDetailPrototypeParts[prototype] = result;\n                return result;\n            }\n            List<RenderPart> sourceParts', `                bool first = true;
                foreach (var part in result.parts)
                {
                    var bounds = TransformBounds(part.mesh.bounds, part.relativeMatrix);
                    if (first) { result.treeBounds = bounds; first = false; }
                    else result.treeBounds.Encapsulate(bounds);
                }
                m_DenseDetailPrototypeParts[prototype] = result;
                return result;
            }
            List<RenderPart> sourceParts`);
replace(detail,'            if (m_AppearanceCaptureCamera != null && m_AppearanceCaptureDetailTilt > 0f)\n                localBounds.Expand(maximumHeight * 2f);',`            if (TryGetTreeLayerBounds(layer, out Bounds treeBounds))
            {
                float radius = Mathf.Sqrt(Mathf.Pow(Mathf.Max(Mathf.Abs(treeBounds.min.x), Mathf.Abs(treeBounds.max.x)), 2f)
                    + Mathf.Pow(Mathf.Max(Mathf.Abs(treeBounds.min.z), Mathf.Abs(treeBounds.max.z)), 2f));
                localBounds.SetMinMax(new Vector3(minX - radius, minY + yOffset + treeBounds.min.y, minZ - radius),
                    new Vector3(maxX + radius, maxY + yOffset + treeBounds.max.y, maxZ + radius));
            }
            if (m_AppearanceCaptureCamera != null && m_AppearanceCaptureDetailTilt > 0f)
                localBounds.Expand(maximumHeight * 2f);`);
replace(detail,'            int firstSectorX = 0, firstSectorZ = 0, endSectorX = width, endSectorZ = height;', `            int firstSectorX = 0, firstSectorZ = 0, endSectorX = width, endSectorZ = height;
            float treePadding = TryGetTreeLayerBounds(layer, out Bounds treeWindowBounds)
                ? treeWindowBounds.center.magnitude + treeWindowBounds.extents.magnitude : 0f;`);
replace(detail,'maximumDistance / Mathf.Abs(terrainMatrix.m00), surfaceBounds.min.x','maximumDistance / Mathf.Abs(terrainMatrix.m00) + treePadding, surfaceBounds.min.x');
replace(detail,'maximumDistance / Mathf.Abs(terrainMatrix.m22), surfaceBounds.min.z','maximumDistance / Mathf.Abs(terrainMatrix.m22) + treePadding, surfaceBounds.min.z');
replace(brg,'                if (layer != null)\n                    maximum = Mathf.Max(maximum, layer.MaxHeight);',`                if (layer != null)
                {
                    maximum = Mathf.Max(maximum, layer.MaximumPaintedHeight);
                    if (TryGetTreeLayerBounds(layer, out Bounds treeBounds))
                        maximum = Mathf.Max(maximum, (treeBounds.center.magnitude + treeBounds.extents.magnitude + Mathf.Abs(layer.YOffset))
                            * transform.localToWorldMatrix.lossyScale.magnitude);
                }`);
const world='Runtime/Maps/Terrain/MGTerrain.World.cs';
replace(world,'                bool outside = UsesWorldBudget', `                // Grass streaming distance must not unload a tile that still has visible trees.
                foreach (var layer in m_DensityDetailLayers)
                {
                    if (layer == null || !layer.RenderingEnabled || layer.PaletteSourceOnly
                        || (uint)layer.PrototypeIndex >= m_Prototypes.Count) continue;
                    var prototype = m_Prototypes[layer.PrototypeIndex];
                    if (prototype == null || prototype.Kind != InstanceKind.Tree) continue;
                    float treeDistance = prototype.MaximumDrawDistance > 0f ? prototype.MaximumDrawDistance : camera.farClipPlane;
                    unloadDistance = Mathf.Max(unloadDistance, treeDistance + 64f);
                }
                bool outside = UsesWorldBudget`);
// Budget classification stays constant when a tree changes mesh LOD. The hard population cap still applies.
for (const p of [detail,brg,world]) files[p]=read(p).replaceAll('visible.densityLod == 0', '(visible.prototype.Kind == InstanceKind.Tree || visible.densityLod == 0)');
replace(detail,'TreeBatchVisible(visible.chunk.batches[batchIndex], visible.densityLod)', 'TreeBatchVisible(visible.chunk.batches[batchIndex], m_AppearanceCaptureCamera != null ? 0 : visible.densityLod)');
replace(core,'            ReleaseInstancedMaterials();\n        }\n\n        void OnValidate()', '            ReleaseInstancedMaterials();\n            m_TreeInstanceCells.Clear();\n        }\n\n        void OnValidate()');
// Helpers and spatially batched serialized tree placements are kept in the same compilation unit.
const extra=fs.readFileSync(path.join(out,'tree-runtime.txt'),'utf8');
files[core]=read(core)+'\n'+extra;
replace(core,'            m_DrawBatches.Clear();\n            ReleaseDetailRenderCache();','            m_DrawBatches.Clear();\n            m_TreeInstanceCells.Clear();\n            ReleaseDetailRenderCache();');
replace(core,'                List<RenderPart> parts = partsByPrototype[prototypeIndex];','                if (m_Prototypes[prototypeIndex]?.Kind == InstanceKind.Tree) continue;\n                List<RenderPart> parts = partsByPrototype[prototypeIndex];');
replace(core,'            MeshRenderer renderer = MeshRenderer;\n            m_WorldBounds', '            BuildTreeInstanceCells(terrainLocalToWorld);\n            MeshRenderer renderer = MeshRenderer;\n            m_WorldBounds');
replace(core,'            RenderDensityDetails(camera, planes);','            DrawTreeInstanceCells(camera, planes);\n            RenderDensityDetails(camera, planes);');
replace(core,'            foreach (var batch in m_DrawBatches) DrawBatchInstances(batch, camera);','            foreach (var batch in m_DrawBatches) DrawBatchInstances(batch, camera);\n            DrawTreeInstanceCells(camera, m_InstanceFrustumPlanes);');
for(const [p,s] of Object.entries(files)) {
 const dest=path.join(out,'staged',p);fs.mkdirSync(path.dirname(dest),{recursive:true});
 const original=path.join(out,'original',p);fs.mkdirSync(path.dirname(original),{recursive:true});
 if(!fs.existsSync(original)) fs.copyFileSync(path.join(root,p),original);
 fs.writeFileSync(dest,s.replace(/\n/g,'\r\n'));
}
fs.writeFileSync(path.join(out,'files.json'),JSON.stringify(Object.keys(files),null,2));
console.log('Staged '+Object.keys(files).length+' files.');
