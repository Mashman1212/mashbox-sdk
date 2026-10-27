using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // A skin's bind pose and its imported display pose are independent. Unity's
    // FBX exporter writes the bind pose onto the joint nodes; restore the body
    // reference's display pose on import without touching mesh binding data.
    internal sealed class StudioStitchedEditorPose : AssetPostprocessor
    {
        private const string Prefix="MashBoxStitchedEditorPose:";
        internal static bool HasPreservedBinding(Mesh mesh)
        {
            var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
            return importer && (importer.userData??"").Split('\n').Any(line=>line.StartsWith(Prefix,StringComparison.Ordinal));
        }
        [Serializable] private sealed class Pose { public Bone[] bones; }
        [Serializable] private sealed class Bone
        {
            public string name;
            public Vector3 position,scale;
            public Quaternion rotation;
        }
        internal static bool Configure(ModelImporter importer,GameObject body)
        {
            string previous=importer.userData??"";
            var lines=previous.Split('\n').Where(line=>!line.StartsWith(Prefix,StringComparison.Ordinal)).ToList();
            if(body)
            {
                var pose=new Pose { bones=body.GetComponentsInChildren<Transform>(true)
                    .Where(b=>b.name.Contains("mixamorig:"))
                    .Select(b=> {
                        var m=body.transform.worldToLocalMatrix*b.localToWorldMatrix;
                        return new Bone{name=b.name,position=m.GetColumn(3),rotation=m.rotation,scale=m.lossyScale};
                    }).ToArray() };
                if(pose.bones.Length==0)throw new InvalidOperationException("The stitch body has no humanoid bones for the exported editor pose.");
                lines.Add(Prefix+JsonUtility.ToJson(pose));
            }
            string next=string.Join("\n",lines);
            if(next==previous)return false;
            importer.userData=next;return true;
        }
        private void OnPostprocessModel(GameObject root)
        {
            string data=assetImporter.userData;
            if(string.IsNullOrEmpty(data)||!data.Contains(Prefix))return;
            var line=data.Split('\n').LastOrDefault(l=>l.StartsWith(Prefix,StringComparison.Ordinal));
            if(line==null)return;
            var pose=JsonUtility.FromJson<Pose>(line.Substring(Prefix.Length));
            if(pose?.bones==null)return;
            var byName=pose.bones.ToDictionary(b=>b.name);
            // Parent-first traversal also handles wrappers on either imported rig.
            foreach(var bone in root.GetComponentsInChildren<Transform>(true))
            {
                if(bone==root.transform||!byName.TryGetValue(bone.name,out var target))continue;
                var world=root.transform.localToWorldMatrix*Matrix4x4.TRS(target.position,target.rotation,target.scale);
                var local=bone.parent.worldToLocalMatrix*world;
                bone.localPosition=local.GetColumn(3);bone.localRotation=local.rotation;bone.localScale=local.lossyScale;
            }
        }
    }
}
