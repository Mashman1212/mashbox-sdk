using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Event Sequence")]
    [DisallowMultipleComponent]
    public class MBEventSequence : MonoBehaviour
    {
        [Serializable]
        private class SequenceStep
        {
            [Tooltip("Optional label to help identify this step in the Inspector.")]
            [SerializeField] private string name = "Step";
            [Min(0f)]
            [Tooltip("How long to wait before firing this step.")]
            [SerializeField] private float delay;
            [SerializeField] private UnityEvent onStep;

            public string Name => name;
            public float Delay => delay;

            public void Invoke()
            {
                onStep?.Invoke();
            }
        }

        [Tooltip("Automatically start the sequence when this object becomes enabled.")]
        [SerializeField] private bool playOnEnable;
        [Tooltip("Ignore Time.timeScale so the sequence still runs while the game is paused or slowed.")]
        [SerializeField] private bool useUnscaledTime;
        [SerializeField] private List<SequenceStep> steps = new List<SequenceStep>();

        [Header("Events")]
        [SerializeField] private UnityEvent onSequenceStarted;
        [SerializeField] private UnityEvent onSequenceStopped;
        [SerializeField] private UnityEvent onSequenceCompleted;
        [SerializeField] private MBIntEvent onStepInvoked;

        private Coroutine playRoutine;

        private void OnEnable()
        {
            if (playOnEnable)
                Play();
        }

        private void OnDisable()
        {
            Stop();
        }

        public void Play()
        {
            Stop();
            playRoutine = StartCoroutine(PlayRoutine());
        }

        public void Restart()
        {
            Play();
        }

        public void Stop()
        {
            if (playRoutine == null)
                return;

            StopCoroutine(playRoutine);
            playRoutine = null;
            onSequenceStopped?.Invoke();
        }

        private IEnumerator PlayRoutine()
        {
            onSequenceStarted?.Invoke();

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                if (step.Delay > 0f)
                {
                    if (useUnscaledTime)
                        yield return new WaitForSecondsRealtime(step.Delay);
                    else
                        yield return new WaitForSeconds(step.Delay);
                }

                step.Invoke();
                onStepInvoked?.Invoke(index);
            }

            playRoutine = null;
            onSequenceCompleted?.Invoke();
        }
    }
}
