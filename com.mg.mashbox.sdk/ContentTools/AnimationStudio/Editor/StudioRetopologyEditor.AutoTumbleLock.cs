using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct TumbleNativePoint { public int x,y; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct TumbleNativeRect { public int left,top,right,bottom; }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out TumbleNativePoint point);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x,int y);
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="ClipCursor")]
        private static extern bool ConfineTumbleCursor(ref TumbleNativeRect rect);
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="ClipCursor")]
        private static extern bool ReleaseTumbleCursor(System.IntPtr rect);
        private bool autoTumbleMouseLocked;
        private TumbleNativePoint autoTumbleLockCenter,autoTumbleLockReturn;
        private float autoTumbleLockScale;

        private void LockAutoTumbleMouse(MashBoxStudioViewport canvas)
        {
            if(autoTumbleMouseLocked||!GetCursorPos(out var pointer))return;
            autoTumbleLockScale=Mathf.Max(.1f,EditorGUIUtility.pixelsPerPoint);
            // Anchor GUI coordinates to the actual desktop cursor. This avoids
            // mixing logical and physical monitor origins on mixed-DPI setups.
            var rect=canvas.ModelCanvasRect;var mouse=Event.current.mousePosition;
            int X(float x)=>pointer.x+Mathf.RoundToInt((x-mouse.x)*autoTumbleLockScale);
            int Y(float y)=>pointer.y+Mathf.RoundToInt((y-mouse.y)*autoTumbleLockScale);
            var bounds=new TumbleNativeRect{left=X(rect.xMin+2),top=Y(rect.yMin+2),right=X(rect.xMax-2),bottom=Y(rect.yMax-2)};
            if(bounds.right<=bounds.left||bounds.bottom<=bounds.top)return;
            autoTumbleLockCenter=new TumbleNativePoint{x=(bounds.left+bounds.right)/2,y=(bounds.top+bounds.bottom)/2};
            autoTumbleLockReturn=pointer;
            if(!ConfineTumbleCursor(ref bounds))return;
            autoTumbleMouseLocked=true;
            SetCursorPos(autoTumbleLockCenter.x,autoTumbleLockCenter.y);
        }

        private Vector2 AutoTumbleMouseDelta(Vector2 fallback)
        {
            if(!autoTumbleMouseLocked||!GetCursorPos(out var pointer))return fallback;
            var delta=new Vector2(pointer.x-autoTumbleLockCenter.x,pointer.y-autoTumbleLockCenter.y)/autoTumbleLockScale;
            if(delta.sqrMagnitude>0)SetCursorPos(autoTumbleLockCenter.x,autoTumbleLockCenter.y);
            // Warp-generated mouse events read zero here and cannot steer the target.
            return delta;
        }

        private void UnlockAutoTumbleMouse()
        {
            if(!autoTumbleMouseLocked)return;
            ReleaseTumbleCursor(System.IntPtr.Zero);autoTumbleMouseLocked=false;
            if(view&&EditorWindow.focusedWindow==view&&UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                SetCursorPos(autoTumbleLockReturn.x,autoTumbleLockReturn.y);
        }
#else
        private CursorLockMode autoTumblePreviousLock;
        private bool autoTumbleMouseLocked;
        private void LockAutoTumbleMouse(MashBoxStudioViewport canvas)
        {
            if(autoTumbleMouseLocked)return;
            autoTumblePreviousLock=Cursor.lockState;Cursor.lockState=CursorLockMode.Locked;autoTumbleMouseLocked=true;
        }
        private Vector2 AutoTumbleMouseDelta(Vector2 fallback)=>fallback;
        private void UnlockAutoTumbleMouse()
        {if(autoTumbleMouseLocked)Cursor.lockState=autoTumblePreviousLock;autoTumbleMouseLocked=false;}
#endif
    }
}
