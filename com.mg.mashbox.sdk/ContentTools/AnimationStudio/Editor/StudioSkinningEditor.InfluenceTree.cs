using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private List<int>[] influenceChildren;
        private readonly List<int> influenceRoots = new List<int>();
        private int[] influenceParents;
        private bool[] influenceExpanded, influenceMatches;
        private int lastTreeSelection = -1;
        private void EnsureInfluenceTree()
        {
            if (influenceChildren != null) return;
            influenceChildren = new List<int>[bones.Length]; influenceParents = new int[bones.Length];
            influenceExpanded = new bool[bones.Length]; influenceMatches = new bool[bones.Length];
            var indices = new Dictionary<Transform,int>();
            for (int i=0;i<bones.Length;i++) { indices[bones[i]]=i; influenceChildren[i]=new List<int>(); influenceParents[i]=-1; }
            for (int i=0;i<bones.Length;i++)
            {
                if (!deformBones[i]) continue;
                var ancestor = bones[i].parent;
                while (ancestor)
                {
                    if (indices.TryGetValue(ancestor,out int parent) && deformBones[parent]) { influenceParents[i]=parent; break; }
                    ancestor=ancestor.parent;
                }
                if (influenceParents[i]<0) influenceRoots.Add(i); else influenceChildren[influenceParents[i]].Add(i);
                string name = StudioSkinningDocument.BoneName(bones[i].name);
                influenceExpanded[i] = !(name.EndsWith("UpLeg",StringComparison.OrdinalIgnoreCase) || name.EndsWith("Shoulder",StringComparison.OrdinalIgnoreCase) || name.EndsWith("Hand",StringComparison.OrdinalIgnoreCase));
            }
        }
        private void DrawInfluenceTree()
        {
            EnsureInfluenceTree();
            if (lastTreeSelection != boneIndex)
            {
                for (int parent=influenceParents[boneIndex];parent>=0;parent=influenceParents[parent]) influenceExpanded[parent]=true;
                lastTreeSelection=boneIndex;
            }
            bool filtering=!string.IsNullOrWhiteSpace(search);
            Array.Clear(influenceMatches,0,influenceMatches.Length);
            if (filtering)
                for (int i=0;i<bones.Length;i++)
                    if (deformBones[i] && bones[i].name.IndexOf(search.Trim(),StringComparison.OrdinalIgnoreCase)>=0)
                        for (int ancestor=i;ancestor>=0;ancestor=influenceParents[ancestor]) influenceMatches[ancestor]=true;
            foreach (int root in influenceRoots) DrawInfluenceBranch(root,0,filtering);
        }
        private void DrawInfluenceBranch(int index,int depth,bool filtering)
        {
            if (filtering && !influenceMatches[index]) return;
            var row=GUILayoutUtility.GetRect(0,22,GUILayout.ExpandWidth(true));
            bool selected=index==boneIndex;
            if (Event.current.type==EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(row,new Color(.23f,.36f,.33f));
                else if (row.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(row,new Color(.16f,.18f,.18f));
            }
            float x=row.x+4+depth*12;
            var arrow=new Rect(x,row.y+2,16,18);
            bool hasChildren=influenceChildren[index].Count>0;
            if (hasChildren)
            {
                bool expanded=EditorGUI.Foldout(arrow,filtering || influenceExpanded[index],GUIContent.none,false);
                if (!filtering) influenceExpanded[index]=expanded;
            }
            var nameRect=new Rect(x+16,row.y,Mathf.Max(0,row.xMax-x-44),22);
            string name=StudioSkinningDocument.BoneName(bones[index].name);
            if (GUI.Button(nameRect,new GUIContent(name,"Select " + name),StudioTheme.Styles.label) && !selected)
            { FinishPoseGesture(); boneIndex=index; UpdateDisplay(); }
            var lockRect=new Rect(row.xMax-24,row.y+1,22,20);
            var previous=GUI.color;
            if (!locked[index]) GUI.color=new Color(previous.r,previous.g,previous.b,previous.a*.35f);
            bool toggle=GUI.Button(lockRect,new GUIContent(EditorGUIUtility.IconContent("LockIcon-On").image,locked[index] ? "Unlock influence for painting" : "Lock influence while painting"),GUIStyle.none);
            GUI.color=previous;
            if (toggle) { locked[index]=!locked[index]; repaint(); }
            if (hasChildren && (filtering || influenceExpanded[index]))
                foreach (int child in influenceChildren[index]) DrawInfluenceBranch(child,depth+1,filtering);
        }
    }
}
