import pathlib,hashlib
src=pathlib.Path('D:/mashbox-sdk/com.mg.mashbox.sdk/Editor/Maps/MGTerrainEditor.cs');out=pathlib.Path(__file__).resolve().parent
s=src.read_text(encoding='utf-8-sig');(out/'original.sha256').write_text(hashlib.sha256(src.read_bytes()).hexdigest())
s=s.replace('int m_PaintDetailIndex, m_PaintChannel, m_PaintDensity = 32;','int m_PaintDetailIndex, m_PaintChannel;\n        float m_PaintDensity = 32f;')
s=s.replace('m_PaintDensity = EditorGUILayout.IntSlider("Target Density / Texel", m_PaintDensity, 1, 2000);','''m_PaintDensity = EditorGUILayout.FloatField(new GUIContent("Target Density / Texel", "Supports fractional density: 0.1 paints approximately one tree per ten texels. Repeated strokes keep the same sparse pattern."), m_PaintDensity);
                m_PaintDensity = float.IsFinite(m_PaintDensity) ? Mathf.Clamp(m_PaintDensity, 0f, 2000f) : 1f;
                if (m_PaintDensity <= 1f)
                {
                    m_PaintDensity = EditorGUILayout.Slider("Sparse Coverage", m_PaintDensity, 0f, 1f);
                    EditorGUILayout.HelpBox($"{m_PaintDensity:P1} of texels targeted for one instance. Paint over an existing dense area to thin it toward this target. Brush strength controls how quickly it reaches the target.", MessageType.None);
                }''')
marker='        float GetPaintSizeTarget(int x, int z)'
helper='''        internal static int SparseDensityTarget(float density, int x, int z, int seed)
        {
            density = float.IsFinite(density) ? Mathf.Clamp(density, 0f, 2000f) : 0f;
            int whole = Mathf.FloorToInt(density);
            // Stable per-texel thresholds keep overlapping dabs from filling all cells.
            unchecked
            {
                uint hash = (uint)x * 0x9E3779B9u ^ (uint)z * 0x85EBCA6Bu ^ (uint)seed;
                hash ^= hash >> 16; hash *= 0x7FEB352Du;
                hash ^= hash >> 15; hash *= 0x846CA68Bu; hash ^= hash >> 16;
                return whole + ((hash & 0xFFFFFFu) / 16777216f < density - whole ? 1 : 0);
            }
        }

'''
assert marker in s;s=s.replace(marker,helper+marker)
s=s.replace('(erase ? 0 : Mathf.Clamp(m_PaintDensity, 1, 2000))','(erase ? 0 : SparseDensityTarget(m_PaintDensity, x, z, terrain.DensityDetailLayers[m_PaintDetailIndex].Seed))')
(out/'MGTerrainEditor.cs').write_text(s,encoding='utf-8')
