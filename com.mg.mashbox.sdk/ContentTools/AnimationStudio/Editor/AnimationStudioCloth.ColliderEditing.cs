using System;
using System.Linq;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void ValidateClothColliderDefaults(ClothBoneCapsule[] capsules)
        {
            if(capsules==null || capsules.Length==0 || capsules.Length>64)
                throw new InvalidOperationException("Use between 1 and 64 capsules.");
            bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
            bool Position(Vector3 v) => Finite(v.x)&&Finite(v.y)&&Finite(v.z);
            foreach(var c in capsules)
            {
                if(string.IsNullOrEmpty(c.startBone)||string.IsNullOrEmpty(c.endBone)||!clothRig.Root.transform.Find(c.startBone)||!clothRig.Root.transform.Find(c.endBone))
                    throw new InvalidOperationException("Collider defaults do not match this character's bones: "+c.startBone+" / "+c.endBone);
                if(!Position(c.startOffset)||!Position(c.endOffset)||!Finite(c.radius)||!Finite(c.endRadius)||c.radius<.001f||c.endRadius<0)
                    throw new InvalidOperationException("Collider positions and sizes must be finite, with positive radii.");
            }
        }
        private bool ApplySharedClothColliderDefaults()
        {
            var defaults=AssetDatabase.LoadAssetAtPath<StudioClothColliderDefaults>(StudioClothColliderDefaults.AssetPath);
            if(!defaults)return false;
            ValidateClothColliderDefaults(defaults.capsules);
            StopClothSession();
            clothProfile.bodyCapsules=(ClothBoneCapsule[])defaults.capsules.Clone();
            EditorUtility.SetDirty(clothProfile);
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            return true;
        }
#if MashBoxDev
        private bool clothEditingColliders,clothColliderScale;
        private int clothEditingCapsule;
        private void DrawClothColliderDeveloperPanel()
        {
            if(GUILayout.Button(clothEditingColliders?"Close collider editor":"Edit collider defaults (Dev)"))
            {
                clothEditingColliders=!clothEditingColliders;
                if(clothEditingColliders){EndClothStroke();StopClothSession();clothPainting=false;clothTool=0;clothShowColliders=true;}
                viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            }
            if(!clothEditingColliders)return;
            var capsules=clothProfile.bodyCapsules;
            if(capsules==null||capsules.Length==0){EditorGUILayout.HelpBox("Generate character capsules first.",MessageType.Info);return;}
            clothEditingCapsule=Mathf.Clamp(clothEditingCapsule,0,capsules.Length-1);
            clothEditingCapsule=EditorGUILayout.Popup("Collider",clothEditingCapsule,capsules.Select((c,i)=>(i+1)+": "+c.startBone?.Split('/').Last()+" / "+c.endBone?.Split('/').Last()).ToArray());
            clothColliderScale=GUILayout.Toolbar(clothColliderScale?1:0,new[]{"Move endpoints","Scale radii"})==1;
            var capsule=capsules[clothEditingCapsule];
            EditorGUI.BeginChangeCheck();
            capsule.startOffset=EditorGUILayout.Vector3Field("Start position",capsule.startOffset);
            capsule.endOffset=EditorGUILayout.Vector3Field("End position",capsule.endOffset);
            capsule.radius=Mathf.Max(.001f,EditorGUILayout.FloatField("Start radius",capsule.radius));
            capsule.endRadius=Mathf.Max(0,EditorGUILayout.FloatField("End radius (0 = uniform)",capsule.endRadius));
            if(EditorGUI.EndChangeCheck())SetEditedClothCapsule(capsule);
            EditorGUILayout.HelpBox("Positions are local to each endpoint bone. Move or resize using the viewport handles. Save to share this setup with SDK users.",MessageType.None);
            if(GUILayout.Button("Save as SDK collider defaults"))ClothAction(SaveSharedClothColliderDefaults);
        }
        private void SetEditedClothCapsule(ClothBoneCapsule capsule)
        {
            StopClothSession();Undo.RecordObject(clothProfile,"Edit cloth collider");
            clothProfile.bodyCapsules[clothEditingCapsule]=capsule;
            EditorUtility.SetDirty(clothProfile);viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }
        private void SaveSharedClothColliderDefaults()
        {
            ValidateClothColliderDefaults(clothProfile.bodyCapsules);
            var defaults=AssetDatabase.LoadAssetAtPath<StudioClothColliderDefaults>(StudioClothColliderDefaults.AssetPath);
            if(!defaults)
            {
                if(AssetDatabase.LoadMainAssetAtPath(StudioClothColliderDefaults.AssetPath))throw new InvalidOperationException("Another asset occupies the collider defaults path.");
                defaults=ScriptableObject.CreateInstance<StudioClothColliderDefaults>();
                defaults.capsules=(ClothBoneCapsule[])clothProfile.bodyCapsules.Clone();
                AssetDatabase.CreateAsset(defaults,StudioClothColliderDefaults.AssetPath);
            }
            else{Undo.RecordObject(defaults,"Save SDK collider defaults");defaults.capsules=(ClothBoneCapsule[])clothProfile.bodyCapsules.Clone();}
            EditorUtility.SetDirty(defaults);AssetDatabase.SaveAssetIfDirty(defaults);
            clothStatus="Saved SDK collider defaults. Generate character capsules now uses this setup for everyone.";
        }
        private bool DrawClothColliderEditorHandles()
        {
            if(!clothEditingColliders||!clothProfile||clothRig==null)return false;
            var capsules=clothProfile.bodyCapsules;
            if(capsules==null||capsules.Length==0)return true;
            clothEditingCapsule=Mathf.Clamp(clothEditingCapsule,0,capsules.Length-1);
            var capsule=capsules[clothEditingCapsule];
            var a=clothRig.Root.transform.Find(capsule.startBone??"");var b=clothRig.Root.transform.Find(capsule.endBone??"");
            if(!a||!b)return true;
            if(viewport.IsOverViewportControls(Event.current.mousePosition)&&GUIUtility.hotControl==0)return true;
            var shape=capsule.ToWorld(a,b);var color=Handles.color;Handles.color=Color.yellow;
            EditorGUI.BeginChangeCheck();
            if(clothColliderScale)
            {
                float radius=Handles.RadiusHandle(a.rotation,shape.a,shape.radius);
                float endRadius=Handles.RadiusHandle(b.rotation,shape.b,shape.endRadius);
                if(EditorGUI.EndChangeCheck())
                {
                    capsule.radius=Mathf.Max(.001f,radius/Mathf.Max(.0001f,Mathf.Abs(a.lossyScale.x),Mathf.Abs(a.lossyScale.y),Mathf.Abs(a.lossyScale.z)));
                    capsule.endRadius=Mathf.Max(.001f,endRadius/Mathf.Max(.0001f,Mathf.Abs(b.lossyScale.x),Mathf.Abs(b.lossyScale.y),Mathf.Abs(b.lossyScale.z)));
                    SetEditedClothCapsule(capsule);
                }
            }
            else
            {
                var start=StudioUnsnappedHandles.PositionHandle(shape.a,a.rotation);var end=StudioUnsnappedHandles.PositionHandle(shape.b,b.rotation);
                if(EditorGUI.EndChangeCheck()){capsule.startOffset=a.InverseTransformPoint(start);capsule.endOffset=b.InverseTransformPoint(end);SetEditedClothCapsule(capsule);}
            }
            Handles.color=color;return true;
        }
#endif
    }
}
