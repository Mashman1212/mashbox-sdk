using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class StudioThemeValidation : EditorWindow
{
    private static readonly List<string> Results = new List<string>();
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool hadPrefs, preview;
    private static string oldPrefs, errors = "";
    private static AnimationStudioWindow controller;
    private static MashBoxStudioViewport viewport;
    private int paletteIndex;
    private bool finished;
    public static void Preview() { preview = true; Run(); }
    public static void Run()
    {
        hadPrefs = EditorPrefs.HasKey(StudioSettings.PreferenceKey); oldPrefs = EditorPrefs.GetString(StudioSettings.PreferenceKey);
        Application.logMessageReceived += Log;
        try
        {
            Check(StudioTheme.Palettes.Length == 20 && StudioTheme.Palettes.Select(p => p.Id).Distinct().Count() == 20, "20 uniquely named palettes");
            EditorPrefs.SetString(StudioSettings.PreferenceKey, "invalid JSON"); StudioSettings.Reload();
            Check(StudioSettings.Current.theme == "graphite", "Invalid saved preferences safely fall back to defaults");
            StudioSettings.Current.theme = "missing"; StudioSettings.Current.gridSpacing = float.NaN; StudioSettings.Current.uvPointSize = 100; StudioSettings.Save();
            Check(StudioSettings.Current.theme == "graphite" && StudioSettings.Current.gridSpacing == .25f && StudioSettings.Current.uvPointSize == 8, "Unknown themes and invalid numeric settings are sanitized");
            StudioSettings.Reset();
            AnimationStudioWindow.Open(); controller = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            viewport = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Fields).Invoke(controller, new object[] { true });
            StudioUVEditor.Open(null); StudioSettingsWindow.Open();
            var window = CreateInstance<StudioThemeValidation>(); window.ShowUtility(); window.Repaint();
        }
        catch (Exception e) { Finish(1, e.ToString()); }
    }
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Results.Add("PASS: " + text); }
    private static void Log(string text, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += text + "\n" + stack + "\n"; }
    private void OnInspectorUpdate() { Repaint(); }
    private void OnGUI()
    {
        if (finished || Event.current.type != EventType.Repaint) return;
        try
        {
            if (paletteIndex < StudioTheme.Palettes.Length)
            {
                var palette = StudioTheme.Palettes[paletteIndex++]; StudioSettings.Current.theme = palette.Id;
                StudioSettings.Current.gridSpacing = .5f; StudioSettings.Current.gridOpacity = .7f; StudioSettings.Current.uvPointSize = 5;
                StudioSettings.Save(); StudioSettings.Reload();
                Check(StudioSettings.Current.theme == palette.Id && StudioSettings.Current.gridSpacing == .5f && StudioSettings.Current.uvPointSize == 5, palette.Name + " preference round-trip");
                var panel = (IMGUIContainer)typeof(MashBoxStudioViewport).GetField("inspector", Fields).GetValue(viewport);
                Check(panel.style.backgroundColor.value == palette.Panel, palette.Name + " updates an existing viewport");
                var originalSkin = GUI.skin; var originalText = EditorStyles.label.normal.textColor; var originalBackground = GUI.backgroundColor;
                using (StudioTheme.Scope())
                {
                    GUI.Label(new Rect(0, 0, 150, 20), palette.Name, StudioTheme.Styles.label);
                    Check(((Vector4)StudioTheme.Styles.miniButton.normal.background.GetPixel(0, 0) - (Vector4)palette.Button).sqrMagnitude < .0001f, palette.Name + " themed control background");
                    var cached = StudioTheme.Styles.miniButton;
                    Check(ReferenceEquals(cached, StudioTheme.Styles.miniButton), palette.Name + " reuses cached styles");
                    var themedSkin = GUI.skin;
                    try { using (StudioTheme.Scope()) throw new InvalidOperationException("scope check"); } catch (InvalidOperationException) { }
                    Check(GUI.skin == themedSkin, palette.Name + " nested scope survives exceptions");
                }
                Check(GUI.skin == originalSkin && EditorStyles.label.normal.textColor == originalText && GUI.backgroundColor == originalBackground, palette.Name + " restores Unity UI state");
                return;
            }
            if (preview)
            {
                if (paletteIndex++ == StudioTheme.Palettes.Length)
                {
                    StudioSettings.Current.theme = "ocean"; StudioSettings.Save();
                    var main = EditorGUIUtility.GetMainWindowPosition();
                    viewport.position = new Rect(main.x + 30, main.y + 40, 1300, 800);
                    var settings = Resources.FindObjectsOfTypeAll<StudioSettingsWindow>().First();
                    settings.position = new Rect(main.x + 900, main.y + 80, 470, 830); settings.ShowUtility(); settings.Focus();
                }
                if (!File.Exists("theme-preview-finish.txt")) return;
            }
            Check(string.IsNullOrEmpty(errors), "Studio, UV editor, and settings render without GUI errors: " + errors);
            finished = true; Finish(0, null);
        }
        catch (Exception e) { finished = true; Finish(1, e.ToString()); }
    }
    private static void Finish(int code, string error)
    {
        if (error != null) Results.Add("FAIL: " + error);
        Application.logMessageReceived -= Log;
        foreach (var window in Resources.FindObjectsOfTypeAll<StudioSettingsWindow>()) window.Close();
        foreach (var window in Resources.FindObjectsOfTypeAll<StudioUVEditor>()) window.Close();
        if (viewport) viewport.Close(); if (controller) DestroyImmediate(controller);
        if (hadPrefs) EditorPrefs.SetString(StudioSettings.PreferenceKey, oldPrefs); else EditorPrefs.DeleteKey(StudioSettings.PreferenceKey);
        StudioSettings.Reload(); File.WriteAllLines("theme-result.txt", Results);
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

