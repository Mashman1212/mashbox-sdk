using System;
using System.Linq;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    // Stable IDs keep earned progress independent of lesson order and wording.
    internal static class StudioRetopologyLessons
    {
        internal sealed class Step
        {
            internal readonly string id,title,help,check,tool;
            internal Step(string id,string title,string help,string check="",string tool="")
            {this.id=id;this.title=title;this.help=help;this.check=check;this.tool=tool;}
        }
        internal sealed class Lesson
        {
            internal readonly string id,title,category,purpose,scenario;
            internal readonly Step[] steps;
            internal Lesson(string id,string title,string category,string purpose,string scenario,params Step[] steps)
            {this.id=id;this.title=title;this.category=category;this.purpose=purpose;this.scenario=scenario;this.steps=steps;}
        }
        private static Step S(string id,string title,string help,string check="",string tool="")=>new Step(id,title,help,check,tool);
        internal static readonly Lesson[] All={
            new Lesson("orientation","01 / Find your flow","Foundations","Read the reference and working mesh without losing your place.","grid",
                S("orbit","Explore the practice surface","Alt-drag to orbit, then pan and zoom. The dark surface is your reference; the mint grid is editable topology.","event:navigate"),
                S("wire","Inspect the reference wire","Toggle Reference wire off and on in the display group of the toolbar. Compare its density with the working grid.","referenceWire"),
                S("xray","Try shallow X-ray","Toggle the eye display off and on to reveal nearby patches through the reference. Distant faces stay hidden automatically. X-ray selection is a separate toggle.","xray"),
                S("wheel","Try the marking wheel","Hold right mouse in the viewport. Drag toward Display or Topology to unfold another wheel without releasing. Release over an option.","wheel"),
                S("frame","Frame your work","Move over the viewport and press F. With nothing selected this frames the whole reference.","event:frame")),
            new Lesson("selection","02 / Select with confidence","Foundations","Pick components, follow directional loops and edit a connected patch.","grid",
                S("verts","Follow a vertex loop","Switch to Vertex Select. Click one vertex, then double-click a directly connected neighbor in the direction you want. The first click of the double-click keeps that direction. Shift adds; Ctrl removes. You can also drag a marquee.","event:selectVertexLoop","Vertices"),
                S("edges","Follow an edge loop","Switch to Edge Select using the toolbar or Activate Edge Select below. Double-click an edge to follow its continuous loop. The edge direction determines the loop. Shift-double-click adds another loop. Loops stop at ambiguous poles or triangles.","event:selectEdgeLoop","Edges"),
                S("faces","Follow a face loop","Switch to Face Select. Click a face, then double-click an adjacent face in the direction you want. This selects a row across opposite quad edges. To select the connected hull instead, double-click the same face without stepping to a neighbor. Shift adds; Ctrl removes. Ctrl+A selects all faces.","event:selectFaceLoop","Faces"),
                S("frame","Frame the selection","Press F with components selected to focus on them.","event:frame"),
                S("transform","Move, rotate and scale","Use W, E and R with the Local / Global dropdown. Move some vertices to complete this checkpoint. You can undo afterwards. Scale stays positive.","event:transform"),
                S("delete","Delete a face","Select a single face and press Delete. Undo restores it.","event:Delete selected faces","Faces")),
            new Lesson("draw","03 / Your first faces","Build",
                "Lay down deliberate edge flow, including triangles at density transitions.","blank",
                S("quad","Draw one quad","Activate Draw Quad. Click four corners around a small rectangle on the reference, in perimeter order.","event:Draw projected quad","Quad"),
                S("join","Attach the next face","Snap two corners to the first quad's open edge; gold hover feedback identifies a real vertex. Use two new corners to continue the surface.","event:joinedFace","Quad"),
                S("triangle","Draw a triangle","With Draw Quad, place three distinct corners, then press Enter or click the first corner to close a triangle.","event:Draw triangle","Quad"),
                S("review","Review the transition","Orbit slightly. Check for crossed edges, stretched faces and accidental overlapping faces. Undo and redraw anything that looks wrong.")),
            new Lesson("patch","04 / Patch builder","Build","Cover broad clothing panels with evenly spaced, connected grids.","patch",
                S("patch","Extend the starter patch","Choose Quad Patch. Click two corners along an exposed side of the starter, then two corners beyond that side. Follow the perimeter; shared border vertices are reused.","event:Draw projected quad patch","Patch"),
                S("density-live","Tune the density","Use the Density slider below the top tool buttons: drag right for more quads. Auto size adapts the grid to patch size; turn it off to set divisions. Your most recent patch updates immediately. Existing welded borders keep their counts. Change the density enough to see its grid update; this step checks off only after the mesh changes.","event:patchDensityApplied","Patch"),
                S("continue","Continue with two corners","Enable Continuous in the top toolbar. After the first patch, use two new corners beyond the gold shared edge. Escape ends the chain.","event:continuePatch"),
                S("match","Read the border guide","When filling between existing patches, watch the final-corner segment counts. Green means compatible borders; orange explains a mismatch. Match counts before closing.")),
            new Lesson("strip","05 / Draw a flowing strip","Build","Trace waistbands, seams and narrow runs by dragging along the surface.","strip",
                S("start","Extend the starter edge","Choose Quad Strip. Start on the open right-hand edge of the starter and drag into empty reference surface.","event:Draw quad strip","Strip"),
                S("resume","Resume after releasing","Release, orbit if needed, then begin again on the new open end. Confirm the highlighted border is the one you mean to extend."),
                S("size","Resize the brush","Hold Ctrl + middle mouse and drag horizontally to adjust width. The brush circle remains visible away from the mesh. Use short strokes near bends.","event:brushRadius"),
                S("finish","Join without overlaps","Bring a strip into an existing open border. Inspect the join from two angles. Avoid steering across occupied faces; use Edge Bridge or Target Weld for a precise finish.")),
            new Lesson("tube","06 / Sleeve and leg tubes","Build","Build continuous rings around a limb instead of patching every side.","tube",
                S("points","Place the tube ends","Choose Tube and click two points along the length of the practice sleeve, near its two ends. Use the same limb.","tubePreview","Tube"),
                S("preview","Check sides and rows","Use the Sides and Rows sliders below the top tool buttons. Orbit to inspect the preview. More rings help bends; keep the ends open.","event:tubeSettings","Tube"),
                S("create","Create the tube","Move over the viewport and press Enter, or click Create Tube beside the top sliders. Escape cancels the preview.","event:Create quad tube","Tube"),
                S("review","Check the whole circumference","Orbit around the back. Confirm the rings follow the reference without jumping to another limb. Bridge open ends to other patches later.")),
            new Lesson("weld","07 / Close a seam precisely","Repair","Merge duplicate seam vertices while preserving the target position.","weld",
                S("inspect","Find the split","The two panels nearly meet but have separate vertices along the middle. Turn on vertex selection to inspect the seam.","vertices","Vertices"),
                S("weld","Weld source to target","Choose Target Weld. Click a vertex on one side of the seam, then the corresponding vertex on the other. The first moves to the second.","event:Target weld vertex","TargetWeld"),
                S("repeat","Finish the seam","Complete all five seam welds, matching the same row. Escape clears an unfinished source. Orange feedback means the weld would create invalid topology.","weldSeam"),
                S("undo","Check undo and the result","Undo one weld, then redo it. Inspect the silhouette and verify the seam has one set of vertices.")),
            new Lesson("bridge","08 / Bridge a gap","Repair","Join open edges or entire borders without creating overlapping faces.","gap",
                S("pair","Bridge a pair of edges","Choose Edge Bridge. Click a vertical edge facing the gap on the left panel, then the matching edge on the right. Green preview means a valid welded quad.","event:Bridge edges","EdgeBridge"),
                S("reset","Reset for the border exercise","Use Reset practice mesh below to restore two clean panels while keeping this lesson's progress. Restart scenario clears the entire checklist.","event:restart"),
                S("borders","Select matching border chains","Choose Edges. Shift-click the three gap-facing edges on each panel. Select only the two facing chains, not their outer borders.","edges","Edges"),
                S("bridge","Bridge selected borders","Use Topology > Bridge Selected Borders. Set Bridge Bands below the top toolbar (1 makes a single band), then bridge. Adjust it immediately afterward to tune the last bridge. The two chains need compatible counts.","event:Bridge boundary borders"),
                S("review","Check the join","Inspect each side. The new faces should reuse the border vertices. Relax the interior if spacing needs improvement.")),
            new Lesson("relax","09 / Relax and refine","Refine","Even out a distorted patch and put extra resolution only where it helps.","distorted",
                S("relax","Brush uneven spacing","Choose Relax Brush and drag over the distorted center. Border protection preserves the outline; snapping keeps the patch on the surface.","event:Relax topology brush","RelaxBrush"),
                S("adjust","Adjust one brush value at a time","Ctrl + middle mouse drag: horizontal changes radius; vertical changes strength. The first drag direction locks the adjustment. Try a softer second pass.","brushAdjusted"),
                S("increase","Add local resolution","Choose Resolution and paint a small area. Release to apply one level. Refinement may follow adjoining quad strips to avoid cracks.","event:Increase topology resolution","Resolution"),
                S("decrease","Reduce a clean block","Ctrl-paint over a complete 2 x 2 quad block and release. Reduction only works on suitable blocks; inspect the status if nothing changes.","event:Reduce topology resolution","Resolution"),
                S("review","Review density transitions","Orbit and inspect the silhouette. Avoid very thin quads and unnecessary density. Topology edits require UV review before texturing.")),
            new Lesson("symmetry","10 / Work both sides","Refine","Mirror repeated clothing panels while keeping the center seam intentional.","blank",
                S("enable","Enable mirror drawing","Enable Mirror drawing X in the toolbar or Tool settings. The plane is the reference's local X = 0.","symmetry"),
                S("draw","Draw on one side","Draw a small quad on the left half of the reference. A mirrored face should appear on the right.","event:Draw projected quad","Quad"),
                S("inspect","Inspect the center","Avoid drawing across the mirror plane until you have planned the center seam. Symmetry is not a substitute for checking welds."),
                S("mirror","Mirror existing topology","Disable Mirror drawing X and move a vertex on one half. Use Topology > Mirror +X to -X or the reverse to copy that half. Inspect the result and undo if you chose the wrong direction.","event:Mirror topology")),
            new Lesson("cleanup","11 / Cleanup","Finish","Clean up topology, project it onto the reference and inspect the result.","triangles",
                S("pair","Pair compatible triangles","Use Topology > Pair Triangles into Quads on the supplied triangulated panel. Only suitable pairs merge.","event:Pair quads"),
                S("project","Project onto the reference","Use Topology > Project to Reference. Surface offset gives a little clearance; display X-ray does not move geometry.","event:Project topology"),
                S("subdivide","Subdivide and project","Use Topology > Subdivide and Project to increase density, then inspect the projected shape.","event:Subdivide"),
                S("clean","Clean and inspect","Use Topology > Clean Mesh. It cleans and checks the mesh automatically, completing this checkpoint even if no cleanup was needed. Review the report for non-manifold edges and degenerate faces; open borders are expected on a panel.","event:validate")),
            new Lesson("pants","12 / Pants workshop","Clothing","Practice waist and leg flow on the SDK pants reference.","pants",
                S("generate","Generate the pants cage","Choose Pants preset in Tool settings, then Generate pants quad cage. The reference stays unchanged.","event:Generate pants quad cage"),
                S("inspect","Review the crotch and knees","Inspect front, back and inside legs. The cage is a starting point; pockets and straps need their own topology. Adjust density for bends."),
                S("relax","Refine a problem area","Choose Relax Brush and gently work the crotch or a knee. Preserve openings and inspect both sides.","event:Relax topology brush","RelaxBrush"),
                S("audit","Judge the result","Use Validate Topology. Check the silhouette, quad proportions and joint loops. Automated retopology still needs this artist review.")),
            new Lesson("uvtransfer","13 / Transfer and inspect UVs","Finish","Reuse reference UVs and check stretching on your new topology.","uvtransfer",
                S("reference","Inspect the reference UVs","Turn on Reference Checker in the top display buttons (checker with the backing sheet). Hide Working mesh in Meshes to inspect the reference. Both checkers use the same scale.","event:referenceChecker"),
                S("transfer","Transfer reference UVs","Use the top UV menu > Transfer UVs from Reference. This copies UV0 to the working surface. The reference and editable vertex connections stay unchanged. Undo reverses the transfer.","event:transferUVs"),
                S("working","Inspect the working UVs","Show Working mesh, then enable Working Mesh Checker (single checker icon). Hide Reference in Meshes for an unobstructed view. Look for stretched rectangles or collapsed checker rows.","event:workingChecker"),
                S("referenceMaterial","Inspect the reference material","Use the Reference Textured button (image icon with a backing sheet). Expand Materials under the mesh to see its slots. Imported model materials are retained.","event:referenceTexture"),
                S("assign","Assign a material","Drag a Material from the Unity Project onto a material slot, the mesh row, or a visible mesh in the viewport. A viewport drop targets the surface material slot; a row drop sets Slot 1. Assignments are workspace edits and support Undo.","event:assignMaterial"),
                S("workingMaterial","Inspect the textured working mesh","Enable Working Mesh Textured (single image icon). This uses the same material slots as the reference. Switch between texture and checker to review the UV transfer. Studio previews base color, normal maps and cutouts; custom shader effects may differ.","event:workingTexture"),
                S("review","Compare seams and stretching","Orbit and compare both meshes using their visibility and checker switches. Transfer follows the nearest surface; faces crossing UV seams or spanning folds can need manual UV work. Use Modeling for a fresh unwrap when needed.")),
            new Lesson("handoff","14 / Finish and hand off","Finish","Move a finished mesh to the next tool without losing the reference.","grid",
                S("check","Inspect before export","Use Topology > Validate Topology. Check face winding and the reference / working counts. New topology needs UV work in Modeling.","event:validate"),
                S("save","Save a practice workspace","File > Save Workspace saves an editable project asset. Pick a new practice filename if you want to keep this exercise. This is optional; mark reviewed once you understand it."),
                S("export","Choose an export format","File > Export Working Mesh offers FBX and OBJ (quads). OBJ keeps polygon faces; FBX needs Unity's FBX Exporter installed. Use a new filename for practice."),
                S("send","Try the next studio","File > Send to Skinner creates a working copy; source weights transfer only when the reference has skin data. This procedural panel is unskinned. Continue in Modeling is available from Edit. Return to Retopology afterwards."),
                S("clear","Understand clear versus remove","On this practice mesh, use mesh-list right-click > Clear Working Mesh. It keeps the reference; Undo restores the faces. Remove Mesh removes the entire workspace entry. Neither deletes your source asset.","event:Clear working mesh"))
        };
        internal static Lesson Find(string id)=>All.FirstOrDefault(l=>l.id==id);
        private static string Key(Lesson l,Step s)=>"MashBox.Retopology.Tutorials.v1."+l.id+"."+s.id;
        internal static bool Done(Lesson l,Step s)=>EditorPrefs.GetBool(Key(l,s),false);
        internal static void SetDone(Lesson l,Step s,bool done)=>EditorPrefs.SetBool(Key(l,s),done);
        internal static int Count(Lesson l)=>l.steps.Count(s=>Done(l,s));
        internal static int Total=>All.Sum(l=>l.steps.Length);
        internal static int Completed=>All.Sum(Count);
        internal static void Reset(Lesson l){foreach(var s in l.steps)EditorPrefs.DeleteKey(Key(l,s));}
    }
}
