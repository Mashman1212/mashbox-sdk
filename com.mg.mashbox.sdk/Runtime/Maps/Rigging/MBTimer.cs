using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [AddComponentMenu("MashBox/Maps/Rigging/Timer")]
    [DisallowMultipleComponent]
    public class MBTimer : MonoBehaviour
    {
        private enum TimerMode
        {
            Countdown,
            CountUp
        }

        [Header("Timing")]
        [Tooltip("Choose whether the timer counts down from the duration or counts up to it.")]
        [SerializeField] private TimerMode timerMode = TimerMode.Countdown;
        [Min(0f)]
        [Tooltip("The total time for the timer in seconds.")]
        [SerializeField] private float duration = 5f;
        [Tooltip("Automatically start the timer when this object becomes enabled.")]
        [SerializeField] private bool autoStart;
        [Tooltip("Ignore Time.timeScale so the timer still runs while the game is paused or slowed.")]
        [SerializeField] private bool useUnscaledTime;

        [Header("Events")]
        [SerializeField] private UnityEvent onStarted;
        [SerializeField] private UnityEvent onPaused;
        [SerializeField] private UnityEvent onResumed;
        [SerializeField] private UnityEvent onStopped;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private MBFloatEvent onTimeChanged;
        [SerializeField] private MBIntEvent onSecondChanged;

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public float CurrentTime { get; private set; }

        private int lastReportedSecond = -1;

        private void Awake()
        {
            duration = Mathf.Max(0f, duration);
            CurrentTime = timerMode == TimerMode.Countdown ? duration : 0f;
        }

        private void OnEnable()
        {
            if (autoStart)
                StartTimer();
        }

        private void Update()
        {
            if (!IsRunning || IsPaused)
                return;

            var deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            if (timerMode == TimerMode.Countdown)
            {
                CurrentTime = Mathf.Max(0f, CurrentTime - deltaTime);
                ReportTime();

                if (CurrentTime <= 0f)
                    Complete();
            }
            else
            {
                CurrentTime += deltaTime;
                ReportTime();

                if (CurrentTime >= duration)
                    Complete();
            }
        }

        public void StartTimer()
        {
            CurrentTime = timerMode == TimerMode.Countdown ? Mathf.Max(0f, duration) : 0f;
            lastReportedSecond = -1;
            IsRunning = true;
            IsPaused = false;

            onStarted?.Invoke();
            ReportTime();

            if (duration <= 0f)
                Complete();
        }

        public void PauseTimer()
        {
            if (!IsRunning || IsPaused)
                return;

            IsPaused = true;
            onPaused?.Invoke();
        }

        public void ResumeTimer()
        {
            if (!IsRunning || !IsPaused)
                return;

            IsPaused = false;
            onResumed?.Invoke();
        }

        public void StopTimer()
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            IsPaused = false;
            onStopped?.Invoke();
        }

        public void ResetTimer()
        {
            IsRunning = false;
            IsPaused = false;
            CurrentTime = timerMode == TimerMode.Countdown ? Mathf.Max(0f, duration) : 0f;
            lastReportedSecond = -1;
            ReportTime();
        }

        public void AddTime(float amount)
        {
            if (timerMode == TimerMode.Countdown)
                CurrentTime = Mathf.Max(0f, CurrentTime + amount);
            else
                CurrentTime = Mathf.Clamp(CurrentTime + amount, 0f, Mathf.Max(0f, duration));

            ReportTime();
        }

        public void SetDuration(float newDuration)
        {
            duration = Mathf.Max(0f, newDuration);

            if (!IsRunning)
                ResetTimer();
        }

        private void OnValidate()
        {
            duration = Mathf.Max(0f, duration);
        }

        private void Complete()
        {
            IsRunning = false;
            IsPaused = false;
            onCompleted?.Invoke();
        }

        private void ReportTime()
        {
            onTimeChanged?.Invoke(CurrentTime);

            var reportedSecond = timerMode == TimerMode.Countdown
                ? Mathf.CeilToInt(CurrentTime)
                : Mathf.FloorToInt(CurrentTime);

            if (reportedSecond == lastReportedSecond)
                return;

            lastReportedSecond = reportedSecond;
            onSecondChanged?.Invoke(reportedSecond);
        }
    }
}
