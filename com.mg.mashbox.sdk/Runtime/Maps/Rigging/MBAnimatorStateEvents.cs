using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Animator State Events")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class MBAnimatorStateEvents : MonoBehaviour
    {
        [Serializable]
        public class AnimatorStateEventEntry
        {
            [SerializeField] private int layerIndex;
            [SerializeField] private string stateName;
            [SerializeField] private string statePath;
            [Range(0f, 1f)]
            [SerializeField] private float normalizedTime = 1f;
            [SerializeField] private bool invokeAtNormalizedTime = true;
            [SerializeField] private bool invokeEveryLoop;

            [Header("Events")]
            [SerializeField] private UnityEvent onStateEntered;
            [SerializeField] private UnityEvent onStateExited;
            [SerializeField] private UnityEvent onNormalizedTimeReached;

            private bool wasInState;
            private bool hasInvokedTime;
            private int lastLoopIndex;

            public int LayerIndex => layerIndex;
            public string StateName => stateName;
            public string StatePath => statePath;
            public bool IsConfigured => !string.IsNullOrWhiteSpace(stateName) || !string.IsNullOrWhiteSpace(statePath);

            internal void ResetRuntimeState(Animator animator)
            {
                wasInState = animator != null && IsCurrentState(animator);
                hasInvokedTime = wasInState;
                lastLoopIndex = wasInState ? GetLoopIndex(animator.GetCurrentAnimatorStateInfo(layerIndex)) : 0;
            }

            internal void Update(Animator animator)
            {
                if (animator == null || !IsConfigured || !IsValidLayer(animator))
                    return;

                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
                bool isInState = MatchesState(stateInfo);

                if (isInState && !wasInState)
                {
                    hasInvokedTime = false;
                    lastLoopIndex = GetLoopIndex(stateInfo);

                    onStateEntered?.Invoke();
                }

                if (isInState)
                    EvaluateNormalizedTime(stateInfo);

                if (!isInState && wasInState)
                {
                    onStateExited?.Invoke();

                    hasInvokedTime = false;
                }

                wasInState = isInState;
            }

            internal void Play(Animator animator, float startNormalizedTime)
            {
                if (animator == null || !IsConfigured)
                    return;

                int layer = IsValidLayer(animator) ? layerIndex : -1;
                string playableStateName = !string.IsNullOrWhiteSpace(statePath) ? statePath : stateName;
                animator.Play(playableStateName, layer, Mathf.Clamp01(startNormalizedTime));
            }

            internal void CrossFade(Animator animator, float transitionDuration, float startNormalizedTime)
            {
                if (animator == null || !IsConfigured)
                    return;

                int layer = IsValidLayer(animator) ? layerIndex : -1;
                string playableStateName = !string.IsNullOrWhiteSpace(statePath) ? statePath : stateName;
                animator.CrossFade(playableStateName, Mathf.Max(0f, transitionDuration), layer, Mathf.Clamp01(startNormalizedTime));
            }

            private void EvaluateNormalizedTime(AnimatorStateInfo stateInfo)
            {
                int loopIndex = GetLoopIndex(stateInfo);
                if (loopIndex != lastLoopIndex && invokeEveryLoop)
                {
                    hasInvokedTime = false;
                    lastLoopIndex = loopIndex;
                }

                if (!invokeAtNormalizedTime || hasInvokedTime)
                    return;

                if (!HasReachedNormalizedTime(stateInfo))
                    return;

                hasInvokedTime = true;
                onNormalizedTimeReached?.Invoke();
            }

            private bool HasReachedNormalizedTime(AnimatorStateInfo stateInfo)
            {
                if (normalizedTime >= 0.999f)
                    return stateInfo.normalizedTime >= 1f;

                float loopProgress = stateInfo.normalizedTime - Mathf.Floor(stateInfo.normalizedTime);
                return loopProgress >= normalizedTime || stateInfo.normalizedTime >= 1f;
            }

            private bool IsCurrentState(Animator animator)
            {
                return IsValidLayer(animator) && MatchesState(animator.GetCurrentAnimatorStateInfo(layerIndex));
            }

            private bool MatchesState(AnimatorStateInfo stateInfo)
            {
                return (!string.IsNullOrWhiteSpace(statePath) && stateInfo.IsName(statePath))
                    || (!string.IsNullOrWhiteSpace(stateName) && stateInfo.IsName(stateName))
                    || (!string.IsNullOrWhiteSpace(stateName) && stateInfo.shortNameHash == Animator.StringToHash(stateName));
            }

            private bool IsValidLayer(Animator animator)
            {
                return layerIndex >= 0 && layerIndex < animator.layerCount;
            }

            private static int GetLoopIndex(AnimatorStateInfo stateInfo)
            {
                return Mathf.Max(0, Mathf.FloorToInt(stateInfo.normalizedTime));
            }
        }

        [SerializeField] private Animator animator;
        [Tooltip("If enabled, enter/time events can fire immediately for the Animator state that is already active when this object is enabled.")]
        [SerializeField] private bool invokeCurrentStateOnEnable;
        [Min(0f)]
        [SerializeField] private float defaultCrossFadeDuration = 0.1f;
        [SerializeField] private List<AnimatorStateEventEntry> stateEvents = new List<AnimatorStateEventEntry>();

        public Animator Animator => animator;
        public int StateEventCount => stateEvents.Count;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            if (invokeCurrentStateOnEnable)
                return;

            foreach (AnimatorStateEventEntry entry in stateEvents)
                entry?.ResetRuntimeState(animator);
        }

        private void Update()
        {
            if (animator == null)
                return;

            foreach (AnimatorStateEventEntry entry in stateEvents)
                entry?.Update(animator);
        }

        public void PlayState(int stateEventIndex)
        {
            PlayState(stateEventIndex, 0f);
        }

        public void PlayState(int stateEventIndex, float startNormalizedTime)
        {
            if (!TryGetEntry(stateEventIndex, out AnimatorStateEventEntry entry))
                return;

            entry.Play(animator, startNormalizedTime);
        }

        public void PlayState(string stateName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName))
                return;

            animator.Play(stateName);
        }

        public void CrossFadeState(int stateEventIndex)
        {
            CrossFadeState(stateEventIndex, 0f);
        }

        public void CrossFadeState(int stateEventIndex, float startNormalizedTime)
        {
            if (!TryGetEntry(stateEventIndex, out AnimatorStateEventEntry entry))
                return;

            entry.CrossFade(animator, defaultCrossFadeDuration, startNormalizedTime);
        }

        public void SnapStateToEnd(int stateEventIndex)
        {
            PlayState(stateEventIndex, 1f);
        }

        private bool TryGetEntry(int stateEventIndex, out AnimatorStateEventEntry entry)
        {
            entry = null;

            if (animator == null)
                animator = GetComponent<Animator>();

            if (stateEventIndex < 0 || stateEventIndex >= stateEvents.Count)
                return false;

            entry = stateEvents[stateEventIndex];
            return entry != null && entry.IsConfigured;
        }
    }
}
