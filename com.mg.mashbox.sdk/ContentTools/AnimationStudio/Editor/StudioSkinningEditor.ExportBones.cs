using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private Transform[] ResolveExportBones(GameObject root,SkinnedMeshRenderer target,StudioSkinWeights skin,Matrix4x4[] pose)
        {
            var resolved=new Transform[bones.Length];
            var all=root.GetComponentsInChildren<Transform>(true).ToList();
            var original=target.bones;
            Transform Resolve(int index)
            {
                if(index<0 || index>=bones.Length) throw new InvalidOperationException("A skin weight references an invalid SDK bone index.");
                if(resolved[index]) return resolved[index];
                if(bones[index]==skeleton.transform) return resolved[index]=root.transform;
                string name=StudioSkinningDocument.BoneName(bones[index].name);
                var matches=all.Where(t=>t!=root.transform && StudioSkinningDocument.BoneName(t.name)==name).ToArray();
                // Existing renderer references disambiguate duplicate source rig names.
                var referenced=matches.Where(t=>original.Contains(t)).ToArray();
                if(referenced.Length==1) return resolved[index]=referenced[0];
                if(matches.Length==1) return resolved[index]=matches[0];
                int parentIndex=Array.IndexOf(bones,bones[index].parent);
                var parent=parentIndex>=0 ? Resolve(parentIndex) : root.transform;
                if(matches.Length>1)
                {
                    var children=matches.Where(t=>t.parent==parent).ToArray();
                    if(children.Length==1) return resolved[index]=children[0];
                    throw new InvalidOperationException("The source FBX contains ambiguous copies of bone "+bones[index].name+". Could not match its SDK parent safely; no file was changed.");
                }
                // Missing bones and their ancestors belong only to this export copy.
                // Never change the source prefab, working skeleton, or vertex weights.
                var created=new GameObject(bones[index].name).transform;
                created.SetParent(parent,false);
                var local=parent.worldToLocalMatrix*root.transform.localToWorldMatrix*pose[index];
                created.localPosition=local.GetColumn(3); created.localRotation=local.rotation; created.localScale=local.lossyScale;
                all.Add(created); resolved[index]=created; return created;
            }
            foreach(int index in skin.Values.SelectMany(v=>v).Select(w=>w.boneIndex).Distinct()) Resolve(index);
            return resolved;
        }
    }
}
