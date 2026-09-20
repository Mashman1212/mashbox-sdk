const fs=require('fs'),path=require('path');
const folder=__dirname;
let core=fs.readFileSync(path.join(folder,'MGTerrain.before.cs'),'utf8');
const marker='        List<RenderPart> GetRenderParts(Prototype prototype, int treeLod = 0)';
if(!core.includes(marker))throw Error('Runtime insertion point missing');
core=core.replace(marker,`        // Inspector diagnostics use the same cached parts and fallback policy as rendering.
        public void GetTreeLodSourceMeshes(int prototypeIndex, int lod, List<Mesh> meshes)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            meshes.Clear();
            if ((uint)prototypeIndex >= m_Prototypes.Count) return;
            var prototype = m_Prototypes[prototypeIndex];
            if (prototype == null || prototype.Kind != InstanceKind.Tree) return;
            int mask = 1 << Mathf.Clamp(lod, 0, prototype.TreeLodCount - 1);
            foreach (var part in GetDenseDetailRenderParts(prototype).parts)
                if ((part.treeLodMask & mask) != 0 && part.mesh != null && !meshes.Contains(part.mesh)) meshes.Add(part.mesh);
        }

`+marker);
fs.writeFileSync(path.join(folder,'MGTerrain.cs'),core);
let editor=fs.readFileSync(path.join(folder,'MGTerrainEditor.Prototypes.before.cs'),'utf8');
editor=editor.replace('        int m_SelectedPrototype;',`        int m_SelectedPrototype;
        readonly System.Collections.Generic.List<Mesh> m_TreeLodSourceMeshes = new System.Collections.Generic.List<Mesh>();

        void DrawDetectedTreeLod(MGTerrain terrain, int lod, string label, SerializedProperty overridePrefab = null)
        {
            bool automatic = overridePrefab == null || overridePrefab.objectReferenceValue == null;
            terrain.GetTreeLodSourceMeshes(m_SelectedPrototype, lod, m_TreeLodSourceMeshes);
            if (m_TreeLodSourceMeshes.Count == 0)
            {
                EditorGUILayout.HelpBox(label + ": no usable mesh found in the prefab.", MessageType.Warning);
                return;
            }
            using (new EditorGUI.DisabledScope(true))
                for (int index = 0; index < m_TreeLodSourceMeshes.Count; index++)
                    EditorGUILayout.ObjectField(index == 0 ? label + (automatic ? " (Automatic)" : " (Override)") : "",
                        m_TreeLodSourceMeshes[index], typeof(Mesh), false);
        }`);
editor=editor.replace('                if (lodCount.intValue > 1)\r\n                {', '                DrawDetectedTreeLod(terrain, 0, "Close Mesh");\r\n                if (lodCount.intValue > 1)\r\n                {');
editor=editor.replace('                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Prefab"), new GUIContent("Medium Prefab Override"));', '                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod1Prefab"), new GUIContent("Medium Prefab Override", "Optional. Leave empty to discover the mesh inside the source prefab automatically."));\n                    DrawDetectedTreeLod(terrain, 1, "Medium Mesh", selected.FindPropertyRelative("m_TreeLod1Prefab"));');
editor=editor.replace('                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Prefab"), new GUIContent("Far Prefab Override"));', '                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("m_TreeLod2Prefab"), new GUIContent("Far Prefab Override", "Optional. Leave empty to discover the mesh inside the source prefab automatically."));\n                    DrawDetectedTreeLod(terrain, 2, "Far Mesh", selected.FindPropertyRelative("m_TreeLod2Prefab"));');
editor=editor.replace("Uses the prefab's LOD0, LOD1 and last mesh LOD. Optional overrides must share its pivot and scale. Missing mesh levels reuse the last available mesh; lower-detail geometry is not generated automatically.","LOD meshes are discovered automatically inside the source prefab, including child LODGroups. Empty override slots use the meshes shown above. With only two mesh levels, Medium and Far reuse the same last level. Overrides must share the source pivot and scale.");
fs.writeFileSync(path.join(folder,'MGTerrainEditor.Prototypes.cs'),editor);
