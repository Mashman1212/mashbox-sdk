const fs = require('fs'), path = require('path');
const dir = __dirname;
function replace(s, a, b) { if (!s.includes(a)) throw Error('Missing: ' + a); return s.replace(a,b); }
let runtime = fs.readFileSync(path.join(dir,'MGTerrain.Details.before.cs'),'utf8').replaceAll('\r\n','\n');
runtime = replace(runtime, '        public enum DetailQualityPreset', '        public enum DetailPaintMap { Density, Size, GrassIds }\n\n        public enum DetailQualityPreset');
runtime = replace(runtime, '            internal void AddToRepresentedInstanceCount(long delta)', `            // The editor uses this only for an identical copy (or a neutral new map).
            // Applying serialized component properties here invokes OnValidate and
            // destroys every layer's render cache before the first brush dab.
            public void AssignPaintMapCopy(Texture2D copy, DetailPaintMap channel)
            {
                if (copy == null) throw new ArgumentNullException(nameof(copy));
                switch (channel)
                {
                    case DetailPaintMap.Density: m_DensityMap = copy; break;
                    case DetailPaintMap.Size: m_SizeMap = copy; break;
                    case DetailPaintMap.GrassIds: m_GrassIdMap = copy; break;
                    default: throw new ArgumentOutOfRangeException(nameof(channel));
                }
            }

            internal void AddToRepresentedInstanceCount(long delta)`);
runtime = replace(runtime, '            internal bool valid;\n            internal int geometryVersion;', '            internal bool valid;\n            internal bool paintDirty;\n            internal int geometryVersion;');
runtime = replace(runtime, '            // Cached candidates can hold references to cells replaced by painting.\n            m_FixedCandidateCaches.Clear();\n', '');
runtime = replace(runtime, '            var remove = new List<DetailChunkKey>();\n            foreach (var pair in m_DensityDetailCache)', `            RefreshPaintedCandidates(layerIndex, normalizedRegion, width, height);
            var remove = new List<DetailChunkKey>();
            foreach (var pair in m_DensityDetailCache)`);
runtime = replace(runtime, '        void InvalidateDetailRenderCache()', `        void RefreshPaintedCandidates(int layerIndex, Rect region, int width, int height)
        {
            if (!m_FixedCandidateCaches.TryGetValue(layerIndex, out var cache)) return;
            // Retain bounds and occupancy for untouched cells, including all other layers.
            // The next selection rebuilds this layer's list to admit newly occupied cells
            // and remove references to cells replaced by the brush.
            cache.paintDirty = true;
            if (cache.occupiedCells == null || cache.width != width || cache.height != height) return;
            int size = cache.cellSize;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(region.xMin * width / size), 0, cache.occupiedColumns - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(region.xMax * width / size), 0, cache.occupiedColumns - 1);
            int rows = cache.occupiedCells.Length / cache.occupiedColumns;
            int z0 = Mathf.Clamp(Mathf.FloorToInt(region.yMin * height / size), 0, rows - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt(region.yMax * height / size), 0, rows - 1);
            var density = m_DensityDetailLayers[layerIndex].DensityMap.GetPixelData<ushort>(0);
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                bool occupied = false;
                for (int z = cz * size; z < Mathf.Min(height, (cz + 1) * size) && !occupied; z++)
                    for (int x = cx * size; x < Mathf.Min(width, (cx + 1) * size); x++)
                        if (density[z * width + x] != 0) { occupied = true; break; }
                cache.occupiedCells[cz * cache.occupiedColumns + cx] = occupied;
            }
        }

        void InvalidateDetailRenderCache()`);
