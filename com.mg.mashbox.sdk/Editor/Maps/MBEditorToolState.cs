using System;
using MashBoxSDK.Maps.Spline;
using UnityEditor;

namespace MashBoxSDK.MapTools
{
    public enum MBEditorAuthoringMode
    {
        Brush,
        SplineLoft,
        Spline,
        MeshSculpt,
        UVSpline,
        Terrain
    }

    public enum MBEditorAuthoringCategory { Brush, Spline, Terrain }

    public enum MBBrushMode { Decor, Painter, SplatMap }
    public enum MBSculptMode { Displace, Smooth, Flatten }
    public enum MBUvHandleMode { MoveAndUv, SideOffset, UvScale }
    public enum MBEditorToolAction { CreateSpline, CreateLoftSpline }

    [InitializeOnLoad]
    internal static class MBEditorToolState
    {
        const string ModePreferenceKey = "MashBoxSDK.SelectedMapAuthoringToolTab";
        const string ModeOrderPreferenceKey = "MashBoxSDK.SelectedMapAuthoringToolTab.Order";
        const string ActiveEditingPreferenceKey = "MashBoxSDK.EditorTools.ActiveEditing";
        const string BrushModePreferenceKey = "MashBoxSDK.EditorTools.BrushMode";
        const string SculptModePreferenceKey = "MashBoxSDK.EditorTools.SculptMode";
        const string UvModePreferenceKey = "MashBoxSDK.EditorTools.UvMode";
        const string LastBrushModePreferenceKey = "MashBoxSDK.EditorTools.LastBrushMode";
        const string LastSplineModePreferenceKey = "MashBoxSDK.EditorTools.LastSplineMode";
        const string CurrentModeOrder = "SplineAfterLoft";
        static bool s_LoftUndoRefreshQueued;

        static MBEditorToolState()
        {
            Undo.undoRedoPerformed += QueueLoftUndoRefresh;
        }

        internal static event Action ModeChanged;
        internal static event Action ActiveEditingChanged;
        internal static event Action BrushModeChanged;
        internal static event Action SculptModeChanged;
        internal static event Action UvModeChanged;
        internal static event Action<MBEditorToolAction> ActionRequested;

        internal static MBEditorAuthoringMode Mode
        {
            get
            {
                int savedMode = GetMigratedModeIndex();
                return Enum.IsDefined(typeof(MBEditorAuthoringMode), savedMode)
                    ? (MBEditorAuthoringMode)savedMode
                    : MBEditorAuthoringMode.Brush;
            }
            set => RequestMode(value);
        }

        internal static MBEditorAuthoringCategory Category => Mode switch
        {
            MBEditorAuthoringMode.Terrain => MBEditorAuthoringCategory.Terrain,
            MBEditorAuthoringMode.SplineLoft => MBEditorAuthoringCategory.Spline,
            MBEditorAuthoringMode.Spline => MBEditorAuthoringCategory.Spline,
            MBEditorAuthoringMode.UVSpline => MBEditorAuthoringCategory.Spline,
            _ => MBEditorAuthoringCategory.Brush
        };

        internal static bool ActiveEditing
        {
            get => EditorPrefs.GetBool(ActiveEditingPreferenceKey, true);
            set
            {
                if (ActiveEditing == value)
                    return;

                EditorPrefs.SetBool(ActiveEditingPreferenceKey, value);
                UVSplineEditor.SceneEditingEnabled = value && Mode == MBEditorAuthoringMode.UVSpline;
                ActiveEditingChanged?.Invoke();
            }
        }

        internal static void RequestMode(MBEditorAuthoringMode mode)
        {
            if (Mode == mode)
                return;

            EditorPrefs.SetInt(ModePreferenceKey, (int)mode);
            EditorPrefs.SetString(ModeOrderPreferenceKey, CurrentModeOrder);
            if (IsSplineMode(mode))
                EditorPrefs.SetInt(LastSplineModePreferenceKey, (int)mode);
            else if (IsBrushMode(mode))
                EditorPrefs.SetInt(LastBrushModePreferenceKey, (int)mode);
            UVSplineEditor.SceneEditingEnabled = ActiveEditing && mode == MBEditorAuthoringMode.UVSpline;
            ModeChanged?.Invoke();
        }

