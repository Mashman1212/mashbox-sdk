using System;
using System.Collections.Generic;
using UnityEditor.Overlays;

namespace MashBoxSDK.AnimationStudio
{
    [Overlay(typeof(MashBoxStudioViewport), Id, "Curves", false,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 6, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioCurvesToolbar : StudioCommandToolbar
    {
        internal const string Id = "MashBox/Curves";
        protected override bool MenuGroupSeparators => true;
        public StudioCurvesToolbar() => Configure(new StudioCustomToolbarDefinition {
            id=Id, name="Curves", commands=new List<string> {
                "Curves/Linear Spline", "Curves/Bezier Spline", "Curves/Edit Knots",
                "Curves/Finish Drawing", "Curves/Insert Knot", "Curves/Cut at Point", "Curves/Remove Knot", "Curves/Extend Endpoint",
                "Curves/Open Loop", "Curves/Close Loop", "Curves/Reverse Direction",
                "Curves/Convert to Linear", "Curves/Convert to Bezier", "Curves/Corner Tangents",
                "Curves/Aligned Tangents", "Curves/Mirrored Tangents", "Curves/Smooth Knots", "Curves/Sweep Mesh" } });
    }
    [Overlay(typeof(MashBoxStudioViewport), Id, "Polygon Primitives", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 3, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioPolygonPrimitivesToolbar : StudioCommandToolbar
    {
        internal const string Id = "MashBox/PolygonPrimitives";
        public StudioPolygonPrimitivesToolbar() => Configure(new StudioCustomToolbarDefinition {
            id = Id, name = "Polygon Primitives", commands = new List<string> {
                "Create/Polygon Primitives/Cube", "Create/Polygon Primitives/Sphere",
                "Create/Polygon Primitives/Plane", "Create/Polygon Primitives/Quad", "Create/Polygon Primitives/Cylinder", "Create/Polygon Primitives/Polygon" } });
    }
    [Overlay(typeof(MashBoxStudioViewport), Id, "Mesh Tools", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 4, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioMeshToolsToolbar : StudioCommandToolbar
    {
        protected override bool MenuGroupSeparators => true;
        internal const string Id = "MashBox/MeshTools";
        public StudioMeshToolsToolbar() => Configure(new StudioCustomToolbarDefinition {
            id = Id, name = "Mesh Tools", commands = new List<string> {
                "Mesh Tools/Combine Meshes…", "Mesh Tools/Duplicate Face",
                "Mesh Tools/Duplicate Face as New Object", "Mesh Tools/Detach Face",
                "Mesh Tools/Split Selection", "Mesh Tools/Bridge Edges",
                "Mesh Tools/Extrude", "Edit Mesh/Fill Hole", "Edit Mesh/Poke", "Mesh Tools/Bevel…", "Mesh Tools/Circularize…",
                "Edit Mesh/To Edge Ring and Collapse", "Mesh Tools/Weld…", "Mesh Tools/Merge Vertices", "Mesh Tools/Target Weld",
                "Mesh Tools/Insert Edge Loop", "Mesh Tools/Bisect", "Mesh Tools/Fracture", "Mesh Tools/Multi-Cut",
                "Deform/Blend Shape", "Deform/Bake Current Pose" } });
    }
    [Overlay(typeof(MashBoxStudioViewport), Id, "Mesh Display", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 5, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioMeshDisplayToolbar : StudioCommandToolbar
    {
        protected override bool MenuGroupSeparators => true;
        internal const string Id = "MashBox/MeshDisplay";
        public StudioMeshDisplayToolbar() => Configure(new StudioCustomToolbarDefinition {
            id = Id, name = "Mesh Display", commands = new List<string> {
                "Mesh Display/Soften", "Mesh Display/Harden", "Mesh Display/Flip Normals", "Mesh Display/Soften…",
                "Mesh Display/X-ray Mode" } });
    }
}
