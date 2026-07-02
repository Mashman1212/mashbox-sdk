#if UNITY_EDITOR
using System.Collections.Generic;
using MashBoxSDK.Maps.Rigging;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MashBoxSDK.EditorResources
{
    internal abstract class MashBoxRiggingInspectorBase : UnityEditor.Editor
    {
        protected abstract string Description { get; }
        protected virtual string SetupNotes => null;

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();

            if (!string.IsNullOrWhiteSpace(Description))
                EditorGUILayout.HelpBox(Description, MessageType.None);

            if (!string.IsNullOrWhiteSpace(SetupNotes))
                EditorGUILayout.HelpBox(SetupNotes, MessageType.Info);

            DrawInspectorBody();
        }

        protected virtual void DrawInspectorBody()
        {
            DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(MBTriggerRelay))]
    internal sealed class MBTriggerRelayInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Trigger Relay is deprecated. Use Trigger Zone instead. This legacy component remains only so older scenes keep working.";

        protected override string SetupNotes =>
            "Setup: replace this with Trigger Zone on new content. Existing scenes can keep using it until you swap them over.";
    }

    [CustomEditor(typeof(MBTriggerZone))]
    internal sealed class MBTriggerZoneInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Trigger Zone is a simple enter/exit event volume for level scripting. It tracks first enter and last exit, but intentionally skips stay events for performance.";

        protected override string SetupNotes =>
            "Setup: this component expects a trigger collider. A Rigidbody must exist on either this object hierarchy or the entering object for trigger callbacks to fire.";
    }

    [CustomEditor(typeof(MBCollisionEvents))]
    internal sealed class MBCollisionEventsInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Collision Events turns collision enter and exit into UnityEvents for bumpers, impact switches, breakables, and physical interaction logic.";

        protected override string SetupNotes =>
            "Setup: use a non-trigger collider. Unity collision callbacks need a Rigidbody on at least one of the colliding objects.";
    }

    [CustomEditor(typeof(MBEventTrigger))]
    internal sealed class MBEventTriggerInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Event Trigger is a manual signal node. Call Trigger() from another event, then use its output events to fan that signal out to other objects.";
    }

    [CustomEditor(typeof(MBCounter))]
    internal sealed class MBCounterInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Counter tracks a running integer value and fires UnityEvents when it changes, reaches a target, becomes positive, or returns to zero.";
    }

    [CustomEditor(typeof(MBNetworkedEvent))]
    internal sealed class MBNetworkedEventInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Networked Event sends creator-authored signals through the active multiplayer session. Use Raise for live one-shot moments, or Set State when late joiners should receive the latest value.";

        protected override string SetupNotes =>
            "Setup: keep the Network Key stable and unique for each synced map object. Wire local triggers to Raise or Set State, then wire the received events to doors, switches, effects, counters, or other scene logic.";
    }

    [CustomEditor(typeof(MBNetworkedObjectSpawner))]
    internal sealed class MBNetworkedObjectSpawnerInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Networked Object Spawner asks MashBox to spawn a registered network prefab for the whole multiplayer session.";

        protected override string SetupNotes =>
            "Setup: use a Spawn Key that MashBox has registered, such as Drift Car 2. Wire Spawn() from triggers, animator events, buttons, or other map logic.";

        protected override void DrawInspectorBody()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnKey"), new GUIContent("Spawn Key"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnPoint"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localOffset"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("snapToGround"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("warnWhenUnavailable"));

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onSpawnRequested"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onSpawnUnavailable"));

            serializedObject.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Spawn"))
                {
                    foreach (Object selectedTarget in targets)
                    {
                        if (selectedTarget is MBNetworkedObjectSpawner spawner)
                            spawner.Spawn();
                    }
                }
            }
        }
    }

    [CustomEditor(typeof(MBAnimatorStateEvents))]
    internal sealed class MBAnimatorStateEventsInspector : MashBoxRiggingInspectorBase
    {
        private struct AnimatorStateOption
        {
            public string DisplayName;
            public string StateName;
            public string StatePath;
        }

        protected override string Description =>
            "Animator State Events watches selected Animator states and fires UnityEvents when they enter, exit, or reach a normalized time. It is useful for wiring animation ends back into map logic.";

        protected override string SetupNotes =>
            "Setup: add this beside an Animator, add watched states, then wire the normalized-time event to things like Networked Event Set True State when an open animation finishes.";

        protected override void DrawInspectorBody()
        {
            serializedObject.Update();

            SerializedProperty animatorProperty = serializedObject.FindProperty("animator");
            SerializedProperty invokeCurrentStateOnEnableProperty = serializedObject.FindProperty("invokeCurrentStateOnEnable");
            SerializedProperty defaultCrossFadeDurationProperty = serializedObject.FindProperty("defaultCrossFadeDuration");
            SerializedProperty stateEventsProperty = serializedObject.FindProperty("stateEvents");

            EditorGUILayout.PropertyField(animatorProperty);
            EditorGUILayout.PropertyField(invokeCurrentStateOnEnableProperty);
            EditorGUILayout.PropertyField(defaultCrossFadeDurationProperty);

            Animator animator = ResolveAnimator(animatorProperty);
            if (animator == null)
                EditorGUILayout.HelpBox("Assign an Animator or place this component on the same object as an Animator to enable state dropdowns.", MessageType.Info);
            else if (ResolveAnimatorController(animator) == null)
                EditorGUILayout.HelpBox("The assigned Animator does not use an Animator Controller that exposes selectable states.", MessageType.Info);

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Watched States", EditorStyles.boldLabel);

            for (int index = 0; index < stateEventsProperty.arraySize; index++)
            {
                SerializedProperty entryProperty = stateEventsProperty.GetArrayElementAtIndex(index);
                DrawStateEntry(entryProperty, animator, index);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add State"))
                    stateEventsProperty.InsertArrayElementAtIndex(stateEventsProperty.arraySize);

                using (new EditorGUI.DisabledScope(stateEventsProperty.arraySize == 0))
                {
                    if (GUILayout.Button("Remove Last"))
                        stateEventsProperty.DeleteArrayElementAtIndex(stateEventsProperty.arraySize - 1);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private Animator ResolveAnimator(SerializedProperty animatorProperty)
        {
            if (animatorProperty.objectReferenceValue is Animator assignedAnimator)
                return assignedAnimator;

            if (target is MBAnimatorStateEvents stateEvents)
                return stateEvents.GetComponent<Animator>();

            return null;
        }

        private static void DrawStateEntry(SerializedProperty entryProperty, Animator animator, int index)
        {
            SerializedProperty layerIndexProperty = entryProperty.FindPropertyRelative("layerIndex");
            SerializedProperty stateNameProperty = entryProperty.FindPropertyRelative("stateName");
            SerializedProperty statePathProperty = entryProperty.FindPropertyRelative("statePath");
            SerializedProperty normalizedTimeProperty = entryProperty.FindPropertyRelative("normalizedTime");
            SerializedProperty invokeAtNormalizedTimeProperty = entryProperty.FindPropertyRelative("invokeAtNormalizedTime");
            SerializedProperty invokeEveryLoopProperty = entryProperty.FindPropertyRelative("invokeEveryLoop");
            SerializedProperty onStateEnteredProperty = entryProperty.FindPropertyRelative("onStateEntered");
            SerializedProperty onStateExitedProperty = entryProperty.FindPropertyRelative("onStateExited");
            SerializedProperty onNormalizedTimeReachedProperty = entryProperty.FindPropertyRelative("onNormalizedTimeReached");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"State {index}", EditorStyles.boldLabel);

                DrawLayerAndStateSelectors(animator, layerIndexProperty, stateNameProperty, statePathProperty);

                EditorGUILayout.PropertyField(onStateEnteredProperty, new GUIContent("On State Entered"));
                EditorGUILayout.PropertyField(onStateExitedProperty, new GUIContent("On State Exited"));

                GUILayout.Space(2f);
                EditorGUILayout.PropertyField(invokeAtNormalizedTimeProperty, new GUIContent("Use Normalized Time Event"));

                if (invokeAtNormalizedTimeProperty.boolValue)
                {
                    EditorGUILayout.PropertyField(normalizedTimeProperty, new GUIContent("Normalized Time"));
                    EditorGUILayout.PropertyField(invokeEveryLoopProperty);
                    EditorGUILayout.PropertyField(onNormalizedTimeReachedProperty);
                }
            }
        }

        private static void DrawLayerAndStateSelectors(
            Animator animator,
            SerializedProperty layerIndexProperty,
            SerializedProperty stateNameProperty,
            SerializedProperty statePathProperty)
        {
            AnimatorController controller = ResolveAnimatorController(animator);
            if (animator == null || controller == null || controller.layers.Length == 0)
            {
                EditorGUILayout.PropertyField(layerIndexProperty);
                EditorGUILayout.PropertyField(stateNameProperty);
                EditorGUILayout.PropertyField(statePathProperty);
                return;
            }

            string[] layerNames = new string[controller.layers.Length];
            for (int index = 0; index < controller.layers.Length; index++)
                layerNames[index] = controller.layers[index].name;

            layerIndexProperty.intValue = Mathf.Clamp(layerIndexProperty.intValue, 0, controller.layers.Length - 1);
            layerIndexProperty.intValue = EditorGUILayout.Popup("Layer", layerIndexProperty.intValue, layerNames);

            List<AnimatorStateOption> states = GetStates(controller, layerIndexProperty.intValue);
            if (states.Count == 0)
            {
                EditorGUILayout.HelpBox("No states found on this Animator layer.", MessageType.Info);
                return;
            }

            string[] displayOptions = new string[states.Count + 1];
            displayOptions[0] = "None";
            for (int index = 0; index < states.Count; index++)
                displayOptions[index + 1] = states[index].DisplayName;

            int selectedIndex = FindSelectedStateIndex(states, stateNameProperty.stringValue, statePathProperty.stringValue);
            int newSelectedIndex = EditorGUILayout.Popup("State", selectedIndex, displayOptions);
            if (newSelectedIndex == 0)
            {
                stateNameProperty.stringValue = string.Empty;
                statePathProperty.stringValue = string.Empty;
                return;
            }

            AnimatorStateOption selected = states[newSelectedIndex - 1];
            stateNameProperty.stringValue = selected.StateName;
            statePathProperty.stringValue = selected.StatePath;
        }

        private static int FindSelectedStateIndex(List<AnimatorStateOption> states, string stateName, string statePath)
        {
            for (int index = 0; index < states.Count; index++)
            {
                AnimatorStateOption state = states[index];
                if (!string.IsNullOrEmpty(statePath) && state.StatePath == statePath)
                    return index + 1;

                if (!string.IsNullOrEmpty(stateName) && state.StateName == stateName)
                    return index + 1;
            }

            return 0;
        }

        private static AnimatorController ResolveAnimatorController(Animator animator)
        {
            if (animator == null)
                return null;

            RuntimeAnimatorController runtimeController = animator.runtimeAnimatorController;
            if (runtimeController is AnimatorOverrideController overrideController)
                runtimeController = overrideController.runtimeAnimatorController;

            return runtimeController as AnimatorController;
        }

        private static List<AnimatorStateOption> GetStates(AnimatorController controller, int layerIndex)
        {
            List<AnimatorStateOption> states = new List<AnimatorStateOption>();
            if (controller == null || layerIndex < 0 || layerIndex >= controller.layers.Length)
                return states;

            AnimatorControllerLayer layer = controller.layers[layerIndex];
            CollectStates(layer.stateMachine, layer.name, string.Empty, states);
            return states;
        }

        private static void CollectStates(
            AnimatorStateMachine stateMachine,
            string layerName,
            string stateMachinePath,
            List<AnimatorStateOption> states)
        {
            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                string displayName = string.IsNullOrEmpty(stateMachinePath)
                    ? childState.state.name
                    : $"{stateMachinePath}/{childState.state.name}";

                string pathPart = string.IsNullOrEmpty(stateMachinePath)
                    ? childState.state.name
                    : $"{stateMachinePath}.{childState.state.name}";

                states.Add(new AnimatorStateOption
                {
                    DisplayName = displayName,
                    StateName = childState.state.name,
                    StatePath = $"{layerName}.{pathPart}"
                });
            }

            foreach (ChildAnimatorStateMachine childMachine in stateMachine.stateMachines)
            {
                string childPath = string.IsNullOrEmpty(stateMachinePath)
                    ? childMachine.stateMachine.name
                    : $"{stateMachinePath}.{childMachine.stateMachine.name}";

                CollectStates(childMachine.stateMachine, layerName, childPath, states);
            }
        }
    }

    [CustomEditor(typeof(MBTimer))]
    internal sealed class MBTimerInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Timer runs a countdown or count-up and exposes start, pause, resume, stop, and completion events so designers can chain time-based logic.";
    }

    [CustomEditor(typeof(MBEventSequence))]
    internal sealed class MBEventSequenceInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Event Sequence plays a list of delayed steps in order. Use it for simple scripted moments, chained reveals, or lightweight timeline-style behavior.";
    }

    [CustomEditor(typeof(MBBoolState))]
    internal sealed class MBBoolStateInspector : MashBoxRiggingInspectorBase
    {
        protected override string Description =>
            "Bool State stores a simple on/off value and fires events when it changes. It works well for switches, toggles, locks, and conditional scene logic.";
    }
}
#endif
