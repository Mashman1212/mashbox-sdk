#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public static class MBChallengeAuthoring
    {
        public static MBTrickChallenge Create(bool line, Transform parent, Vector3 position)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create " + (line ? "Line" : "Spot") + " Challenge");
            var obj = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent, line ? "Line Challenge" : "Spot Challenge"));
            Undo.RegisterCreatedObjectUndo(obj, "Create Challenge");
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            MBTrickChallenge challenge = line ? (MBTrickChallenge)Undo.AddComponent<MBLineChallenge>(obj) : Undo.AddComponent<MBSpotChallenge>(obj);
            challenge.ChallengeName = obj.name;
            Undo.AddComponent<MBChallengeBridge>(obj);
            int steps = line ? 3 : 1;
            for (int i = 0; i < steps; i++) AddZone(challenge);
            AddStarterTiers(challenge);
            EditorUtility.SetDirty(challenge);
            EditorSceneManager.MarkSceneDirty(obj.scene);
            Selection.activeGameObject = obj;
            Undo.CollapseUndoOperations(group);
            return challenge;
        }

        public static void AddZone(MBTrickChallenge challenge)
        {
            Undo.RecordObject(challenge, "Add Challenge Zone");
            int index = challenge.Zones.Count;
            var obj = new GameObject(challenge.IsLine ? $"Step {index + 1:00}" : $"Spot Zone {index + 1:00}");
            Undo.RegisterCreatedObjectUndo(obj, "Add Challenge Zone");
            obj.transform.SetParent(challenge.transform, false);
            obj.transform.localPosition = new Vector3(0, 1.5f, index * 8f);
            var zone = Undo.AddComponent<MBChallengeZone>(obj);
            var box = obj.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(5, 4, 5);
            challenge.Zones.Add(zone);
            // Every Line tier gets a valid, editable objective for the new step.
            if (challenge.IsLine)
                foreach (var tier in challenge.Tiers)
                    tier.goals.Add(TrickGoal(index, "180"));
            EditorUtility.SetDirty(challenge);
            EditorSceneManager.MarkSceneDirty(challenge.gameObject.scene);
            Selection.activeGameObject = obj;
        }

        public static void AddStarterTiers(MBTrickChallenge challenge)
        {
            if (challenge.Tiers.Count != 0) return;
            Undo.RecordObject(challenge, "Add Challenge Tiers");
            string[] titles = { "Basic", "Am", "Pro", "Legendary" };
            int steps = challenge.IsLine ? challenge.Zones.Count : 1;
            for (int t = 0; t < titles.Length; t++)
            {
                var tier = new MBChallengeTier { title = titles[t] };
                for (int step = 0; step < steps; step++)
                {
                    if (t == 1)
                        tier.goals.Add(new MBChallengeGoal { label = "Land 200 points", kind = MBChallengeGoalKind.Score, target = 200, stepIndex = step, accumulation = MBChallengeAccumulation.Best });
                    else
                    {
                        tier.goals.Add(TrickGoal(step, t == 0 ? "180" : "Barspin"));
                        if (t >= 2) tier.goals.Add(TrickGoal(step, "360"));
                        if (t == 3) tier.goals.Add(new MBChallengeGoal { label = "Land 500 points", kind = MBChallengeGoalKind.Score, target = 500, stepIndex = step, accumulation = MBChallengeAccumulation.Best });
                    }
                }
                challenge.Tiers.Add(tier);
            }
            EditorUtility.SetDirty(challenge);
        }

        private static MBChallengeGoal TrickGoal(int step, string trick) => new MBChallengeGoal
        {
            label = "Land " + trick, stepIndex = step, tricks = new List<string> { trick }
        };

        public static void AddMapTask(MBTrickChallenge challenge)
        {
            var scene = challenge.gameObject.scene;
            var list = MBMapTaskList.FindInScene(scene);
            if (list == null)
            {
                var obj = new GameObject(MBMapTaskList.RootName);
                Undo.RegisterCreatedObjectUndo(obj, "Create Map Tasks");
                SceneManager.MoveGameObjectToScene(obj, scene);
                var root = scene.GetRootGameObjects().FirstOrDefault(value => value.name == "Challenges");
                if (root != null) obj.transform.SetParent(root.transform, false);
                list = Undo.AddComponent<MBMapTaskList>(obj);
                list.Tasks.Clear();
            }
            Undo.RecordObject(list, "Add Challenge Map Task");
            string tier = challenge.Tiers.Count > challenge.SelectedTier ? challenge.Tiers[challenge.SelectedTier].title : "Basic";
            list.Tasks.Add(new MBMapTaskDefinition
            {
                displayName = $"Complete {tier} at {challenge.ChallengeName}",
                verb = "Completed", preposition = challenge.IsLine ? "Line Challenge" : "Spot Challenge",
                adjective = challenge.ChallengeName + " " + tier, targetCount = 1
            });
            EditorUtility.SetDirty(list);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = list.gameObject;
        }
    }

    [CustomEditor(typeof(MBTrickChallenge), true)]
    public class MBTrickChallengeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var challenge = (MBTrickChallenge)target;
            serializedObject.Update();
            EditorGUILayout.LabelField(challenge.IsLine ? "Line Challenge" : "Spot Challenge", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("challengeId"));
            foreach (string field in new[] { "challengeName", "description", "startMode", "retryOnZoneEntry", "rules", "zones", "tiers" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field), true);
            if (challenge.Tiers.Count > 0)
            {
                var selected = serializedObject.FindProperty("selectedTier");
                selected.intValue = EditorGUILayout.Popup("Active Tier", selected.intValue, challenge.Tiers.Select(tier => tier?.title ?? "Missing").ToArray());
            }
            EditorGUILayout.Space();
            foreach (string field in new[] { "onStarted", "onProgressChanged", "onStepChanged", "onTierCompleted", "onFailed", "onReset" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            serializedObject.ApplyModifiedProperties();
            foreach (string error in challenge.GetValidationErrors()) EditorGUILayout.HelpBox(error, MessageType.Error);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(challenge.IsLine ? "Add Step Zone" : "Add Spot Zone")) MBChallengeAuthoring.AddZone(challenge);
                if (GUILayout.Button("Preview Rules")) MBChallengePreview.Open(challenge);
            }
            if (challenge.Tiers.Count == 0 && GUILayout.Button("Add Starter Tiers")) MBChallengeAuthoring.AddStarterTiers(challenge);
            if (GUILayout.Button("Add Tier"))
            {
                Undo.RecordObject(challenge, "Add Challenge Tier");
                var tier = challenge.Tiers.Count > 0 && challenge.Tiers[challenge.SelectedTier] != null
                    ? JsonUtility.FromJson<MBChallengeTier>(JsonUtility.ToJson(challenge.Tiers[challenge.SelectedTier]))
                    : new MBChallengeTier { goals = new List<MBChallengeGoal> { new MBChallengeGoal() } };
                tier.id = Guid.NewGuid().ToString("N");
                tier.title = "New Tier";
                challenge.Tiers.Add(tier);
                EditorUtility.SetDirty(challenge);
            }
            if (GUILayout.Button("Repair Duplicated Tier IDs"))
            {
                Undo.RecordObject(challenge, "Repair Tier IDs");
                challenge.RepairTierIdentities();
                EditorUtility.SetDirty(challenge);
            }
            if (GUILayout.Button("Add Active Tier to Map Tasks")) MBChallengeAuthoring.AddMapTask(challenge);
            if (GUILayout.Button("New Identity for Duplicated Challenge"))
            {
                Undo.RecordObject(challenge, "Regenerate Challenge Identity");
                challenge.RegenerateIdentity();
                EditorUtility.SetDirty(challenge);
            }
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Attempt", challenge.State.ToString());
                if (GUILayout.Button("Start / Retry Active Tier")) challenge.StartChallenge();
                if (GUILayout.Button("Reset Attempt")) challenge.ResetAttempt();
            }
        }
    }

    [CustomPropertyDrawer(typeof(MBChallengeGoal))]
    public class MBChallengeGoalDrawer : PropertyDrawer
    {
        private static IEnumerable<string> Fields(SerializedProperty property)
        {
            yield return "label"; yield return "kind"; yield return "stepIndex"; yield return "target"; yield return "accumulation";
            var kind = (MBChallengeGoalKind)property.FindPropertyRelative("kind").enumValueIndex;
            if (kind == MBChallengeGoalKind.TrickCombo) { yield return "match"; yield return "tricks"; }
            if (kind == MBChallengeGoalKind.CustomSignal) yield return "signal";
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + 4;
            if (property.isExpanded)
                foreach (string field in Fields(property)) height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + 3;
            return height;
        }
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);
            rect.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, property.FindPropertyRelative("label").stringValue, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (string field in Fields(property))
                {
                    rect.y += rect.height + 3;
                    var child = property.FindPropertyRelative(field);
                    rect.height = EditorGUI.GetPropertyHeight(child, true);
                    if (field == "stepIndex" && property.serializedObject.targetObject is MBTrickChallenge challenge)
                    {
                        string[] choices = challenge.IsLine
                            ? challenge.Zones.Select((zone, index) => $"{index + 1}. {(zone != null ? zone.name : "Missing Zone")}").ToArray()
                            : new[] { "Spot" };
                        child.intValue = EditorGUI.Popup(rect, "Step", child.intValue, choices);
                    }
                    else EditorGUI.PropertyField(rect, child, new GUIContent(ObjectNames.NicifyVariableName(field)), true);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(MBChallengeTier))]
    public class MBChallengeTierDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + 4 + (property.isExpanded
                ? EditorGUIUtility.singleLineHeight + 4 + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("goals"), true) + 4 : 0);
        }
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);
            rect.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, property.FindPropertyRelative("title").stringValue, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                rect.y += rect.height + 4;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative("title"));
                rect.y += rect.height + 4;
                rect.height = EditorGUI.GetPropertyHeight(property.FindPropertyRelative("goals"), true);
                EditorGUI.PropertyField(rect, property.FindPropertyRelative("goals"), true);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
#endif
