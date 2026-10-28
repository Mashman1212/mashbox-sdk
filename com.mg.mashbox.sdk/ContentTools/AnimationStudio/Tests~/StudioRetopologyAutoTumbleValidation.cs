using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioRetopologyAutoTumbleValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static string Report=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/retopology-auto-tumble-validation.txt"));
        public static void Run()
        {
            var log=new List<string>();StudioRetopologyDocument doc=null;StudioRetopologyEditor editor=null;SceneView preview=null;Mesh reference=null;
            void Check(bool yes,string name){if(!yes)throw new Exception(name);log.Add("PASS "+name);}
            object Call(string name,params object[] args)=>typeof(StudioRetopologyEditor).GetMethod(name,Flags).Invoke(editor,args);
            void Set(string name,object value)=>typeof(StudioRetopologyEditor).GetField(name,Flags).SetValue(editor,value);
            object Get(string name)=>typeof(StudioRetopologyEditor).GetField(name,Flags).GetValue(editor);
            try
            {
                foreach(var normal in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right,Vector3.up,Vector3.down,new Vector3(.4f,.8f,.3f).normalized})
                {
                    var rotation=StudioRetopologyEditor.AutoTumbleTargetRotation(normal,Quaternion.identity);
                    Check(Quaternion.Angle(rotation,Quaternion.identity)<=12.01f,"A single hover cannot swing to the full surface normal "+normal);
                    Check(Mathf.Abs((rotation*Vector3.right).y)<.00001f,"Target stays upright "+normal);
                }
                Check(!StudioRetopologyEditor.ShouldRetargetAutoTumble(new Vector2(8,5),Vector2.zero,true),"Small cursor corrections preserve the world-space anchor");
                Check(StudioRetopologyEditor.ShouldRetargetAutoTumble(new Vector2(10,0),Vector2.zero,true),"Deliberate cursor travel can select another anchor");
                Check(StudioRetopologyEditor.ShouldRetargetAutoTumble(Vector2.zero,Vector2.zero,false),"The first hover acquires an anchor");
                var pending=new Vector2(80,-35);var travel=Vector2.zero;
                var firstAim=StudioRetopologyEditor.SmoothAutoTumbleInput(ref pending,1f/120);
                Check(firstAim.magnitude>0&&firstAim.magnitude<new Vector2(80,-35).magnitude,"Reticle input starts smoothly without a hard pixel threshold");
                travel+=firstAim;
                for(int i=0;i<120;i++)travel+=StudioRetopologyEditor.SmoothAutoTumbleInput(ref pending,1f/120);
                Check(Vector2.Distance(travel,new Vector2(80,-35))<.0001f&&pending==Vector2.zero,"Reticle smoothing preserves exact travel and settles without drift");
                pending=Vector2.zero;
                Check(StudioRetopologyEditor.SmoothAutoTumbleInput(ref pending,1f/60)==Vector2.zero,"Camera-only repaint cannot move the virtual cursor");
                Check(!StudioRetopologyEditor.AutoTumbleEnabled(false,false),"Toolbar off stays off without Space");
                Check(StudioRetopologyEditor.AutoTumbleEnabled(false,true),"Holding Space temporarily enables a disabled toolbar setting");
                Check(!StudioRetopologyEditor.AutoTumbleEnabled(true,true),"Holding Space temporarily disables an enabled toolbar setting");
                Check(StudioRetopologyEditor.AutoTumbleEnabled(true,false),"Releasing Space restores the enabled toolbar setting");
                var tube=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                try
                {
                    Object.DestroyImmediate(tube.GetComponent<Collider>());
                    var tubeCollider=tube.AddComponent<MeshCollider>();tubeCollider.sharedMesh=tube.GetComponent<MeshFilter>().sharedMesh;
                    Physics.SyncTransforms();
                    foreach(float side in new[]{-1f,1f})
                    {
                        var p=new Vector3(side*.5f,0,0);var n=new Vector3(side,0,0);
                        bool walked=true;
                        for(int i=0;i<10;i++)
                        {
                            if(!StudioRetopologyEditor.WalkAutoTumbleSurface(tubeCollider,p,n,new Vector3(side*.015f,0,0),Vector3.forward,out var nextHit))
                            {walked=false;break;}
                            walked&=nextHit.point.z>p.z&&Vector3.Distance(nextHit.point,p)<.04f;
                            p=nextHit.point;n=nextHit.normal;
                        }
                        Check(walked,"Directional surface probes advance locally around a cylindrical silhouette, side="+side);
                    }
                    Check(!StudioRetopologyEditor.WalkAutoTumbleSurface(tubeCollider,new Vector3(3,0,0),Vector3.right,Vector3.right*.01f,Vector3.forward,out _),"Surface following cannot jump across empty space to a distant surface");
                    Check(!StudioRetopologyEditor.WalkAutoTumbleSurface(tubeCollider,new Vector3(.5f,0,0),Vector3.right,Vector3.zero,Vector3.forward,out _),"No pointer movement means no automatic surface walking");
                }
                finally{Object.DestroyImmediate(tube);}
                bool gates=true;
                for(int bits=0;bits<32;bits++)
                {
                    bool enabled=(bits&1)!=0,onCanvas=(bits&2)!=0,busy=(bits&4)!=0,manual=(bits&8)!=0,active=(bits&16)!=0;
                    gates&=StudioRetopologyEditor.CanAutoTumble(enabled,onCanvas,busy,manual,active)==(enabled&&onCanvas&&!busy&&!manual&&active);
                }
                Check(gates,"Tools, manual navigation and inactive windows suspend automatic movement");
                var anchor=new Vector3(2,1,3);var initialPivot=new Vector3(-1,.5f,1);var initialRotation=Quaternion.Euler(23,44,0);
                bool anchored=true;
                foreach(var next in new[]{Quaternion.Euler(40,80,0),Quaternion.Euler(-30,-50,0),Quaternion.Euler(80,179,0)})
                {
                    var orbitMotion=new StudioRetopologyEditor.AutoTumbleMotion();
                    // A negligible pan speed isolates orbit in the actual motion
                    // integrator, so a regression to the old pivot is detected.
                    StudioRetopologyEditor.StepAutoTumble(initialPivot,initialRotation,anchor,next,.000001f,.05f,ref orbitMotion,out var orbitPivot,out var orbitRotation);
                    var before=Quaternion.Inverse(initialRotation)*(anchor-initialPivot);
                    var after=Quaternion.Inverse(orbitRotation)*(anchor-orbitPivot);
                    anchored&=Vector3.Distance(before,after)<.00001f;
                }
                Check(anchored,"Orbit alone preserves the picked point's camera coordinates in perspective and orthographic views");
                var facing=StudioRetopologyEditor.AutoTumbleTargetRotation(Vector3.left,Quaternion.identity);
                var motion=new StudioRetopologyEditor.AutoTumbleMotion();
                StudioRetopologyEditor.StepAutoTumble(Vector3.zero,Quaternion.identity,anchor,facing,2.5f,1f/60,ref motion,out var first,out var firstRotation);
                Check(Quaternion.Angle(firstRotation,Quaternion.identity)<.25f,"Orbit accelerates gently from rest");
                Check(motion.panVelocity.sqrMagnitude>0&&motion.orbitVelocity.sqrMagnitude>0,"Smoothing retains pan and orbit velocities between frames");
                var firstOffset=Quaternion.Inverse(firstRotation)*(first-anchor);
                Check(Vector3.Distance(firstOffset,-anchor)<2.5f*.35f/60,"The surface point starts centering gradually without an orbit-induced jump");
                var normalSpeed=new StudioRetopologyEditor.AutoTumbleMotion();var fastPan=normalSpeed;var fastOrbit=normalSpeed;
                StudioRetopologyEditor.StepAutoTumble(Vector3.zero,Quaternion.identity,anchor,facing,2.5f,1f/60,ref normalSpeed,out var normalP,out var normalR,1,1);
                StudioRetopologyEditor.StepAutoTumble(Vector3.zero,Quaternion.identity,anchor,facing,2.5f,1f/60,ref fastPan,out var panP,out var panR,1,1.5f);
                StudioRetopologyEditor.StepAutoTumble(Vector3.zero,Quaternion.identity,anchor,facing,2.5f,1f/60,ref fastOrbit,out var orbitP,out var orbitR,2,1);
                Check(Vector3.Distance(panP,anchor)<Vector3.Distance(normalP,anchor),"The new 1.5x pan default centers faster");
                Check(Quaternion.Angle(panR,normalR)<.001f,"Pan speed does not change orbit speed");
                Check(Quaternion.Angle(orbitR,facing)<Quaternion.Angle(normalR,facing),"Orbit speed independently accelerates rotation");
                Check(Mathf.Abs(Vector3.Distance(orbitP,anchor)-Vector3.Distance(normalP,anchor))<.00001f,"Orbit speed does not change pan progress");
                var frozenMotion=motion;
                StudioRetopologyEditor.StepAutoTumble(first,firstRotation,anchor,facing,2.5f,0,ref frozenMotion,out var stopped,out var stoppedRotation);
                Check(stopped==first&&Quaternion.Angle(stoppedRotation,firstRotation)<.001f,"Zero elapsed time preserves the camera");
                motion=default;
                StudioRetopologyEditor.StepAutoTumble(Vector3.zero,Quaternion.identity,anchor,facing,2.5f,20,ref motion,out var resumed,out var resumedRotation);
                Check(Vector3.Distance(Quaternion.Inverse(resumedRotation)*(resumed-anchor),-anchor)<.05f&&Quaternion.Angle(Quaternion.identity,resumedRotation)<1,"An editor stall cannot jump the point or camera");
                bool level=true;Vector3 p30=Vector3.zero,p120=Vector3.zero;Quaternion r30=Quaternion.Euler(0,0,43),r120=r30;
                var motion30=new StudioRetopologyEditor.AutoTumbleMotion();var motion120=motion30;
                for(int i=0;i<480;i++)
                {
                    StudioRetopologyEditor.StepAutoTumble(p30,r30,anchor,facing,2.5f,1f/30,ref motion30,out p30,out r30);
                    level&=Mathf.Abs((r30*Vector3.right).y)<.00001f;
                }
                for(int i=0;i<1920;i++)StudioRetopologyEditor.StepAutoTumble(p120,r120,anchor,facing,2.5f,1f/120,ref motion120,out p120,out r120);
                Check(level,"Every animation frame stays level, including recovery from a rolled view");
                Check(Vector3.Distance(p30,p120)<.001f&&Quaternion.Angle(r30,r120)<.1f,"Damped motion is consistent at different editor frame rates");
                Check(Vector3.Distance(p30,anchor)<.001f&&Quaternion.Angle(r30,facing)<.1f,"A fixed hover settles at the picked point without continuing to tumble");
                var seamStart=Quaternion.Euler(20,179,0);var seamEnd=Quaternion.Euler(25,-179,0);motion=default;
                StudioRetopologyEditor.StepAutoTumble(Vector3.zero,seamStart,Vector3.zero,seamEnd,2.5f,1f/60,ref motion,out _,out var seamStep);
                Check(Quaternion.Angle(seamStart,seamStep)<.1f&&Quaternion.Angle(seamStep,seamEnd)<Quaternion.Angle(seamStart,seamEnd),"Yaw takes the short path across the 180-degree boundary");
                preview=ScriptableObject.CreateInstance<SceneView>();
                foreach(bool orthoZoom in new[]{false,true})
                {
                    preview.LookAt(initialPivot,initialRotation,2.5f,orthoZoom,true);
                    var relative=(preview.pivot-anchor)/preview.size;
                    foreach(float wheel in new[]{3f,-6f,3f})
                    {
                        StudioRetopologyEditor.ZoomAutoTumbleView(preview,anchor,wheel);
                        Check(Vector3.Distance((preview.pivot-anchor)/preview.size,relative)<.00001f,
                            "Wheel zoom preserves off-center reticle projection, orthographic="+orthoZoom+", wheel="+wheel);
                        Check(Quaternion.Angle(preview.rotation,initialRotation)<.001f&&preview.orthographic==orthoZoom,
                            "Wheel zoom cannot orbit or change projection, orthographic="+orthoZoom+", wheel="+wheel);
                    }
                    Check(Mathf.Abs(preview.size-2.5f)<.00001f,"Opposite wheel travel restores original zoom, orthographic="+orthoZoom);
                }
                foreach(bool ortho in new[]{false,true})
                {
                    preview.LookAt(Vector3.zero,Quaternion.identity,2.5f,ortho,true);float size=preview.size;motion=default;
                    bool settled=false;
                    for(int i=0;i<1200&&!settled;i++)settled=StudioRetopologyEditor.AdvanceAutoTumbleView(preview,anchor,facing,1f/60,.00001f,ref motion);
                    Check(settled&&Vector3.Distance(preview.pivot,anchor)<.000001f&&Quaternion.Angle(preview.rotation,facing)<.01f,"The picked point reaches the exact view center, orthographic="+ortho);
                    Check(Mathf.Abs(preview.size-size)<.00001f&&preview.orthographic==ortho,"Targeted tumble preserves zoom and projection, orthographic="+ortho);
                }
                reference=new Mesh();reference.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};reference.triangles=new[]{0,1,2,0,2,3};
                reference.normals=new[]{Vector3.forward,new Vector3(.3f,0,1).normalized,new Vector3(0,.4f,1).normalized,new Vector3(-.3f,0,1).normalized};reference.RecalculateBounds();
                doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();doc.selected=0;doc.parts.Add(new StudioRetopologyDocument.Part{name="Auto tumble validation",reference=reference});
                editor=new StudioRetopologyEditor(doc,()=>{});Set("view",preview);preview.camera.transform.position=new Vector3(0,0,2);
                var references=(List<GameObject>)Get("references");var collider=references[0].GetComponent<MeshCollider>();
                Check(collider.Raycast(new Ray(new Vector3(.25f,.1f,2),Vector3.back),out var hit,10),"Reference surface can be sampled without modifying its geometry");
                var indices=reference.triangles;var normals=reference.normals;int t=hit.triangleIndex*3;var bary=hit.barycentricCoordinate;
                var expected=(normals[indices[t]]*bary.x+normals[indices[t+1]]*bary.y+normals[indices[t+2]]*bary.z).normalized;
                Check(Vector3.Angle((Vector3)Call("AutoTumbleNormal",hit),expected)<.01f,"Interpolated mesh normals smooth out triangle-by-triangle camera jitter");
                reference.normals=Array.Empty<Vector3>();Set("autoTumbleNormalMesh",null);
                Check(Vector3.Angle((Vector3)Call("AutoTumbleNormal",hit),hit.normal)<.01f,"Meshes without vertex normals use the collider surface normal");
                Set("autoTumbleSpaceHeld",true);Set("autoTumbleAimDelta",Vector2.one);
                Set("autoTumbleTarget",true);Call("StopAutoTumble");Check(!(bool)Get("autoTumbleSpaceHeld")&&(Vector2)Get("autoTumbleAimDelta")==Vector2.zero,"Stopping releases hold mode and relative aim state");Check(!(bool)Get("autoTumbleTarget")&&Get("autoTumbleView")==null,"Stopping clears the latched navigation target");
                Set("autoTumbleTarget",true);editor.SuspendLighting();Check(!(bool)Get("autoTumbleTarget"),"Leaving Retopology cancels automatic camera motion");
                Set("autoTumbleTarget",true);Call("TickAutoTumble");Check(!(bool)Get("autoTumbleTarget"),"A missing or changed viewport cancels automatic motion");
                Check(doc.parts[0].vertices.Count==0&&doc.parts[0].faces.Count==0&&!doc.dirty,"Navigation leaves topology and workspace dirty state unchanged");
                var update=typeof(EditorApplication).GetField("update",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
                bool Registered()=>((Delegate)update.GetValue(null)).GetInvocationList().Any(d=>ReferenceEquals(d.Target,editor)&&d.Method.Name=="TickAutoTumble");
                Check(Registered(),"Editor registers its camera interpolation update");editor.Dispose();
                Check(!Registered(),"Disposal removes the camera update callback");editor=null;
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally{editor?.Dispose();if(preview)Object.DestroyImmediate(preview);if(doc){Undo.ClearUndo(doc);Object.DestroyImmediate(doc);}if(reference)Object.DestroyImmediate(reference);File.WriteAllLines(Report,log);}
        }
    }
}
