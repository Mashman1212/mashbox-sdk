using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool SymmetryActive => sculptMode && Tool != Brush.Pose && Document.sculptSymmetryAxis > 0 && Part != null;
        private Rect SymmetryRect(SceneView view)
        {
            var mode = ModeRect(view);
            return new Rect(mode.x,mode.yMax+5,204,24);
        }
        private bool OverSymmetry(SceneView view, Vector2 mouse) => sculptMode && Tool != Brush.Pose && SymmetryRect(view).Contains(mouse);
        private void SetSymmetry(int axis, bool local)
        {
            EndStroke(); Undo.RecordObject(Document,"Sculpt symmetry");
            Document.sculptSymmetryAxis=axis; Document.sculptSymmetryLocal=local; Changed();
        }
        private Matrix4x4 SymmetryFrame => Document.sculptSymmetryLocal && Part != null
            ? Matrix4x4.TRS(Part.sourceToWorkspace.GetColumn(3),Part.sourceToWorkspace.rotation,Vector3.one)
            : Matrix4x4.identity;
        private Vector3 ReflectSymmetry(Vector3 value, bool point)
        {
            var frame=SymmetryFrame;
            var local=point ? frame.inverse.MultiplyPoint3x4(value) : frame.inverse.MultiplyVector(value);
            int axis=Mathf.Clamp(Document.sculptSymmetryAxis-1,0,2); local[axis]=-local[axis];
            return point ? frame.MultiplyPoint3x4(local) : frame.MultiplyVector(local);
        }
        private void ShowSymmetryMenu(Rect rect)
        {
            var menu=new GenericMenu();
            menu.AddItem(new GUIContent("Symmetry Off"),Document.sculptSymmetryAxis==0,()=>SetSymmetry(0,Document.sculptSymmetryLocal));
            for(int i=1;i<=3;i++)
            {
                int axis=i; string label="XYZ"[i-1].ToString();
                menu.AddItem(new GUIContent("World/"+label),Document.sculptSymmetryAxis==i && !Document.sculptSymmetryLocal,()=>SetSymmetry(axis,false));
                menu.AddItem(new GUIContent("Local/"+label),Document.sculptSymmetryAxis==i && Document.sculptSymmetryLocal,()=>SetSymmetry(axis,true));
            }
            menu.DropDown(rect);
        }
        private void DrawSymmetryToolbar(SceneView view)
        {
            if(!sculptMode || Tool==Brush.Pose) return;
            var rect=SymmetryRect(view);
            for(int i=1;i<=3;i++)
            {
                var button=new Rect(rect.x+(i-1)*30,rect.y,26,24); int axis=i;
                bool active=Document.sculptSymmetryAxis==i;
                if(GUI.Button(button,new GUIContent("XYZ"[i-1].ToString(),"Toggle sculpt symmetry on "+"XYZ"[i-1]),StudioTheme.Styles.overlayButton))
                    SetSymmetry(active ? 0 : axis,Document.sculptSymmetryLocal);
                if(active) EditorGUI.DrawRect(new Rect(button.x+4,button.yMax-2,button.width-8,2),new Color(.5f,1,.87f));
            }
            var space=new Rect(rect.x+94,rect.y,110,24);
            if(EditorGUI.DropdownButton(space,new GUIContent(Document.sculptSymmetryLocal ? "Local" : "World","Symmetry space: local uses the imported mesh's origin and axes"),FocusType.Passive,StudioTheme.Styles.menuDropdown))
                ShowSymmetryMenu(space);
        }
        private Rect SymmetryMenuRect => new Rect(menuOrigin.x-65,menuOrigin.y+94,130,26);
        private void DrawMirroredBrush(Vector3 point,Vector3 normal)
        {
            if(!SymmetryActive) return;
            var previous=Handles.color;
            Handles.color=new Color(.5f,1,.87f,.8f);
            Handles.DrawWireDisc(ReflectSymmetry(point,true),ReflectSymmetry(normal,false).normalized,radius);
            Handles.color=previous;
        }
    }
}
