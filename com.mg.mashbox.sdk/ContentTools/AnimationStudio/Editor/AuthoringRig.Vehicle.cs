using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class AuthoringRig
    {
        private readonly List<GameObject> vehicleVisuals = new List<GameObject>();
        public void BuildVehicleParts(List<StudioVehiclePart> parts)
        {
            foreach(var visual in vehicleVisuals) if(visual) UnityEngine.Object.DestroyImmediate(visual);
            vehicleVisuals.Clear();
            if(parts==null || parts.Count==0) return;
            var saved=Capture();
            try
            {
                Apply(Rest);
                foreach(var part in parts)
                {
                    if(!part.source) { vehicleVisuals.Add(null); continue; }
                    var joint=Bones.FirstOrDefault(b=>b.name.Split(':').Last()==part.joint) ?? Bones.FirstOrDefault(b=>UnityEditor.AnimationUtility.CalculateTransformPath(b,Root.transform)==part.joint);
                    if(!joint) throw new InvalidOperationException("Vehicle part joint not found: "+part.joint);
                    Transform mount=null;
                    if(part.anchorPart>=0)
                    {
                        if(part.anchorPart>=vehicleVisuals.Count || !vehicleVisuals[part.anchorPart]) throw new InvalidOperationException("Mount a part to an earlier, assigned part.");
                        var parent=vehicleVisuals[part.anchorPart].transform;
                        mount=parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==part.anchor || UnityEditor.AnimationUtility.CalculateTransformPath(t,parent)==part.anchor);
                        if(!mount) throw new InvalidOperationException("Mount anchor not found: "+part.anchor);
                    }
                    var copy=new GameObject(part.source.name+" · Preview"); vehicleVisuals.Add(copy);
                    copy.transform.SetParent(mount ? mount : joint,false);
                    var map=new Dictionary<Transform,Transform> { [part.source.transform]=copy.transform };
                    CloneChildren(part.source.transform,copy.transform,map); CopyRenderers(part.source,map);
                    if(!mount && part.bindFromRest)
                    {
                        // Parts are authored in model space; retain that rest placement while the chosen joint drives motion.
                        copy.transform.SetPositionAndRotation(Root.transform.TransformPoint(part.position),Root.transform.rotation*Quaternion.Euler(part.rotation));
                    }
                    else { copy.transform.localPosition=part.position; copy.transform.localRotation=Quaternion.Euler(part.rotation); }
                    if(mount) copy.transform.SetParent(joint,true);
                    copy.transform.localScale=Vector3.Scale(part.source.transform.localScale,part.scale);
                }
            }
            catch
            {
                foreach(var visual in vehicleVisuals) if(visual) UnityEngine.Object.DestroyImmediate(visual);
                vehicleVisuals.Clear(); throw;
            }
            finally { Apply(saved); }
        }
    }
}
