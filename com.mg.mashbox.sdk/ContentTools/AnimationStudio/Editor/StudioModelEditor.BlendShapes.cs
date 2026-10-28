using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool HasBlendShape => Document && Active>=0 && Active<Document.nodes.Count && Document.nodes[Active].blendShape!=null;
        private int[] SelectedBlendMeshes => Document ? HierarchyRows().Where(i=>objects.Contains(i)&&Document.nodes[i].mesh).ToArray() : new int[0];
        private void CreateSelectedBlendShape()
        {
            int index=Document.CreateBlendShape(SelectedBlendMeshes);
            StopCutTool();mode=ComponentMode.Object;pivot=false;Components.Clear();
            Rebuild();SelectObject(index);Changed();RememberAction(RepeatKind.Blend);
        }
        private void BakeSelectedBlendShape()
        {
            Document.BakeBlendShape(Active);Changed();RememberAction(RepeatKind.Bake);
        }
        private void DrawBlendShapeChannels()
        {
            if(!HasBlendShape)return;
            var data=Document.nodes[Active].blendShape;
            GUILayout.Space(10);GUILayout.Label("BLEND SHAPES",StudioTheme.Styles.boldLabel);
            EditorGUILayout.HelpBox("Sequence follows the original hierarchy order. Master blends between neighbours; individual weights use additive mixing. Inputs are saved snapshots aligned at their origins.",MessageType.None);
            EditorGUI.BeginChangeCheck();
            float master=EditorGUILayout.Slider(new GUIContent("Master Blend","0 = base, 1 = last target. Moving this switches to sequential blending."),data.master,0,1);
            if(EditorGUI.EndChangeCheck())Run(()=>{Document.SetBlendMaster(Active,master);Changed();});
            GUILayout.Label(data.sequential?"Sequential blend":"Individual weights",StudioTheme.Styles.miniLabel);
            float position=data.master*(data.shapes.Length-1);
            int from=Mathf.Min(Mathf.FloorToInt(position),data.shapes.Length-2);
            if(data.sequential) EditorGUILayout.LabelField(data.shapes[from].name+" → "+data.shapes[from+1].name,EditorStyles.wordWrappedMiniLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Use Master"))Run(()=>{Document.SetBlendMaster(Active,data.master);Changed();});
                if(GUILayout.Button("Reset to Base"))Run(()=>{Document.SetBlendMaster(Active,0);Changed();});
            }
            GUILayout.Space(6);EditorGUILayout.LabelField("Base",data.shapes[0].name);
            var weights=data.EffectiveWeights();
            for(int i=0;i<weights.Length;i++)
            {
                GUILayout.Label((i+1)+". "+data.shapes[i+1].name,StudioTheme.Styles.miniLabel);
                EditorGUI.BeginChangeCheck();float next=EditorGUILayout.Slider(weights[i],0,1);
                int shape=i;
                if(EditorGUI.EndChangeCheck())Run(()=>{Document.SetBlendWeight(Active,shape,next);Changed();});
            }
            EditorGUILayout.HelpBox("Positions, UV 0 and surface directions blend together using the base material. FBX exports the current pose. Save Workspace retains the editable controls. Bake Current Pose enables component editing.",MessageType.None);
            if(GUILayout.Button("Bake Current Pose"))Run(BakeSelectedBlendShape);
        }
    }
}
