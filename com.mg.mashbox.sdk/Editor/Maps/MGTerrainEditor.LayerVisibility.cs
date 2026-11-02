#if UNITY_EDITOR
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        void DrawLayerVisibilityButton(Rect rect)
        {
            if (MGTerrainToolbarIcons.Button(rect, MGTerrainToolbarIcons.Icon.Eye, "Layer Visibility", EditorStyles.miniButton))
            {
                PopupWindow.Show(rect, new DetailLayerVisibilityPopup(this));
                GUIUtility.ExitGUI();
            }
        }

        sealed class DetailLayerVisibilityPopup : PopupWindowContent
        {
            readonly MGTerrainEditor m_Owner;
            Vector2 m_Scroll;
            internal DetailLayerVisibilityPopup(MGTerrainEditor owner) { m_Owner = owner; }

            public override Vector2 GetWindowSize()
            {
                int count = m_Owner != null && m_Owner.target is MGTerrain terrain ? terrain.DensityDetailLayerCount : 0;
                return new Vector2(520, Mathf.Clamp(48 + count * 24, 90, 360));
            }

            public override void OnGUI(Rect rect)
            {
                if (m_Owner == null || !(m_Owner.target is MGTerrain terrain)) { editorWindow.Close(); return; }
                GUILayout.Space(6);
                EditorGUILayout.LabelField("Layer Visibility", EditorStyles.boldLabel);
                bool previous = m_Owner.m_WorldDetailControls;
                bool shared = terrain.World != null;
                if (shared) m_Owner.PrepareSharedWorldDetails(terrain);
                m_Owner.serializedObject.Update();
                m_Owner.m_WorldDetailControls = shared;
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
                try
                {
                    if (terrain.DensityDetailLayerCount == 0)
                        EditorGUILayout.LabelField("No detail layers.", EditorStyles.miniLabel);
                    else m_Owner.DrawDetailLayerVisibility(terrain, true);
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                    m_Owner.serializedObject.ApplyModifiedProperties();
                    if (shared) m_Owner.CommitSharedWorldDetails(terrain);
                    m_Owner.m_WorldDetailControls = previous;
                    m_Owner.Repaint();
                }
            }
        }
    }
}
#endif
