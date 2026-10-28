using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private float clothGrabRadius = .08f;
        private bool clothGrabDragging, clothResizing;
        private Plane clothGrabPlane;
        private int clothResizeControl;
        private Vector2 clothResizeMouse;
        private float clothResizeStart;
        private Vector3 clothResizePoint, clothResizeNormal;
        private bool clothResizeHit;
        private float ClothToolRadius
        {
            get => clothPainting ? clothBrushRadius : clothGrabRadius;
            set { if (clothPainting) clothBrushRadius = value; else clothGrabRadius = value; }
        }
        private void EndClothGrab()
        {
            clothSession?.Solver.ReleaseGrab(); clothGrabParticle = -1;
            if (clothGrabDragging && GUIUtility.hotControl == clothControl) GUIUtility.hotControl = 0;
            clothGrabDragging = false;
        }
        private bool HandleClothGrabDrag(Event evt)
        {
            if (!clothGrabDragging) return false;
            if (clothSession == null || GUIUtility.hotControl != clothControl || clothTool != 1)
            { EndClothGrab(); return false; }
            if (evt.type == EventType.MouseDrag && evt.button == 0)
            {
                var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (clothGrabPlane.Raycast(ray, out float distance))
                { clothGrabPosition = ray.GetPoint(distance); clothSession.Solver.GrabPosition = clothGrabPosition; }
                evt.Use(); viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            }
            if (evt.type == EventType.Repaint) DrawClothGrabSphere(clothGrabPosition);
            return true;
        }
        private void DrawClothGrabSphere(Vector3 point)
        {
            using (new Handles.DrawingScope(Color.yellow, Matrix4x4.identity))
            {
                Handles.DrawWireDisc(point, Vector3.right, clothGrabRadius);
                Handles.DrawWireDisc(point, Vector3.up, clothGrabRadius);
                Handles.DrawWireDisc(point, Vector3.forward, clothGrabRadius);
            }
        }
        private void EndClothBrushResize()
        {
            if (clothResizing && GUIUtility.hotControl == clothResizeControl) GUIUtility.hotControl = 0;
            clothResizing = false;
        }
        private bool HandleClothBrushResize(SceneView view, bool active)
        {
            var evt = Event.current;
            int id = GUIUtility.GetControlID("Cloth radius".GetHashCode(), FocusType.Passive);
            if (clothResizing)
            {
                if (GUIUtility.hotControl != clothResizeControl) { EndClothBrushResize(); return false; }
                if (evt.type == EventType.MouseDrag && evt.button == 2)
                {
                    ClothToolRadius = Mathf.Clamp(clothResizeStart * Mathf.Exp((evt.mousePosition.x - clothResizeMouse.x) * .01f), .005f, .5f);
                    viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay); evt.Use();
                }
                if (evt.type == EventType.Repaint && clothResizeHit)
                {
                    if (clothPainting)
                    { using (new Handles.DrawingScope(Color.yellow, Matrix4x4.identity)) Handles.DrawWireDisc(clothResizePoint, clothResizeNormal, clothBrushRadius); }
                    else DrawClothGrabSphere(clothResizePoint);
                }
                return true;
            }
            if (!active || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0 || evt.alt) return false;
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.LeftBracket || evt.keyCode == KeyCode.RightBracket))
            {
                ClothToolRadius = Mathf.Clamp(ClothToolRadius * (evt.keyCode == KeyCode.RightBracket ? 1.1f : 1 / 1.1f), .005f, .5f);
                viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay); evt.Use(); return true;
            }
            if (evt.type != EventType.MouseDown || evt.button != 2 || !evt.control) return false;
            clothResizeMouse = evt.mousePosition; clothResizeStart = ClothToolRadius;
            var vertices = clothSession != null ? clothSession.Vertices : clothPickVertices;
            clothResizeHit = ClothHit(HandleUtility.GUIPointToWorldRay(evt.mousePosition), vertices, ClothRenderer.transform.localToWorldMatrix, out clothResizePoint, out clothResizeNormal);
            clothResizing = true; clothResizeControl = id; GUIUtility.hotControl = id; evt.Use(); return true;
        }
    }
}