        internal static void RequestCategory(MBEditorAuthoringCategory category)
        {
            if (Category == category)
                return;

            MBEditorAuthoringMode currentMode = Mode;
            if (IsSplineMode(currentMode))
                EditorPrefs.SetInt(LastSplineModePreferenceKey, (int)currentMode);
            else if (IsBrushMode(currentMode))
                EditorPrefs.SetInt(LastBrushModePreferenceKey, (int)currentMode);

            if (category == MBEditorAuthoringCategory.Terrain)
            {
                RequestMode(MBEditorAuthoringMode.Terrain);
                return;
            }

            bool splineCategory = category == MBEditorAuthoringCategory.Spline;
            string preferenceKey = splineCategory ? LastSplineModePreferenceKey : LastBrushModePreferenceKey;
            MBEditorAuthoringMode fallback = splineCategory
                ? MBEditorAuthoringMode.SplineLoft
                : MBEditorAuthoringMode.Brush;
            MBEditorAuthoringMode requestedMode = (MBEditorAuthoringMode)EditorPrefs.GetInt(
                preferenceKey,
                (int)fallback);

            if (splineCategory != IsSplineMode(requestedMode))
                requestedMode = fallback;

            RequestMode(requestedMode);
        }

        internal static bool IsBrushMode(MBEditorAuthoringMode mode)
        {
            return mode == MBEditorAuthoringMode.Brush
                || mode == MBEditorAuthoringMode.MeshSculpt;
        }

        internal static bool IsSplineMode(MBEditorAuthoringMode mode)
        {
            return mode == MBEditorAuthoringMode.SplineLoft
                || mode == MBEditorAuthoringMode.Spline
                || mode == MBEditorAuthoringMode.UVSpline;
        }

        internal static MBBrushMode BrushMode
        {
            get => (MBBrushMode)EditorPrefs.GetInt(BrushModePreferenceKey, (int)MBBrushMode.Decor);
            set
            {
                if (BrushMode == value) return;
                EditorPrefs.SetInt(BrushModePreferenceKey, (int)value);
                BrushModeChanged?.Invoke();
            }
        }

        internal static MBSculptMode SculptMode
        {
            get => (MBSculptMode)EditorPrefs.GetInt(SculptModePreferenceKey, (int)MBSculptMode.Displace);
            set
            {
                if (SculptMode == value) return;
                EditorPrefs.SetInt(SculptModePreferenceKey, (int)value);
                SculptModeChanged?.Invoke();
            }
        }

        internal static MBUvHandleMode UvMode
        {
            get => (MBUvHandleMode)EditorPrefs.GetInt(UvModePreferenceKey, (int)MBUvHandleMode.MoveAndUv);
            set
            {
                if (UvMode == value) return;
                EditorPrefs.SetInt(UvModePreferenceKey, (int)value);
                UvModeChanged?.Invoke();
            }
        }

        internal static void RequestAction(MBEditorToolAction action)
        {
            if (ActiveEditing)
                ActionRequested?.Invoke(action);
        }

        static void QueueLoftUndoRefresh()
        {
            if (!IsSplineMode(Mode) || s_LoftUndoRefreshQueued)
                return;

            s_LoftUndoRefreshQueued = true;
            EditorApplication.delayCall -= RefreshLoftsAfterUndo;
            EditorApplication.delayCall += RefreshLoftsAfterUndo;
        }

        static void RefreshLoftsAfterUndo()
        {
            EditorApplication.delayCall -= RefreshLoftsAfterUndo;
            s_LoftUndoRefreshQueued = false;

            MultiSplineLoft[] lofts = UnityEngine.Object.FindObjectsByType<MultiSplineLoft>(
                UnityEngine.FindObjectsInactive.Include,
                UnityEngine.FindObjectsSortMode.None);
            for (int i = 0; i < lofts.Length; i++)
            {
                MultiSplineLoft loft = lofts[i];
                if (loft == null || EditorUtility.IsPersistent(loft) || !loft.gameObject.scene.IsValid())
                    continue;

                try
                {
                    loft.Regenerate();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception, loft);
                }
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        static int GetMigratedModeIndex()
        {
            int savedMode = EditorPrefs.GetInt(ModePreferenceKey, (int)MBEditorAuthoringMode.Brush);
            string savedOrder = EditorPrefs.GetString(ModeOrderPreferenceKey, string.Empty);
            if (string.Equals(savedOrder, CurrentModeOrder, StringComparison.Ordinal))
                return savedMode;

            // Spline was inserted after Spline Loft. Preserve the meaning of
            // previously saved Mesh Sculpt, UV Spline, and Terrain indices.
            if (savedMode >= 2)
                savedMode++;

            EditorPrefs.SetInt(ModePreferenceKey, savedMode);
            EditorPrefs.SetString(ModeOrderPreferenceKey, CurrentModeOrder);
            return savedMode;
        }
    }
}
