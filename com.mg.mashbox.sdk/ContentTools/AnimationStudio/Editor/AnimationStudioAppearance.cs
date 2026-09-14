using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void DrawClothing()
        {
            clothing = clothing ?? Array.Empty<GameObject>();
            outfitOptions = outfitOptions ?? Array.Empty<OutfitOptions>();
            if (outfitOptions.Length != clothing.Length) Array.Resize(ref outfitOptions, clothing.Length);
            for (int i = 0; i < clothing.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Copy the array before editing: the take may own the original reference.
                    var next = (GameObject)EditorGUILayout.ObjectField("Clothing / mesh " + (i + 1), clothing[i], typeof(GameObject), false);
                    if (next != clothing[i]) { clothing = (GameObject[])clothing.Clone(); clothing[i] = next; }
                    if (GUILayout.Button("Remove", GUILayout.Width(65)))
                    {
                        var list = new System.Collections.Generic.List<GameObject>(clothing);
                        var settings = new System.Collections.Generic.List<OutfitOptions>(outfitOptions);
                        settings.RemoveAt(i); outfitOptions = settings.ToArray();
                        list.RemoveAt(i); clothing = list.ToArray(); break;
                    }
                }
                var option = outfitOptions[i];
                EditorGUI.BeginChangeCheck();
                option.role = (OutfitRole)EditorGUILayout.EnumPopup("Piece type", option.role);
                option.doubleSided = EditorGUILayout.Toggle(new GUIContent("Double-sided", "Render both outside and inside faces in the animation preview."), option.doubleSided);
                if (option.role == OutfitRole.Hat)
                {
                    option.position = EditorGUILayout.Vector3Field("Head offset", option.position);
                    option.rotation = EditorGUILayout.Vector3Field("Head rotation", option.rotation);
                    option.scale = Mathf.Max(0.001f, EditorGUILayout.FloatField("Hat scale", option.scale > 0 ? option.scale : 1));
                }
                else if (option.role == OutfitRole.SkinnedClothing)
                {
                    option.hideBody = EditorGUILayout.Toggle("Cut body underneath", option.hideBody);
                    if (option.hideBody)
                        option.cutoutDistance = EditorGUILayout.Slider(new GUIContent("Coverage distance (m)", "Search distance on either side of the body's surface. Increase carefully to avoid removing exposed skin."), option.cutoutDistance > 0 ? option.cutoutDistance : 0.03f, 0.001f, 0.15f);
                }
                if (EditorGUI.EndChangeCheck())
                { outfitOptions = (OutfitOptions[])outfitOptions.Clone(); outfitOptions[i] = option; }
                EditorGUILayout.Space(4);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add clothing / skinned mesh")) Array.Resize(ref clothing, clothing.Length + 1);
                using (new EditorGUI.DisabledScope(rig == null))
                    if (GUILayout.Button("Apply outfit")) Guard(() =>
                    {
                        RebuildAppearance();
                        if (take)
                        {
                            Undo.RecordObject(take, "Change preview outfit");
                            take.body = body; take.clothing = (GameObject[])clothing.Clone(); take.outfitOptions = (OutfitOptions[])outfitOptions.Clone(); Save();
                        }
                        neutralShading = false;
                        rig.SetNeutralShading(false);
                    });
            }
            EditorGUILayout.LabelField("Drag compatible skinned body, bust or clothing prefabs into these slots, then Apply outfit. Base textures and colors use studio preview shading; pose keys stay unchanged.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Preview body / bust is always Body. Mark extra skin pieces as Body; Hat attaches to Head. Cutouts affect preview body copies only and are recalculated on Apply outfit.", EditorStyles.wordWrappedMiniLabel);
            if (rig != null && rig.HiddenBodyTriangles > 0) EditorGUILayout.LabelField(rig.HiddenBodyTriangles + " body triangles hidden", EditorStyles.miniLabel);
        }

        private void RebuildAppearance()
        {
            // Build first so incompatible garments leave the current preview and pose intact.
            var replacement = new AuthoringRig(character, body, clothing, outfitOptions);
            try
            {
                replacement.SetNeutralShading(neutralShading);
                if (pose != null) replacement.Apply(pose);
            }
            catch { replacement.Dispose(); throw; }
            var previous = rig;
            rig = replacement;
            if (viewport)
            {
                var angle = viewport.rotation; var pivot = viewport.pivot; float size = viewport.size;
                viewport.Bind(rig.Scene);
                viewport.rotation = angle; viewport.pivot = pivot; viewport.size = size;
            }
            previous?.Dispose();
            viewport?.Repaint();
        }
    }
}

