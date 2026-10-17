using System;
using System.IO;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public static class StudioMenuThemeValidation
{
    private static StudioMenuPopup popup;
    private static StudioMenu initialMenu;
    private static int step = -1, calls;
    private static double next;
    private static string errors = "";
    public static void Run()
    {
        var menu = new StudioMenu();
        menu.AddItem(new GUIContent("Primitives/Cube"), false, () => calls++);
        menu.AddDisabledItem(new GUIContent("Primitives/Disabled"));
        menu.AddItem(new GUIContent("UV/Sets/UV 4"), true, () => calls += 4);
        menu.AddSeparator(""); menu.AddItem(new GUIContent("World"), true, () => calls += 10);
        initialMenu = menu;
        next = EditorApplication.timeSinceStartup + .7;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    private static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += message + "\n" + stack; }
    private static void Check(bool test, string message) { if (!test) throw new Exception(message); }
    private static void Mouse(EventType type, float x, float y) => popup.SendEvent(new Event { type = type, mousePosition = new Vector2(x, y), button = 0 });
    private static void Key(KeyCode key) => popup.SendEvent(new Event { type = EventType.KeyDown, keyCode = key });
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .35;
        try
        {
            switch (step++)
            {
                case -1: popup = StudioMenuPopup.Open(initialMenu, new Rect(200, 200, 80, 20)); break;
                case 0:
                    Check(popup, "Popup did not remain open");
                    Mouse(EventType.MouseMove, 60, 16);
                    Check(popup.ColumnCount == 2 && calls == 0, "Hover did not open submenu without executing command"); break;
                case 1:
                    Mouse(EventType.MouseUp, 280, 41);
                    Check(popup && calls == 0, "Disabled entry executed or closed menu");
                    Mouse(EventType.MouseMove, 60, 41);
                    break;
                case 2:
                    Mouse(EventType.MouseMove, 280, 16);
                    Check(popup.ColumnCount == 3 && calls == 0, "Deep nested UV submenu failed: " + popup.ColumnCount); break;
                case 3:
                    Mouse(EventType.MouseUp, 510, 16); break;
                case 4:
                    Check(!popup && calls == 4, "Leaf did not invoke exactly once and close");
                    var menu = new StudioMenu(); menu.AddDisabledItem(new GUIContent("Disabled"));
                    menu.AddItem(new GUIContent("Local"), true, () => calls += 10);
                    popup = StudioMenuPopup.Open(menu, new Rect(200, 200, 80, 20)); break;
                case 5: Key(KeyCode.DownArrow); Key(KeyCode.Return); break;
                case 6:
                    Check(!popup && calls == 14, "Keyboard navigation did not skip disabled entries and execute selection");
                    var cancel = new StudioMenu(); cancel.AddItem(new GUIContent("Cancel test"), false, () => calls++);
                    popup = StudioMenuPopup.Open(cancel, new Rect(200, 200, 80, 20)); break;
                case 7: Key(KeyCode.Escape); break;
                case 8:
                    Check(!popup && calls == 14, "Escape failed to cancel");
                    Check(string.IsNullOrEmpty(errors), errors);
                    Finish(0, "PASS: Dark Unity popup renders without GUI errors; hover submenus, three-level navigation, disabled items, checked entries, single callback execution, keyboard navigation and Escape."); break;
            }
        }
        catch (Exception e) { Finish(1, "FAIL: " + e + "\n" + errors); }
    }
    private static void Finish(int code, string message)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log; if (popup) popup.Close();
        File.WriteAllText("menu-theme-result.txt", message); EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
