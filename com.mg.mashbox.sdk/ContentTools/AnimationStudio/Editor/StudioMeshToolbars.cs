using System;
using System.Collections.Generic;
using UnityEditor.Overlays;

namespace MashBoxSDK.AnimationStudio
{
    [Overlay(typeof(MashBoxStudioViewport), Id, "Polygon Primitives", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 3, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioPolygonPrimitivesToolbar : StudioCommandToolbar
    {
        internal const string Id = "MashBox/PolygonPrimitives";
        public StudioPolygonPrimitivesToolbar() => Configure(new StudioCustomToolbarDefinition {
            id = Id, name = "Polygon Primitives", commands = new List<string> {
                "Create/Polygon Primitives/Cube", "Create/Polygon Primitives/Sphere",
                "Create/Polygon Primitives/Plane", "Create/Polygon Primitives/Quad", "Create/Polygon Primitives/Cylinder" } });
    }
    [Overlay(typeof(MashBoxStudioViewport), Id, "Mesh Tools", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 4, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioMeshToolsToolbar : StudioCommandToolbar
    {
        internal const string Id = "MashBox/MeshTools";
        public StudioMeshToolsToolbar() => Configure(new StudioCustomToolbarDefinition {
            id = Id, name = "Mesh Tools", commands = new List<string> {
                "Mesh Tools/Extrude", "Mesh Tools/Bevel…", "Mesh Tools/Merge Vertices",
                "Mesh Tools/Target Weld", "Mesh Tools/Insert Edge Loop", "Mesh Tools/Multi-Cut",
                "Deform/Blend Shape", "Deform/Bake Current Pose" } });
    }
    [Overlay(typeof(MashBoxStudioViewport), Id, "Mesh Display", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top,
        defaultDockIndex = 5, defaultLayout = Layout.HorizontalToolbar)]
    internal sealed class StudioMeshDisplayToolbar : StudioCommandToolbar
    {
        internal const string Id = "MashBox/MeshDisplay";
        public StudioMeshDisplayToolbar() => Configure(new StudioCustomToolbarDefinition {
            id = Id, name = "Mesh Display", commands = new List<string> {
                "Mesh Display/Soften", "Mesh Display/Harden", "Mesh Display/Soften…",
                "Mesh Display/X-ray Mode" } });
    }
}
