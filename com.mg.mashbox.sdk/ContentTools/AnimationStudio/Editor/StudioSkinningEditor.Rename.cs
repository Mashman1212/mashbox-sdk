using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private StudioSkinningDocument.Part renamingPart;
        private string meshRenameText;
        private bool focusMeshRename;
        private const string MeshRenameControl = "MashBoxSkinMeshRename";

        private void BeginMeshRename(StudioSkinningDocument.Part part)
        {
            if(part == null || part.referenceOnly || !Document.parts.Contains(part)) return;
            FinishMeshRename(true); EndStroke();
            Document.selected=Document.parts.IndexOf(part); RefreshSurface();
            renamingPart=part; meshRenameText=part.name; focusMeshRename=true;
            repaint();
        }

        private void FinishMeshRename(bool commit)
        {
            var part=renamingPart; renamingPart=null; focusMeshRename=false;
            string name=(meshRenameText ?? "").Trim();
            if(commit && part != null && !part.referenceOnly && Document.parts.Contains(part) && name.Length>0 && name!=part.name)
            {
                Undo.RecordObject(Document,"Rename mesh"); part.name=name;
                int index=Document.parts.IndexOf(part);
                if(index<objects.Count && objects[index]) objects[index].name=name;
                Changed();
            }
            if(GUI.GetNameOfFocusedControl()==MeshRenameControl) GUI.FocusControl(null);
            repaint();
        }
        private bool DrawMeshRename(Rect row,StudioSkinningDocument.Part part,int index)
        {
            var e=Event.current;
            if(e.type==EventType.MouseDown && e.button==0 && e.clickCount==2 && row.Contains(e.mousePosition))
            {
                BeginMeshRename(part);
                e.Use();
            }
            if(renamingPart!=part) return false;
            if(e.type==EventType.KeyDown && (e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter || e.keyCode==KeyCode.Escape))
            {
                bool commit=e.keyCode!=KeyCode.Escape; e.Use(); FinishMeshRename(commit); return false;
            }
            if(e.type==EventType.MouseDown && !row.Contains(e.mousePosition))
            { FinishMeshRename(true); return false; }
            GUI.SetNextControlName(MeshRenameControl);
            meshRenameText=EditorGUI.TextField(row,meshRenameText);
            if(focusMeshRename)
            {
                EditorGUI.FocusTextInControl(MeshRenameControl);
                if(e.type==EventType.Repaint) focusMeshRename=false;
            }
            return true;
        }
    }
}
