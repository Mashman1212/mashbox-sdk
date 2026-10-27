using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private bool selectionCursorActive;
#if UNITY_EDITOR_WIN
        [DllImport("user32.dll",EntryPoint="LoadCursorW")]
        private static extern IntPtr LoadImageSystemCursor(IntPtr instance,IntPtr name);
        [DllImport("user32.dll",EntryPoint="SetCursor")]
        private static extern IntPtr SetImageSystemCursor(IntPtr cursor);
        private static IntPtr selectionSystemCursor;
#else
        private Texture2D selectionCursorTexture;
#endif
        private void UpdateImageSelectionCursor(bool insideView)
        {
            bool active=insideView && mouseOverWindow==this && Event.current.type!=EventType.MouseLeaveWindow && !panning &&
                (imageTool==ImageTool.Marquee || drawingImageSelection || tracingSelection || choosingColorRange);
            if(!active){RestoreImageSelectionCursor();return;}
            // CustomCursor prevents Unity's FPS/arrow cursor from being installed over ours.
            EditorGUIUtility.AddCursorRect(new Rect(0,0,canvasArea.width,canvasArea.height),MouseCursor.CustomCursor);
#if UNITY_EDITOR_WIN
            // IDC_CROSS is the OS crosshair: a single precise cross, with no arrow or aim graphic.
            if(selectionSystemCursor==IntPtr.Zero)selectionSystemCursor=LoadImageSystemCursor(IntPtr.Zero,new IntPtr(32515));
            bool move=movingImageSelection||(drawingImageSelection&&marqueeSpaceHeld)||(!drawingImageSelection&&!tracingSelection&&!choosingColorRange&&selectionMoveHover);
            if(move)SetImageSystemCursor(LoadImageSystemCursor(IntPtr.Zero,new IntPtr(32646)));
            else if(selectionSystemCursor!=IntPtr.Zero)SetImageSystemCursor(selectionSystemCursor);
#else
            if(!selectionCursorTexture)
            {
                const int size=19;var pixels=new Color32[size*size];
                for(int y=1;y<size-1;y++)for(int x=1;x<size-1;x++)
                    if(Mathf.Abs(x-9)<=1 || Mathf.Abs(y-9)<=1)pixels[y*size+x]=(x==9||y==9)?(Color32)Color.white:(Color32)Color.black;
                selectionCursorTexture=new Texture2D(size,size,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
                selectionCursorTexture.SetPixels32(pixels);selectionCursorTexture.Apply();
            }
            Cursor.SetCursor(selectionCursorTexture,new Vector2(9,9),CursorMode.Auto);
#endif
            selectionCursorActive=true;
        }
        private void RestoreImageSelectionCursor()
        {
            if(!selectionCursorActive)return;
#if UNITY_EDITOR_WIN
            SetImageSystemCursor(LoadImageSystemCursor(IntPtr.Zero,new IntPtr(32512)));
#else
            Cursor.SetCursor(null,Vector2.zero,CursorMode.Auto);
#endif
            selectionCursorActive=false;
        }
        private void ReleaseImageSelectionCursor()
        {
            RestoreImageSelectionCursor();
#if !UNITY_EDITOR_WIN
            if(selectionCursorTexture)DestroyImmediate(selectionCursorTexture);
            selectionCursorTexture=null;
#endif
        }
    }
}
