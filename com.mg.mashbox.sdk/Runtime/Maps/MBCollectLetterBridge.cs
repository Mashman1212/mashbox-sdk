using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBCollectLetterBridge : MonoBehaviour
    {
        [Tooltip("The SDK letter proxy this spawned object should drive. If left empty, the bridge looks up the hierarchy.")]
        [SerializeField] private MBCollectLetter letter;

        [Header("Events")]
        [SerializeField] private UnityEvent onBound;
        [SerializeField] private UnityEvent onCollected;
        [SerializeField] private UnityEvent onReset;

        public MBCollectLetter Letter => letter;

        private void Awake()
        {
            Rebind();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnTransformParentChanged()
        {
            Rebind();
        }

        public void Rebind()
        {
            Unsubscribe();

            if (letter == null)
                letter = GetComponentInParent<MBCollectLetter>();

            Subscribe();

            if (letter != null)
                onBound?.Invoke();
        }

        public void Bind(MBCollectLetter target)
        {
            if (letter == target)
                return;

            Unsubscribe();
            letter = target;
            Subscribe();

            if (letter != null)
                onBound?.Invoke();
        }

        public void Collect()
        {
            letter?.Collect();
        }

        public void ResetCollected()
        {
            letter?.ResetCollected();
        }

        private void Subscribe()
        {
            if (letter == null)
                return;

            letter.Collected -= HandleCollected;
            letter.ResetOccurred -= HandleReset;
            letter.Collected += HandleCollected;
            letter.ResetOccurred += HandleReset;
        }

        private void Unsubscribe()
        {
            if (letter == null)
                return;

            letter.Collected -= HandleCollected;
            letter.ResetOccurred -= HandleReset;
        }

        private void HandleCollected()
        {
            onCollected?.Invoke();
        }

        private void HandleReset()
        {
            onReset?.Invoke();
        }
    }
}
