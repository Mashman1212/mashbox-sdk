using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private StickerLayer renamingImageLayer;
        private string imageLayerRenameDraft;
        private bool imageLayerRenameFocus;
        private Rect imageLayerRenameScreenRect;
        private const string ImageLayerRenameControl="MashBoxLayerRename";
        private void BeginImageLayerRename(int index)
        {
            FinishImageLayerRename(true);FinishInlineImageText(true);
            if(index<0||index>=layers.Count)return;
            selectedLayer=index;renamingImageLayer=layers[index];imageLayerRenameDraft=renamingImageLayer.name??"Layer";
            imageLayerRenameFocus=true;layerDragCandidate=null;GUIUtility.hotControl=0;Repaint();
        }
        private void FinishImageLayerRename(bool commit)
        {
            var layer=renamingImageLayer;if(layer==null)return;
            string name=(imageLayerRenameDraft??"").Trim();
            renamingImageLayer=null;imageLayerRenameDraft=null;imageLayerRenameFocus=false;
            GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;
            if(commit&&name.Length>0&&name!=layer.name&&layers.Contains(layer))
            {inputSnapshot=CaptureImage();layer.name=name;MarkCompositeDirty();inputSnapshot=null;}
            Repaint();
        }
        private bool HandleImageLayerRenameInput()
        {
            if(renamingImageLayer==null)return false;var e=Event.current;
            if(e.type==EventType.KeyDown&&(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter||e.keyCode==KeyCode.Escape))
            {FinishImageLayerRename(e.keyCode!=KeyCode.Escape);e.Use();return true;}
            if(e.type==EventType.MouseDown&&!imageLayerRenameFocus&&!imageLayerRenameScreenRect.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition)))
            {FinishImageLayerRename(true);return false;}
            EditorGUIUtility.editingTextField=true;return true;
        }
        private void DrawImageLayerName(Rect row,Rect label,int index)
        {
            var layer=layers[index];var e=Event.current;
            if(e.type==EventType.MouseDown&&e.button==0&&e.clickCount==2&&new Rect(row.x+26,row.y,row.width-26,row.height).Contains(e.mousePosition))
            {BeginImageLayerRename(index);e.Use();}
            if(renamingImageLayer==layer)
            {
                var field=new Rect(label.x,row.center.y-10,Mathf.Max(20,label.width),20);
                imageLayerRenameScreenRect=new Rect(GUIUtility.GUIToScreenPoint(field.position),field.size);
                GUI.SetNextControlName(ImageLayerRenameControl);imageLayerRenameDraft=GUI.TextField(field,imageLayerRenameDraft,255,EditorStyles.textField);
                if(imageLayerRenameFocus&&e.type==EventType.Repaint)
                {GUI.FocusControl(ImageLayerRenameControl);EditorGUIUtility.editingTextField=true;var editor=GUIUtility.GetStateObject(typeof(TextEditor),GUIUtility.keyboardControl) as TextEditor;if(editor!=null){editor.text=imageLayerRenameDraft;editor.SelectAll();}imageLayerRenameFocus=false;}
                return;
            }
            GUI.Label(label,new GUIContent((layer.locked?"• ":"")+(layer.isText?"T  ":"")+(string.IsNullOrWhiteSpace(layer.name)?"Layer":layer.name),"Double-click to rename"),StudioTheme.Styles.label);
            if(GUI.Button(new Rect(row.x+26,row.y,row.width-26,row.height),GUIContent.none,GUIStyle.none)){selectedLayer=index;Repaint();}
        }
    }
}
