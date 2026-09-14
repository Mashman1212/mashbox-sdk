using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private StudioActorPreset actorPreset;
        [SerializeField] private GameObject vehicleLoadout;
        [SerializeField] private int selectedVehiclePart;
        private string vehicleOutfitNotice;
        private AnimationTake OutfitActor => activeActor>=0 && activeActor<actorPreviews.Count ? actorPreviews[activeActor].Take : take;
        private AuthoringRig OutfitRig => activeActor>=0 && activeActor<actorPreviews.Count ? actorPreviews[activeActor].Rig : rig;
        private void SaveOutfitActor()
        {
            if(pendingSave && pendingSave!=OutfitActor) FlushSave();
            EditorUtility.SetDirty(OutfitActor); pendingSave=OutfitActor; saveAfter=EditorApplication.timeSinceStartup+1;
        }
        private void DrawActorPresets()
        {
            actorPreset=(StudioActorPreset)EditorGUILayout.ObjectField("Actor preset",actorPreset,typeof(StudioActorPreset),false);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Save selected actor preset…")) Guard(SaveActorPreset);
                using(new EditorGUI.DisabledScope(!actorPreset))
                    if(GUILayout.Button("Add from preset")) Guard(()=>AddActorPreset(actorPreset));
            }
        }
        private void SaveActorPreset()
        {
            string path=EditorUtility.SaveFilePanelInProject("Save actor / vehicle preset",OutfitActor.name+" Preset","asset","Store the actor source, outfit, vehicle parts and handle layout.");
            if(string.IsNullOrEmpty(path)) return;
            var p=CreateInstance<StudioActorPreset>(); var a=OutfitActor;
            p.character=a.character; p.body=a.body; p.clothing=a.clothing; p.outfitOptions=a.outfitOptions;
            p.vehicleParts=a.vehicleParts; p.handleOffsets=a.handleOffsets;
            var independent=Instantiate(p); DestroyImmediate(p);
            AssetDatabase.CreateAsset(independent,AssetDatabase.GenerateUniqueAssetPath(path)); AssetDatabase.SaveAssetIfDirty(independent);
            actorPreset=independent; EditorGUIUtility.PingObject(independent);
        }
        private void AddActorPreset(StudioActorPreset preset)
        {
            if(!preset || !preset.character) throw new InvalidOperationException("Preset is missing its actor source.");
            AddActor(preset.character);
            var a=OutfitActor; var copy=Instantiate(preset);
            Undo.RecordObject(a,"Apply actor preset");
            a.body=copy.body; a.clothing=copy.clothing; a.outfitOptions=copy.outfitOptions;
            a.vehicleParts=copy.vehicleParts; a.handleOffsets=copy.handleOffsets; DestroyImmediate(copy);
            SaveOutfitActor(); ReleaseActors(); EnsureActors(); EvaluateConstraints(); viewport?.Repaint();
        }
        private static string AnchorKey(string text) => new string(text.ToLowerInvariant().Replace("anchor","").Where(char.IsLetterOrDigit).ToArray());
        private static string SlotJoint(Transform slot)
        {
            for(var t=slot;t;t=t.parent)
            {
                string n=AnchorKey(t.name);
                if(n.Contains("rightpedal")) return "RightPedal_Joint";
                if(n.Contains("leftpedal")) return "LeftPedal_Joint";
                if(n.Contains("backwheel") || n.Contains("rearwheel")) return "BackWheel_Joint";
                if(n.Contains("frontwheel")) return "FrontWheel_Joint";
                if(n.Contains("headset") || n.Contains("fork") || n.Contains("bars")) return "Bars_Joint";
                if(n.Contains("crank") || n=="bbvisuals" || n.Contains("sprocket")) return "DriveTrain_Joint";
            }
            return "Frame_Joint";
        }
        private void ReadVehicleSlots(GameObject source)
        {
            if(!source) throw new InvalidOperationException("Choose a vehicle prefab containing equip slots.");
            var entries=new List<(StudioVehiclePart part,string anchor,Vector3 snapPosition,Vector3 snapRotation)>();
            foreach(var component in source.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(!component) continue;
                using(var so=new SerializedObject(component))
                {
                    var prefix=so.FindProperty("_slotPrefix");
                    if(prefix==null || component.transform.childCount==0) continue;
                    var child=component.transform.GetChild(0).gameObject;
                    if(!child.GetComponentInChildren<Renderer>(true)) continue;
                    string anchor="";
                    Vector3 snapPosition=Vector3.zero, snapRotation=Vector3.zero;
                    foreach(var m in component.GetComponents<MonoBehaviour>())
                    {
                        if(!m) continue;
                        using(var anchorData=new SerializedObject(m))
                        {
                            var root=anchorData.FindProperty("_rootingPrefix"); var direction=anchorData.FindProperty("_directionID");
                            if(root!=null) anchor=AnchorKey((direction?.stringValue ?? "")+root.stringValue);
                            bool Flag(string n)=>anchorData.FindProperty(n)?.boolValue ?? false;
                            float Value(string n)=>anchorData.FindProperty(n)?.floatValue ?? 0;
                            if(root!=null)
                            {
                                snapPosition.z=Value("_forwardOffset");
                                snapRotation=new Vector3(Flag("_pitch180")?180:0,Flag("_yaw180")?180:0,(Flag("_roll180")?180:0)+Value("_rollOffset"));
                            }
                        }
                    }
                    // Preserve the prefab's authored default child, including its anchor hierarchy.
                    var partSource=PrefabUtility.GetCorrespondingObjectFromSource(child) ?? child;
                    var part=new StudioVehiclePart { source=partSource,joint=SlotJoint(component.transform),
                        position=source.transform.InverseTransformPoint(child.transform.position),
                        rotation=(Quaternion.Inverse(source.transform.rotation)*child.transform.rotation).eulerAngles };
                    entries.Add((part,anchor,snapPosition,snapRotation));
                }
            }
            if(entries.Count==0) throw new InvalidOperationException("No equipped default parts found under this prefab's vehicle slots.");
            // Frame and fork anchors must exist before parts that mount to them.
            entries=entries.OrderBy(e=>e.part.source.name.IndexOf("Frame",StringComparison.OrdinalIgnoreCase)>=0 ? 0 :
                e.part.source.name.IndexOf("Fork",StringComparison.OrdinalIgnoreCase)>=0 ? 1 : 2).ToList();
            for(int i=0;i<entries.Count;i++)
            {
                if(entries[i].anchor.Length==0) continue;
                for(int p=0;p<i;p++)
                {
                    var root=entries[p].part.source.transform;
                    var anchor=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.IndexOf("anchor",StringComparison.OrdinalIgnoreCase)>=0 && AnchorKey(t.name)==entries[i].anchor);
                    if(!anchor) continue;
                    entries[i].part.anchorPart=p; entries[i].part.anchor=AnimationUtility.CalculateTransformPath(anchor,root);
                    entries[i].part.position=entries[i].snapPosition; entries[i].part.rotation=entries[i].snapRotation; break;
                }
            }
            Undo.RecordObject(OutfitActor,"Read vehicle outfit"); OutfitActor.vehicleParts=entries.Select(e=>e.part).ToList();
            int mounts=entries.Count(e=>e.part.anchorPart>=0);
            vehicleOutfitNotice=$"Read {entries.Count} parts; {mounts} named anchor mounts. Other parts retain their prefab rest placement. Inspect the mounts when changing frame geometry.";
            SaveOutfitActor(); ApplyVehicleOutfit();
        }
        private void ApplyVehicleOutfit()
        {
            OutfitRig.BuildVehicleParts(OutfitActor.vehicleParts); OutfitRig.SetNeutralShading(neutralShading);
            EvaluateConstraints(); viewport?.Repaint();
        }
        private void DrawVehicleOutfit()
        {
            var a=OutfitActor; var r=OutfitRig;
            EditorGUILayout.LabelField(a.name+" · VEHICLE PARTS",EditorStyles.boldLabel);
            if(!string.IsNullOrEmpty(vehicleOutfitNotice)) EditorGUILayout.HelpBox(vehicleOutfitNotice,MessageType.Info);
            vehicleLoadout=(GameObject)EditorGUILayout.ObjectField("Vehicle prefab",vehicleLoadout,typeof(GameObject),false);
            using(new EditorGUI.DisabledScope(!vehicleLoadout))
                if(GUILayout.Button("Read equipped defaults / anchors")) Guard(()=>ReadVehicleSlots(vehicleLoadout));
            a.vehicleParts=a.vehicleParts ?? new List<StudioVehiclePart>();
            if(GUILayout.Button("Add vehicle part"))
            { Undo.RecordObject(a,"Add vehicle part"); a.vehicleParts.Add(new StudioVehiclePart()); selectedVehiclePart=a.vehicleParts.Count-1; SaveOutfitActor(); }
            if(a.vehicleParts.Count>0)
            {
                selectedVehiclePart=EditorGUILayout.Popup("Part",Mathf.Clamp(selectedVehiclePart,0,a.vehicleParts.Count-1),a.vehicleParts.Select((p,i)=>i+" · "+(p.source ? p.source.name : "Empty part")).ToArray());
                var p=a.vehicleParts[selectedVehiclePart];
                EditorGUI.BeginChangeCheck();
                var source=(GameObject)EditorGUILayout.ObjectField("Part prefab / model",p.source,typeof(GameObject),false);
                string[] joints=r.Bones.Where(b=>b.name.Contains("Joint") || b.name.Contains("PIDBalance")).Select(b=>b.name).Distinct().ToArray();
                if(joints.Length==0) joints=r.Bones.Select(b=>b.name).ToArray();
                int joint=EditorGUILayout.Popup("Driven by joint",Mathf.Max(0,Array.IndexOf(joints,p.joint)),joints);
                string[] parents=new[]{"Model rest / joint"}.Concat(a.vehicleParts.Take(selectedVehiclePart).Select((v,i)=>i+" · "+(v.source ? v.source.name : "Empty"))).ToArray();
                int parent=EditorGUILayout.Popup("Mount on part",Mathf.Clamp(p.anchorPart+1,0,parents.Length-1),parents)-1;
                string anchor=p.anchor;
                if(parent>=0 && a.vehicleParts[parent].source)
                {
                    var root=a.vehicleParts[parent].source.transform;
                    var anchors=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.IndexOf("anchor",StringComparison.OrdinalIgnoreCase)>=0).Select(t=>AnimationUtility.CalculateTransformPath(t,root)).ToArray();
                    if(anchors.Length>0) anchor=anchors[EditorGUILayout.Popup("Mount anchor",Mathf.Max(0,Array.IndexOf(anchors,anchor)),anchors)];
                    else EditorGUILayout.HelpBox("This part has no named anchors.",MessageType.Warning);
                }
                bool bind=EditorGUILayout.Toggle("Preserve model rest",p.bindFromRest);
                Vector3 position=EditorGUILayout.Vector3Field("Position offset",p.position), rotation=EditorGUILayout.Vector3Field("Rotation offset",p.rotation), scale=EditorGUILayout.Vector3Field("Scale",p.scale);
                if(EditorGUI.EndChangeCheck())
                { Undo.RecordObject(a,"Edit vehicle part"); p.source=source; p.joint=joints[joint]; p.anchorPart=parent; p.anchor=anchor; p.bindFromRest=bind; p.position=position; p.rotation=rotation; p.scale=scale; SaveOutfitActor(); }
                if(GUILayout.Button("Remove part"))
                {
                    Undo.RecordObject(a,"Remove vehicle part"); int removed=selectedVehiclePart; a.vehicleParts.RemoveAt(removed);
                    foreach(var item in a.vehicleParts) { if(item.anchorPart==removed) { item.anchorPart=-1; item.anchor=""; } else if(item.anchorPart>removed) item.anchorPart--; }
                    SaveOutfitActor(); Guard(ApplyVehicleOutfit);
                }
            }
            if(GUILayout.Button("Apply vehicle outfit")) Guard(ApplyVehicleOutfit);
            EditorGUILayout.HelpBox("Mount frame parts in model rest space or directly on a joint. Choose a parent part and its anchor for attachments. Mounts are resolved in the rest pose, then follow the chosen joint. Offsets can be adjusted for different frame geometry.",MessageType.None);
            DrawActorPresets();
        }
    }
}
