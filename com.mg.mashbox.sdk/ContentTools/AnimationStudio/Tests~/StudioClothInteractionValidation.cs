using System;
using System.Collections.Generic;
using System.IO;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioClothInteractionValidation
    {
        public static void Run()
        {
            var log = new List<string>();
            Mesh mesh = null; GameObject root = null; MashBoxClothProfile profile = null;
            MashBoxClothSession session = null; MashBoxClothSolver solver = null;
            void Check(bool condition, string name) { if (!condition) throw new Exception(name); log.Add("PASS " + name); }
            try
            {
                var taper = new ClothCapsule(Vector3.zero, Vector3.up, .3f, .1f);
                var probe = new Unity.Mathematics.float3(.1f,.5f,0);
                float depth = taper.Distance(probe,0,out var contactNormal);
                Check(depth < 0 && contactNormal.y > .19f, "Taper contact normal follows the sloping wall");
                Check(Mathf.Abs(taper.Distance(probe-contactNormal*depth,0,out _)) < .00001f, "Taper push-out lands on the surface");
                Check(taper.Distance(new Vector3(.2f,.1f,0),0,out _) < 0 && taper.Distance(new Vector3(.2f,.9f,0),0,out _) > 0, "Taper is wider at the thigh than the knee");
                Check(Mathf.Abs(taper.Distance(new Vector3(0,-.4f,0),0,out _)-.1f)<.00001f && Mathf.Abs(taper.Distance(new Vector3(0,1.2f,0),0,out _)-.1f)<.00001f,"Taper retains both spherical ends");
                var legacy = new ClothCapsule(Vector3.zero,Vector3.up,.2f);
                Check(Mathf.Abs(legacy.Distance(new Vector3(.3f,.5f,0),0,out _)-.1f)<.00001f,"Legacy uniform capsules retain their shape");
                var contained = new ClothCapsule(Vector3.zero,Vector3.up*.1f,.4f,.1f);
                Check(Mathf.Abs(contained.Distance(new Vector3(.5f,0,0),0,out _)-.1f)<.00001f,"Overlapping endpoint spheres handle containment");
                Check(Mathf.Abs(taper.Distance(probe,.02f,out _)-(depth-.02f))<.00001f,"Thickness expands the tapered shape consistently");
                float axisDepth=taper.Distance(new Vector3(0,.5f,0),0,out var axisNormal);
                Check(Mathf.Abs(taper.Distance(new Unity.Mathematics.float3(0,.5f,0)-axisNormal*axisDepth,0,out _))<.00001f,"On-axis contact resolves to the tapered surface");
                var smoothNeighbors = AnimationStudioWindow.BuildClothPaintNeighbors(5,new[]{0,1,2,2,1,3});
                var movement = new[]{0f,0f,.3f,.3f,.7f}; var smoothed = new float[5];
                AnimationStudioWindow.ClothSmoothTargets(movement,smoothNeighbors,smoothed);
                Check(Mathf.Abs(smoothed[0]-.1f)<.00001f && Mathf.Abs(smoothed[3]-.2f)<.00001f,"Smoothing softens movement-distance transitions");
                Check(Mathf.Abs(smoothed[1]-.15f)<.00001f && Mathf.Abs(smoothed[2]-.15f)<.00001f,"Shared triangle edges count each neighbor once");
                Check(smoothed[4]==.7f && movement[0]==0 && movement[2]==.3f,"Smoothing preserves isolated vertices and snapshots the original dab");
                AnimationStudioWindow.ClothSmoothTargets(new[]{.2f,.2f,.2f,.2f,.2f},smoothNeighbors,smoothed);
                Check(Array.TrueForAll(smoothed,v=>Mathf.Abs(v-.2f)<.00001f),"Smoothing preserves uniform painted distances");
                var shader = Shader.Find("Hidden/MashBox/ClothWeightOverlay");
                Check(shader && !ShaderUtil.ShaderHasError(shader), "Cloth vertex-color overlay shader compiles");
                Check(AnimationStudioWindow.ClothPaintFalloff(new Vector3(0,0,1),Vector3.forward,Vector3.forward,.1f,false)==0,"Surface brush excludes rear garment layers");
                Check(AnimationStudioWindow.ClothPaintFalloff(new Vector3(0,0,1),Vector3.forward,Vector3.forward,.1f,true)==1,"Pass-through reaches rear layers at full center strength");
                Check(AnimationStudioWindow.ClothPaintFalloff(new Vector3(.2f,0,1),Vector3.forward,Vector3.forward,.1f,true)==0,"Pass-through respects brush radius");
                Check(Mathf.Abs(AnimationStudioWindow.ClothPaintFalloff(new Vector3(.05f,0,1),Vector3.forward,Vector3.forward,.1f,true)-.5f)<.0001f,"Pass-through preserves radial falloff");
                var vertices = new[] { new Vector3(0,0,0), new Vector3(.1f,0,0), new Vector3(0,.1f,0), new Vector3(.1f,.1f,0) };
                mesh = new Mesh { vertices = vertices, triangles = new[] { 0,2,1,1,2,3 } }; mesh.RecalculateNormals();
                profile = ScriptableObject.CreateInstance<MashBoxClothProfile>(); profile.Initialize(mesh);
                profile.animationInertia = 0; profile.animationFollow = 0; profile.motion = new[] { 0f, 10f, 10f, 10f }; profile.gravity = profile.wind = Vector3.zero;
                profile.stretchCompliance = profile.bendCompliance = 1; profile.substeps = profile.iterations = 1;
                solver = new MashBoxClothSolver(new MashBoxClothTopology(vertices, mesh.triangles, 100));
                solver.SetTargets(new List<Vector3>(vertices), Matrix4x4.identity, profile.motion); solver.Reset(true);
                profile.motion = new[] {10f,10f,10f,10f};
                var shifted = new List<Vector3>();foreach(var v in vertices)shifted.Add(v+Vector3.forward);
                foreach(float follow in new[]{0f,1f})
                {
                    profile.animationFollow=follow;
                    solver.SetTargets(new List<Vector3>(vertices),Matrix4x4.identity,profile.motion);solver.Reset(true);
                    solver.SetTargets(shifted,Matrix4x4.identity,profile.motion);
                    solver.Step(1f/30,0,profile,Array.Empty<ClothCapsule>(),0);
                    Check(follow==0?solver.Displacement(1).magnitude>.9f:solver.Displacement(1).magnitude<.00001f,"Animation follow transports moving bone targets, strength="+follow);
                    if(follow==1){solver.SetTargets(shifted,Matrix4x4.identity,profile.motion);solver.Step(1f/30,0,profile,Array.Empty<ClothCapsule>(),0);Check(solver.Displacement(1).magnitude<.00001f,"Follow transport adds no spurious velocity when the animation stops");}
                }
                profile.animationFollow=0;profile.motion=new[]{0f,10f,10f,10f};
                solver.SetTargets(new List<Vector3>(vertices),Matrix4x4.identity,profile.motion);solver.Reset(true);
                profile.motion=new[]{10f,10f,10f,10f};profile.animationFollow=1;profile.animationInertia=.65f;profile.damping=3;
                solver.SetTargets(new List<Vector3>(vertices),Matrix4x4.identity,profile.motion);solver.Reset(true);
                float lag=0,overshoot=0;
                for(int frame=0;frame<42;frame++)
                {
                    var moving=new List<Vector3>();float z=Mathf.Min(frame+1,12)*.01f;
                    foreach(var v in vertices)moving.Add(v+Vector3.forward*z);
                    solver.SetTargets(moving,Matrix4x4.identity,profile.motion);
                    solver.Step(1f/30,frame/30f,profile,Array.Empty<ClothCapsule>(),0);
                    if(frame<12)lag=Mathf.Min(lag,solver.Displacement(1).z);
                    else overshoot=Mathf.Max(overshoot,solver.Displacement(1).z);
                }
                Check(lag<-.001f,"Animation inertia produces lag during acceleration even at full follow");
                Check(overshoot>.001f,"Cloth retains momentum and overshoots when the animation stops");
                solver.Reset(true);solver.Step(1f/30,0,profile,Array.Empty<ClothCapsule>(),0);
                Check(solver.Displacement(1).magnitude<.00001f,"Reset clears animation momentum history");
                profile.animationFollow=0;profile.animationInertia=0;profile.damping=3;profile.motion=new[]{0f,10f,10f,10f};
                solver.SetTargets(new List<Vector3>(vertices),Matrix4x4.identity,profile.motion);solver.Reset(true);
                int grabbed = solver.BeginGrab(new Vector3(.05f,.05f,0), .2f);
                Check(grabbed == 3, "Volume captures multiple particles and excludes pinned particles");
                solver.GrabPosition += Vector3.forward * .2f;
                solver.Step(1f/60,0,profile,Array.Empty<ClothCapsule>(),0);
                Check(solver.Position(0) == Vector3.zero, "Volume drag preserves pins");
                Check(solver.Position(1).z > .05f && solver.Position(2).z > .05f, "Drag moves the captured group with falloff");
                solver.ReleaseGrab(); Check(solver.GrabbedCount == 0 && solver.GrabbedParticle == -1, "Release clears all grab constraints");
                Check(solver.BeginGrab(new Vector3(100,100,100), .01f) == 0, "Empty volume cannot grab distant particles");
                solver.Reset(); profile.bodyCollisions = false;
                var caps = new[] { new ClothCapsule(new Vector3(0,-1,0),new Vector3(0,1,0),.3f) };
                solver.Step(1f/60,0,profile,caps,1);
                Check(solver.Position(1).x < .2f, "Disabled collisions skip body projection");
                profile.bodyCollisions = true;
                solver.Step(1f/60,0,profile,caps,1);
                Check(solver.Position(1).x >= .3f, "Collision toggle applies to the existing solver on the next step");
                var contactPosition=solver.Position(1);profile.bodyCollisions=false;
                solver.Step(1f/60,0,profile,Array.Empty<ClothCapsule>(),0);
                Check(Vector3.Distance(solver.Position(1),contactPosition)<.01f,"Collision push-out does not launch a particle after contact ends");
                solver.Reset(); profile.bodyCollisions = false; profile.gravity = new Vector3(0,-9.81f,0);
                solver.Step(1f/60,0,profile,caps,1);
                Check(solver.Position(1).y < 0, "Gravity tuning applies without rebuilding the solver");
                profile.gravity = new Vector3(0,0,-9.81f); profile.backstop = true;
                profile.backstopDistance = .005f; profile.backstopRadius = .05f;
                solver.SetTargets(new List<Vector3>(vertices), Matrix4x4.identity, profile.motion,
                    new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
                solver.Reset();
                for(int i=0;i<10;i++) solver.Step(1f/60,i/60f,profile,caps,0);
                Check(solver.Position(1).z >= -.0051f, "Backstop limits inward motion independently of disabled body collisions");
                Check(solver.Position(0) == Vector3.zero, "Backstop preserves pinned particles");
                profile.backstopDistance = .02f;
                for(int i=0;i<10;i++) solver.Step(1f/60,i/60f,profile,caps,0);
                Check(solver.Position(1).z < -.006f && solver.Position(1).z >= -.0201f, "Backstop distance tunes live");
                profile.backstop = false;
                for(int i=0;i<10;i++) solver.Step(1f/60,i/60f,profile,caps,0);
                Check(solver.Position(1).z < -.025f, "Disabling backstop releases the inward constraint");
                solver.Dispose(); solver = null;
                root = new GameObject("Cloth interaction validation");
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                var bone = new GameObject("Bone"); bone.transform.SetParent(root.transform,false);
                profile.gravity = Vector3.zero; profile.bodyCapsules = Array.Empty<ClothBoneCapsule>();
                session = new MashBoxClothSession(renderer,profile,root.transform);
                var originalSolver = session.Solver;
                session.Step(1f/60); int steps = session.Steps;
                profile.wind = new Vector3(0,0,2); profile.damping = 5; profile.frequency = 90;
                profile.substeps = 3; profile.iterations = 5; profile.thickness = .02f;
                session.Step(1f/90);
                Check(session.Solver == originalSolver && session.Steps == steps + 1, "Live tuning preserves solver identity and simulation progress");
                profile.bodyCollisions = true;
                profile.bodyCapsules = new[] { new ClothBoneCapsule { startBone="Bone", endBone="Bone", radius=.3f } };
                session.Step(1f/90);
                Check(session.Solver.Position(1).magnitude >= .31f, "Adding a capsule binds it live without restarting");
                profile.interpolateMotion=true;int contactSteps=session.Steps;
                bone.transform.position=new Vector3(.08f,0,0);session.Advance(0);
                Check(session.Steps==contactSteps&&Vector3.Distance(session.Vertices[1],bone.transform.position)>=.31999f,"Display-only interpolation resolves render vertices against the current moving capsule");
                Check(session.Vertices[0]==vertices[0],"Render collision correction preserves pinned vertices");
                bone.transform.position=Vector3.zero;
                profile.bodyCapsules = null; session.Step(1f/90);
                Check(session.Solver == originalSolver, "Removing all capsules is safe during simulation");
                profile.particleBudget = 32; session.Step(1f/90);
                Check(session.Solver == originalSolver, "Particle budget stays pending until explicit rebuild");
                profile.collisionFriction = 1; profile.bodyCapsules = new[] { new ClothBoneCapsule { startBone="Missing",endBone="Missing",radius=.2f } };
                session.Step(1f/90);
                Check(!float.IsNaN(session.Solver.Position(1).x), "Missing live collision paths remain safe");
                profile.bodyCollisions = false; profile.gravity = new Vector3(0,-9.81f,0); profile.wind = Vector3.zero;
                profile.frequency = 30; profile.interpolateMotion = true; session.Reset();
                int beforeSteps = session.Steps;
                session.Advance(1f/30);
                var solved = session.Solver.Displacement(session.Solver.Topology.vertexParticle[1]);
                Check(session.Steps == beforeSteps + 1 && solved.magnitude > .00001f, "30 Hz advance performs exactly one physical step");
                Check(Vector3.Distance(session.Vertices[1], vertices[1]) < .00001f, "Interpolation starts at the previous physics snapshot");
                session.Advance(1f/120);
                Check(session.Steps == beforeSteps + 1, "Display-only frame does not run another solver step");
                Check(Vector3.Distance(session.Vertices[1], vertices[1]+solved*.25f) < .00001f, "120 Hz display blends one quarter through a 30 Hz physics interval");
                session.Advance(1f/120);
                Check(Vector3.Distance(session.Vertices[1], vertices[1]+solved*.5f) < .00001f, "Next display frame progresses smoothly to the interval midpoint");
                Check(session.Vertices[0] == vertices[0], "Interpolated output keeps pinned vertices fixed");
                session.Reset();
                Check(Vector3.Distance(session.Vertices[1],vertices[1]) < .00001f, "Reset clears interpolation history");
                profile.interpolateMotion = false; session.Advance(1f/30);
                Check(Vector3.Distance(session.Vertices[1], vertices[1]+session.Solver.Displacement(session.Solver.Topology.vertexParticle[1])) < .00001f, "Disabling interpolation displays the current solved state");
                profile.interpolateMotion = true; session.Step(1f/30);
                Check(Vector3.Distance(session.Vertices[1], vertices[1]+session.Solver.Displacement(session.Solver.Topology.vertexParticle[1])) < .00001f, "Manual step displays the newest state without interpolation delay");
                profile.motion[1]=.000001f; session.Advance(1f/120);
                Check(Vector3.Distance(session.Vertices[1],vertices[1])<=.0000011f,"Display interpolation respects a newly reduced painted movement limit");
                profile.motion[1]=0; session.Advance(0);
                Check(session.Vertices[1]==vertices[1],"Painting a live vertex pinned removes interpolated displacement immediately");
                Check(profile.preserveAuthoredShading,"Authored shading is the default");
                mesh.normals=new[]{Vector3.up,Vector3.up,Vector3.up,Vector3.up};
                mesh.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                profile.normalsEverySteps=1;session.Advance(0);
                Check(session.Output.normals[1]==Vector3.up && session.Output.tangents[1]==new Vector4(1,0,0,1),"Preserved output copies authored normals and tangents despite recalculation cadence");
                profile.preserveAuthoredShading=false;session.Step(1f/30);
                Check(Vector3.Distance(session.Output.normals[1],Vector3.up)>.1f,"Triangle normal recalculation remains available");
                profile.preserveAuthoredShading=true;session.Advance(0);
                Check(session.Output.normals[1]==Vector3.up,"Switching preservation on restores authored shading live");
                profile.preserveAuthoredShading=false;profile.normalsEverySteps=0;mesh.normals=new[]{Vector3.right,Vector3.right,Vector3.right,Vector3.right};session.Advance(0);
                Check(session.Output.normals[1]==Vector3.right,"Zero cadence follows current pose normals instead of freezing old normals");
                var shin=new GameObject("TestShin").transform;shin.SetParent(root.transform);shin.localPosition=Vector3.up*.4f;
                var foot=new GameObject("TestFoot").transform;foot.SetParent(root.transform);foot.localPosition=Vector3.zero;
                var toe=new GameObject("TestToe").transform;toe.SetParent(foot,false);toe.localPosition=Vector3.forward*.15f;
                var ankle=new ClothBoneCapsule{radius=.048f,endRadius=.03f};
                var originalAnkle=ankle.ToWorld(shin,foot);
                Check(originalAnkle.a==shin.position && originalAnkle.b==foot.position,"Zero offsets preserve legacy collider positions");
                AnimationStudioWindow.FitClothAnkle(ref ankle,shin,foot);
                var fitted=ankle.ToWorld(shin,foot);
                Check(Vector3.Distance(fitted.b,foot.position+Vector3.forward*.01f)<.00001f && Mathf.Abs(fitted.endRadius-.052f)<.00001f,"Ankle fit shifts toward toes and widens end radius");
                Check(fitted.a==originalAnkle.a && fitted.radius==originalAnkle.radius,"Ankle fit preserves knee endpoint and radius");
                foot.localRotation=Quaternion.Euler(30,60,0);fitted=ankle.ToWorld(shin,foot);
                Check(Vector3.Distance(fitted.b,foot.TransformPoint(ankle.endOffset))<.00001f && Vector3.Dot((fitted.b-foot.position).normalized,(toe.position-foot.position).normalized)>.999f,"Ankle offset follows animated foot rotation");
                var savedOffset=ankle.endOffset;AnimationStudioWindow.FitClothAnkle(ref ankle,shin,foot);
                Check(Vector3.Distance(savedOffset,ankle.endOffset)<.00001f,"Repeated ankle fitting does not accumulate offset");
                var kneeBefore=ankle.ToWorld(shin,foot);
                AnimationStudioWindow.FitClothKnee(ref ankle,shin,foot);var kneeAfter=ankle.ToWorld(shin,foot);
                Check(kneeAfter.radius>kneeBefore.radius && kneeAfter.a.z<kneeBefore.a.z,"Knee fit widens radius and shifts center backward");
                Check(Mathf.Abs((kneeAfter.a.z+kneeAfter.radius)-(kneeBefore.a.z+kneeBefore.radius))<.00001f,"Knee fit preserves front kneecap extent");
                var kneeOffset=ankle.startOffset;AnimationStudioWindow.FitClothKnee(ref ankle,shin,foot);
                Check(Vector3.Distance(kneeOffset,ankle.startOffset)<.00001f,"Repeated knee fitting does not accumulate offset");
                profile.animationFollow=0;profile.animationInertia=0;profile.gravity=new Vector3(0,-9.81f,0);profile.wind=Vector3.zero;profile.damping=3;
                profile.backstop=false;profile.bodyCollisions=false;profile.stretchCompliance=profile.bendCompliance=1;
                profile.motion=new[]{0f,10f,10f,10f};profile.shapeDamping=.7f;profile.substeps=1;profile.iterations=4;
                float Sag(float support,int frequency)
                {
                    profile.shapeRetention=support;
                    using(var probeSolver=new MashBoxClothSolver(new MashBoxClothTopology(vertices,mesh.triangles,100)))
                    {
                        probeSolver.SetTargets(new List<Vector3>(vertices),Matrix4x4.identity,profile.motion);probeSolver.Reset(true);
                        for(int step=0;step<frequency*3;step++)probeSolver.Step(1f/frequency,step/(float)frequency,profile,Array.Empty<ClothCapsule>(),0);
                        Check(probeSolver.Position(0)==vertices[0],"Shape support preserves painted pin at "+frequency+" Hz, strength "+support);
                        return Vector3.Distance(probeSolver.Position(3),vertices[3]);
                    }
                }
                float unsupported=Sag(0,30),supported=Sag(.35f,30),strong=Sag(1,30),lowRate=Sag(.35f,15);
                Check(supported<unsupported*.25f && supported>.001f,"Gentle shape support reduces gravitational sag while allowing displacement");
                Check(strong<supported,"Increasing shape retention progressively reduces sag");
                Check(!float.IsNaN(lowRate)&&lowRate<.05f && Mathf.Abs(lowRate-supported)<.01f,"Implicit shape support stays stable and consistent at 15 Hz");
                profile.shapeRetention=.35f;profile.bodyCollisions=true;profile.thickness=.02f;
                using(var probeSolver=new MashBoxClothSolver(new MashBoxClothTopology(vertices,mesh.triangles,100)))
                {
                    probeSolver.SetTargets(new List<Vector3>(vertices),Matrix4x4.identity,profile.motion);probeSolver.Reset(true);
                    var contact=new[]{new ClothCapsule(Vector3.zero,Vector3.zero,.3f)};
                    for(int i=0;i<60;i++)probeSolver.Step(1f/30,i/30f,profile,contact,1);
                    Check(probeSolver.Position(1).magnitude>=.31999f,"Body contact takes priority over shape recovery");
                }
                log.Add("SUCCESS");
            }
            catch(Exception e) { log.Add("FAIL " + e); }
            finally
            {
                session?.Dispose(); solver?.Dispose();
                if(root)Object.DestroyImmediate(root);if(mesh)Object.DestroyImmediate(mesh);if(profile)Object.DestroyImmediate(profile);
                File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-interaction-validation.txt")),log);
            }
        }
    }
}
