using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class MashBoxStudioViewport
    {
        private VisualElement menuInputShield;
        private int menuShieldPointer = -1;
        private bool MenuInputBlocked => menuShieldPointer >= 0 || StudioMenuPopup.BlocksInput(this);

        // Apply at every binding, including callbacks rebound after BindModelInput.
        private Action WrapStudioGUI(Action draw, bool menu = false)
        {
            var themed = StudioTheme.Wrap(draw);
            return () => {
                var evt = Event.current;
                if (menu || !MenuInputBlocked || evt == null) { themed?.Invoke(); return; }
                Vector2 mouse = evt.mousePosition;
                try
                {
                    // Repaint also needs the masked pointer: IMGUI hover is computed
                    // while drawing, independently of mouse-event consumption.
                    evt.mousePosition = new Vector2(-100000,-100000);
                    if (evt.isMouse || evt.isKey || evt.type == EventType.ScrollWheel || evt.type == EventType.ContextClick || evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) evt.Use();
                    StudioTheme.WithoutHover(themed);
                }
                finally { evt.mousePosition = mouse; }
            };
        }
        internal void SetMenuInputBlocked(bool blocked)
        {
            if (blocked && menuInputShield == null)
            {
                menuInputShield = new VisualElement {name="mashbox-menu-input-shield",pickingMode=PickingMode.Position};
                menuInputShield.style.position=Position.Absolute;
                menuInputShield.style.left=0;menuInputShield.style.right=0;
                menuInputShield.style.top=24;menuInputShield.style.bottom=0;
                menuInputShield.RegisterCallback<PointerDownEvent>(evt => {
                    menuShieldPointer=evt.pointerId;menuInputShield.CapturePointer(evt.pointerId);
                    evt.StopImmediatePropagation();StudioMenuPopup.CloseInputMenu(this);
                });
                menuInputShield.RegisterCallback<PointerUpEvent>(evt => {evt.StopImmediatePropagation();EndMenuShieldPointer();});
                menuInputShield.RegisterCallback<PointerCancelEvent>(evt => {evt.StopImmediatePropagation();EndMenuShieldPointer();});
                menuInputShield.RegisterCallback<PointerCaptureOutEvent>(evt => EndMenuShieldPointer());
                menuInputShield.RegisterCallback<PointerMoveEvent>(evt => evt.StopImmediatePropagation());
                menuInputShield.RegisterCallback<WheelEvent>(evt => evt.StopImmediatePropagation());
                menuInputShield.RegisterCallback<DragUpdatedEvent>(evt => evt.StopImmediatePropagation());
                menuInputShield.RegisterCallback<DragPerformEvent>(evt => evt.StopImmediatePropagation());
                rootVisualElement.Add(menuInputShield);
            }
            EditorApplication.update -= KeepMenuShieldOnTop;
            bool visible=blocked || menuShieldPointer>=0;
            if (menuInputShield != null) menuInputShield.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if (visible) {EditorApplication.update+=KeepMenuShieldOnTop;KeepMenuShieldOnTop();}
            foreach (var container in rootVisualElement.Query<IMGUIContainer>().ToList()) container.MarkDirtyRepaint();
            RepaintUI();
        }
        private void KeepMenuShieldOnTop()
        {
            if (!this || menuInputShield == null) {EditorApplication.update-=KeepMenuShieldOnTop;return;}
            menuInputShield.BringToFront();
            // Menubar hover switching remains available above the blocked content.
            modelMenuBar?.BringToFront();modeSelector?.BringToFront();
        }
        private void EndMenuShieldPointer()
        {
            int pointer=menuShieldPointer;menuShieldPointer=-1;
            if (pointer>=0 && menuInputShield != null && menuInputShield.HasPointerCapture(pointer)) menuInputShield.ReleasePointer(pointer);
            SetMenuInputBlocked(StudioMenuPopup.BlocksInput(this));
        }
        private void ClearMenuInputShield()
        {
            EditorApplication.update-=KeepMenuShieldOnTop;
            int pointer=menuShieldPointer;menuShieldPointer=-1;
            if(menuInputShield!=null && pointer>=0 && menuInputShield.HasPointerCapture(pointer))menuInputShield.ReleasePointer(pointer);
            EditorApplication.update-=KeepMenuShieldOnTop;
        }
    }
}
