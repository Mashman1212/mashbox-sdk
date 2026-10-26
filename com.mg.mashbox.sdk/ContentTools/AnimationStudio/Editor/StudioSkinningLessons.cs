using System.Linq;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioSkinningLessons
    {
        internal sealed class Step
        {
            internal readonly string id, title, help, action, icon;
            internal Step(string id,string title,string help,string action="",string icon="")
            {this.id=id;this.title=title;this.help=help;this.action=action;this.icon=icon;}
        }
        internal sealed class Lesson
        {
            internal readonly string id,title,purpose;
            internal readonly bool pants,unbound;
            internal readonly Step[] steps;
            internal Lesson(string id,string title,string purpose,bool pants,bool unbound,params Step[] steps)
            {this.id=id;this.title=title;this.purpose=purpose;this.pants=pants;this.unbound=unbound;this.steps=steps;}
        }
        private static Step S(string id,string title,string help,string action="",string icon="")=>new Step(id,title,help,action,icon);
        internal static readonly Lesson[] All={
            new Lesson("explore","01 / Meet your workspace","Inspect a practice hoodie and the locked reference character.",false,false,
                S("orbit","Look around","Alt-drag in the viewport to orbit. Your original workspace is parked safely until Return to workspace.","orbit"),
                S("visible","Show and hide a mesh","Open Meshes at the upper left. Toggle a visibility checkbox. Default Body and Bust are locked references; your practice clothing is editable.","visibility"),
                S("hover","Preview an influence","Open Influences. Hover a joint name to preview its weights without selecting it. Warm colors mean more influence.","hover"),
                S("select","Choose an influence","Click a joint in Influences. That joint becomes the target for painting. Expand chains or use search to find Spine or an arm.","influence","paint")),
            new Lesson("fit","02 / Fit before binding","Position unskinned clothing and sculpt local clearance.",false,true,
                S("move","Position the clothing","Use the cube Object mode button, then W, E or R. Make a small transform with a gizmo. Transforms bake into the working geometry; Undo restores them.","transform","object"),
                S("brush","Fit one area to the body","Choose Sculpt, then hold right mouse and drag to Fit to Body. Brush a small area. Body gap and Fit distance are in Brush Settings below this guide. Default meshes stay untouched.","sculpt:FitBody","sculpt"),
                S("size","Tune the brush radius","Hold Ctrl + middle mouse and drag horizontally. The brush circle stays visible off the surface too. Radius changes the area affected.","radius","sculpt"),
                S("review","Inspect clearance","Orbit around the sleeves and sides. Avoid flattening garment details or pulling across to the wrong limb. Use Undo and lighter strokes when needed.")),
            new Lesson("bind","03 / Skin in the pose you choose","Transfer weights from the body and bust, then test deformation.",false,true,
                S("pose","Choose a matching pose","Use the A-pose figure button on the playback bar (frame 2). T-pose and A-pose+ are also available. Clothing and the reference should match before binding.","pose:2","apose"),
                S("bind","Bind the clothing","Use Edit Skins > Skin, or right-click the clothing row > Skin. Weights transfer from the underlying body and bust in the current pose. Joint cleanup runs automatically.","bind"),
                S("test","Scrub the test motion","Drag the bottom timeline to inspect motion. Stop on a bend and look at the sleeve and torso transitions.","scrub"),
                S("unbind","Try unbinding","Use Edit Skins > Unskin on this practice copy. It removes weights so you can refit, then bind again. Undo restores the previous skin.","unbind")),
            new Lesson("paint","04 / Paint with control","Make a small weight correction and smooth its transition.",false,false,
                S("select","Pick the joint to paint","Open Influences and choose Spine1 or an upper arm. Watch the heatmap before you paint.","influence","paint"),
                S("paint","Paint a small correction","Choose Skin Weight Paint. Hold right mouse to choose Paint, then drag on the hoodie. Ctrl erases. Paint a small area with low strength.","paint","paint"),
                S("strength","Lower the strength","Ctrl + middle mouse, drag vertically. The first direction locks the adjustment to strength or radius for that gesture.","strength","paint"),
                S("smooth","Smooth the transition","Choose Smooth weights from the right-click wheel, or hold Shift while painting. Brush over the transition to neighboring influences.","smoothWeights","paint"),
                S("lock","Protect an influence","Click a joint's lock icon in Influences. Locked weights are protected from editing. Unlock it again when you need to change that joint.","lock")),
            new Lesson("sculpt","05 / Shape a skinned garment","Adjust geometry while keeping its skin weights.",false,false,
                S("inflate","Add clearance","Choose Sculpt. Hold right mouse and choose Inflate. Apply a small stroke to the torso. Leave Sculpt + smooth weights off to preserve weights.","sculpt:Inflate","sculpt"),
                S("grab","Adjust a fold","Choose Grab in the wheel. Drag gently. Volume reaches through the mesh; Surface follows connected geometry. Choose the falloff in Brush Settings.","sculpt:Grab","sculpt"),
                S("smooth","Soften the shape","Choose Smooth shape or hold Shift while sculpting. Smooth a small fold without erasing all garment detail.","sculpt:SmoothShape","sculpt"),
                S("test","Test after sculpting","Scrub the timeline to a different pose. Inspect both silhouette and intersections. Sculpt changes shape; weight painting changes how it follows joints.","scrub")),
            new Lesson("mirror","06 / Work symmetrically","Use sculpt symmetry and directional weight mirroring deliberately.",false,false,
                S("symmetry","Enable X symmetry","In Sculpt mode, use the top symmetry control or hold right mouse and hover Symmetry to unfold its wheel. Choose World X or Local X.","symmetry","sculpt"),
                S("stroke","Sculpt both sides","With X symmetry enabled, make a small Inflate or Grab stroke on one side. Inspect the corresponding change on the other.","symmetricStroke","sculpt"),
                S("mirror","Mirror skin weights","Use Edit Skins > Mirror Weights Left to Right or Right to Left. Read the direction before clicking. Mirroring uses A-pose and maps left/right joints.","mirror"),
                S("review","Check asymmetric details","Inspect the center seam and both sides. A pocket or zipper may be intentionally asymmetric; undo the mirror if it harms that detail.")),
            new Lesson("armpits","07 / Shoulder transitions","Review arm pull and smooth an armpit transition.",false,false,
                S("pose","Raise the arms","Click the T-pose figure on the playback bar (frame 0) to expose the underarm silhouette.","pose:0","tpose"),
                S("fix","Check excess arm pull","Use Edit Skins > Reduce Armpit Tenting \u2014 Strong. It retains more torso influence while protecting sleeves. Already-correct weights may need no change.","armpit"),
                S("smooth","Smooth the armpit weights","Use Edit Skins > Smooth Armpit Weights. It softens the transition while limiting changes to the arm contribution. A clean transition may already be smooth.","armpitSmooth"),
                S("review","Compare moving poses","Scrub and orbit. Look for a stable torso with sleeves that follow the arms. These passes run on bind too; use manual repair after further edits when needed.")),
            new Lesson("pants","08 / Pants and the crotch","Keep the pelvis stable while the legs move apart.",true,false,
                S("test","Find a leg bend","Scrub the Skinning Test timeline and inspect the inside legs. Pause on a useful frame.","scrub"),
                S("small","Try the small correction","Use Edit Skins > Fix Crotch Weights - Small. This reduces leg pull near the pelvis and smooths the transition. A clean region can need no changes.","crotchSmall"),
                S("strong","Compare the strong correction","Use Edit Skins > Fix Crotch Weights - Strong. Compare it with Small, then Undo if Small was enough. The displayed pose is restored after analysis.","crotchStrong"),
                S("review","Inspect both legs","Play or scrub and check the front, back and inside legs. Default Body is only a guide. Automatic correction still needs your visual review.")),
            new Lesson("display","09 / Inspect the surface","Check topology, UV stretching and material appearance.",false,false,
                S("wire","Toggle wireframe","Use the wire grid button at the bottom left above playback. Toggle it to inspect the selected garment's topology.","wire","wire"),
                S("uv","Show the UV checker","Use the checkerboard button beside wireframe. Look for stretched rectangles, collapsed rows or seams.","checker","checker"),
                S("texture","Show the material","Use the image/textured display button beside the checker. Expand Materials under the clothing row to inspect imported material slots.","texture","texture"),
                S("review","Review materials and UVs","You can drag a Unity Material onto a slot, mesh row or visible mesh. This changes the practice copy. Custom shader effects may differ from the Studio preview.")),
            new Lesson("finish","10 / Save and keep creating","Understand editable workspaces and game-ready clothing exports.",false,false,
                S("test","Do one last deformation check","Scrub the test motion before exporting. Check sleeves, body intersections and skin transitions.","scrub"),
                S("save","Save a copy when ready","Use File > Save FBX As for a new clothing asset. Practice Save FBX also routes to Save As so the SDK source stays intact. Unity's FBX Exporter is required. Export is optional for this lesson; mark reviewed if you do not need a file."),
                S("workspace","Keep editable work if needed","File > Save workspace stores an editable asset in Assets/Workspaces. Otherwise practice stays in memory. Restart and Return discard unsaved practice edits, while your original workspace stays parked."),
                S("review","Inspect the exported import settings","Exports use T-pose, Humanoid with the SDK SkeletonAvatar, and Import Animation off. Save FBX updates a linked source outside tutorials; Save FBX As makes a copy. Return to workspace when you are ready."))
        };
        internal static Lesson Find(string id)=>All.FirstOrDefault(l=>l.id==id);
        private static string Key(Lesson l,Step s)=>"MashBox.Skinning.Tutorials.v1."+l.id+"."+s.id;
        internal static bool Done(Lesson l,Step s)=>EditorPrefs.GetBool(Key(l,s),false);
        internal static void SetDone(Lesson l,Step s,bool value)=>EditorPrefs.SetBool(Key(l,s),value);
        internal static int Count(Lesson l)=>l.steps.Count(s=>Done(l,s));
        internal static int Total=>All.Sum(l=>l.steps.Length);
        internal static int Completed=>All.Sum(Count);
        internal static void Reset(Lesson l){foreach(var s in l.steps)EditorPrefs.DeleteKey(Key(l,s));}
    }
}
