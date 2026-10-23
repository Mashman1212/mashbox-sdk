using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using NativeUndo = UnityEditor.Undo;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static class StudioUndo
    {
        private const string SessionKey = "MashBox.Studio.Undo.Actions.v1";
        [Serializable] private sealed class SavedAction
        {
            public int group;
            public bool applied = true;
            public List<int> groups = new List<int>(), tokens = new List<int>(), objects = new List<int>();
        }
        [Serializable] private sealed class SavedHistory { public List<SavedAction> actions = new List<SavedAction>(); }
        private sealed class ActionRecord
        {
            internal int group;
            internal bool applied = true;
            internal readonly HashSet<int> groups = new HashSet<int>(), objects = new HashSet<int>();
            internal readonly List<StudioUndoToken> tokens = new List<StudioUndoToken>();
        }
        private static readonly List<ActionRecord> actions = new List<ActionRecord>();
        private static bool trimQueued;
        private static int namedGroup = -1;
        private static string groupName;
        static StudioUndo()
        {
            RestoreSession(); NativeUndo.undoRedoEvent += TrackHistory;
            NativeUndo.willFlushUndoRecord += QueueTrim; QueueTrim();
        }
        internal static event NativeUndo.UndoRedoCallback undoRedoPerformed
        { add { NativeUndo.undoRedoPerformed += value; } remove { NativeUndo.undoRedoPerformed -= value; } }
        internal static event NativeUndo.UndoRedoEventCallback undoRedoEvent
        { add { NativeUndo.undoRedoEvent += value; } remove { NativeUndo.undoRedoEvent -= value; } }
        internal static int GetCurrentGroup() => NativeUndo.GetCurrentGroup();
        internal static void SetCurrentGroupName(string name)
        {
            // A native group-name marker survives ClearUndo(identifier) as an
            // empty Undo step. Supply the name on the actual keyed record instead.
            namedGroup = GetCurrentGroup(); groupName = name;
        }
        internal static void IncrementCurrentGroup() => NativeUndo.IncrementCurrentGroup();
        internal static void RecordObject(Object target, string name) => RegisterCompleteObjectUndo(target, name);
        internal static void RegisterCompleteObjectUndo(Object target, string name) => RegisterCompleteObjectUndo(new[] { target }, name);
        internal static void RegisterCompleteObjectUndo(Object[] targets, string name)
        {
            var objects = targets.Where(o => o).Distinct().ToArray();
            if (objects.Length == 0) return;
            // Native recording also abandons the redo branch when a new edit starts.
            foreach (var old in actions.Where(a => !a.applied).ToArray()) Release(old);
            int group = GetCurrentGroup();
            if (group == namedGroup && !string.IsNullOrEmpty(groupName)) name = groupName;
            var action = actions.LastOrDefault(a => a.group == group && a.applied);
            var fresh = objects.Where(o => action == null || !action.objects.Contains(o.GetInstanceID())).ToArray();
            if (fresh.Length == 0) return;
            if (action == null)
            {
                action = new ActionRecord { group = group }; action.groups.Add(group);
                var token = ScriptableObject.CreateInstance<StudioUndoToken>();
                token.name = "Studio Undo: " + name; token.hideFlags = HideFlags.HideAndDontSave;
                action.tokens.Add(token); actions.Add(action);
            }
            var recorded = new Object[fresh.Length + 1]; recorded[0] = action.tokens[0]; Array.Copy(fresh, 0, recorded, 1, fresh.Length);
            try { NativeUndo.RegisterCompleteObjectUndo(recorded, name); }
            catch
            {
                if (action.objects.Count == 0) Release(action);
                SaveSession(); throw;
            }
            foreach (var item in fresh) action.objects.Add(item.GetInstanceID());
            SaveSession(); QueueTrim();
        }
        internal static void CollapseUndoOperations(int group)
        {
            NativeUndo.CollapseUndoOperations(group);
            var merged = actions.Where(a => a.applied && a.group >= group).ToArray();
            if (merged.Length > 0)
            {
                var first = merged[0]; first.group = group; first.groups.Add(group);
                foreach (var other in merged.Skip(1))
                {
                    first.groups.UnionWith(other.groups); first.objects.UnionWith(other.objects);
                    first.tokens.AddRange(other.tokens); actions.Remove(other);
                }
            }
            SaveSession(); EnforceLimit();
        }
        internal static void RevertAllDownToGroup(int group)
        {
            NativeUndo.RevertAllDownToGroup(group);
            foreach (var action in actions.Where(a => a.group >= group).ToArray()) Release(action);
            SaveSession();
        }
        internal static void PerformUndo() => NativeUndo.PerformUndo();
        internal static void PerformRedo() => NativeUndo.PerformRedo();
        internal static void ClearUndo(Object target)
        {
            if (!target) return;
            int id = target.GetInstanceID();
            foreach (var action in actions.Where(a => a.objects.Contains(id)).ToArray()) Release(action);
            NativeUndo.ClearUndo(target); SaveSession();
        }
        private static void TrackHistory(in UndoRedoInfo info)
        {
            int group = info.undoGroup;
            var action = actions.LastOrDefault(a => a.groups.Contains(group));
            if (action != null) { action.applied = info.isRedo; SaveSession(); }
        }
        private static void Release(ActionRecord action)
        {
            foreach (var token in action.tokens)
                if (token) { NativeUndo.ClearUndo(token); Object.DestroyImmediate(token); }
            actions.Remove(action);
        }
        private static void QueueTrim()
        {
            if (trimQueued || actions.Count <= StudioSettings.Current.undoLimit) return;
            trimQueued = true; EditorApplication.delayCall += RunQueuedTrim;
        }
        private static void RunQueuedTrim()
        { trimQueued = false; EnforceLimit(); }
        internal static void EnforceLimit()
        {
            // Never evict part of a still-active drag; Collapse or the native
            // mouse-up flush schedules enforcement once the action is complete.
            if (GUIUtility.hotControl != 0) return;
            int maximum = Math.Max(1, StudioSettings.Current.undoLimit);
            while (actions.Count > maximum) Release(actions[0]);
            SaveSession();
        }
        private static void SaveSession()
        {
            var data = new SavedHistory();
            foreach (var action in actions)
                data.actions.Add(new SavedAction { group = action.group, applied = action.applied,
                    groups = action.groups.ToList(), objects = action.objects.ToList(),
                    tokens = action.tokens.Where(t => t).Select(t => t.GetInstanceID()).ToList() });
            SessionState.SetString(SessionKey, JsonUtility.ToJson(data));
        }
        private static void RestoreSession()
        {
            try
            {
                var data = JsonUtility.FromJson<SavedHistory>(SessionState.GetString(SessionKey, ""));
                if (data?.actions == null) return;
                foreach (var saved in data.actions)
                {
                    var action = new ActionRecord { group = saved.group, applied = saved.applied };
                    foreach (int id in saved.tokens ?? new List<int>())
                        if (EditorUtility.InstanceIDToObject(id) is StudioUndoToken token) action.tokens.Add(token);
                    if (action.tokens.Count == 0) continue;
                    action.groups.UnionWith(saved.groups ?? new List<int>()); action.objects.UnionWith(saved.objects ?? new List<int>());
                    actions.Add(action);
                }
            }
            catch (ArgumentException) { SessionState.EraseString(SessionKey); }
        }
#if MASHBOX_STUDIO_TESTS
        internal static int RetainedActions => actions.Count;
        internal static int RetainedTokens => actions.Sum(a => a.tokens.Count);
        internal static void ReloadSessionForTests() { actions.Clear(); RestoreSession(); }
        internal static void ClearForTests()
        {
            foreach (var action in actions.ToArray()) Release(action);
            SaveSession();
        }
#endif
    }
}