runtime = replace(runtime, 'if (geometryUnchanged && KeepAllDetailCellsResident && candidates.Count > 0)', 'if (geometryUnchanged && !cache.paintDirty && KeepAllDetailCellsResident && candidates.Count > 0)');
runtime = replace(runtime, 'if (geometryUnchanged && cache.cameraPosition == cameraWorld && cache.distance == maximumDistance)', 'if (geometryUnchanged && !cache.paintDirty && cache.cameraPosition == cameraWorld && cache.distance == maximumDistance)');
runtime = replace(runtime, '                cache.valid = true;\n                cache.cameraPosition', '                cache.valid = true;\n                cache.paintDirty = false;\n                cache.cameraPosition');
fs.writeFileSync(path.join(dir,'MGTerrain.Details.cs'),runtime);
let editor = fs.readFileSync(path.join(dir,'MGTerrainEditor.before.cs'),'utf8').replaceAll('\r\n','\n');
editor = replace(editor, `                serializedObject.Update();
                var layer = m_DensityDetailLayers.GetArrayElementAtIndex(m_PaintDetailIndex);
                layer.FindPropertyRelative(m_PaintChannel == 0 ? "m_DensityMap" : "m_SizeMap").objectReferenceValue = copy;
                serializedObject.ApplyModifiedProperties();`, `                detail.AssignPaintMapCopy(copy, m_PaintChannel == 0 ? MGTerrain.DetailPaintMap.Density : MGTerrain.DetailPaintMap.Size);
                serializedObject.Update();`);
editor = replace(editor, `                    serializedObject.Update();
                    m_DensityDetailLayers.GetArrayElementAtIndex(m_PaintDetailIndex).FindPropertyRelative("m_GrassIdMap").objectReferenceValue = ids;
                    serializedObject.ApplyModifiedProperties();`, `                    detail.AssignPaintMapCopy(ids, MGTerrain.DetailPaintMap.GrassIds);
                    serializedObject.Update();`);
// An empty/no-op brush must not evict cells (e.g. thinning already empty space).
editor = replace(editor, '            var pixels = m_StrokeMap.GetPixelData<ushort>(0);\n            for (int z = z0;', '            var pixels = m_StrokeMap.GetPixelData<ushort>(0);\n            bool changed = false;\n            for (int z = z0;');
editor = replace(editor, '                    var ids = m_GrassStrokeIds.GetPixelData<byte>(0); ids[index] = (byte)m_GrassPaintSubId;', '                    var ids = m_GrassStrokeIds.GetPixelData<byte>(0);\n                    changed |= ids[index] != (byte)m_GrassPaintSubId;\n                    ids[index] = (byte)m_GrassPaintSubId;');
editor = replace(editor, '                pixels[index] = m_PaintChannel == 0 ? (ushort)Mathf.RoundToInt(next) : Mathf.FloatToHalf(next);', '                ushort value = m_PaintChannel == 0 ? (ushort)Mathf.RoundToInt(next) : Mathf.FloatToHalf(next);\n                changed |= pixels[index] != value;\n                pixels[index] = value;');
editor = replace(editor, '            Rect region = Rect.MinMaxRect(x0 / (float)w,', '            PaintWorldNeighbours(terrain, point, erase);\n            if (!changed) return;\n            Rect region = Rect.MinMaxRect(x0 / (float)w,');
editor = replace(editor, '            m_HasPendingPaint = true;\n            PaintWorldNeighbours(terrain, point, erase);', '            m_HasPendingPaint = true;');
fs.writeFileSync(path.join(dir,'MGTerrainEditor.cs'),editor);
const root = 'D:/mashbox-sdk/com.mg.mashbox.sdk';
for (const assembly of ['MashBoxSDK','Assembly-CSharp-Editor']) {
 let rsp = fs.readFileSync('D:/MappyX/Library/Bee/artifacts/500b0aE.dag/'+assembly+'.rsp','utf8');
 rsp = rsp.replace(/^-out:.*$/m, '-out:"'+dir+'/'+assembly+'.dll"').replace(/^-refout:.*$/m, '-refout:"'+dir+'/'+assembly+'.ref.dll"');
 rsp = rsp.replaceAll(root+'/Runtime/Maps/Terrain/MGTerrain.Details.cs', dir+'/MGTerrain.Details.cs').replaceAll(root+'/Editor/Maps/MGTerrainEditor.cs',dir+'/MGTerrainEditor.cs');
 if (assembly !== 'MashBoxSDK') {
  rsp = rsp.replace(/-r:"[^"\r\n]*\/MashBoxSDK(?:\.ref)?\.dll"/g, '-r:"'+dir+'/MashBoxSDK.ref.dll"');
  if (!rsp.includes('MGTerrainPaintValidation.cs')) rsp += '\n"D:/mashbox-sdk/com.mg.mashbox.sdk/Editor/Maps/Validation/MGTerrainPaintValidation.cs"\n';
 }
 fs.writeFileSync(path.join(dir,assembly+'.rsp'),rsp);
}
