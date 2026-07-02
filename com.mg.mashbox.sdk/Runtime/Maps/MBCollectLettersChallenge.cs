using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxSDK.Maps
{
    [Serializable]
    public class MBCollectLetterTypeEvent : UnityEvent<MBCollectLetter.LetterType>
    {
    }

    [DisallowMultipleComponent]
    public class MBCollectLettersChallenge : MonoBehaviour
    {
        [Header("Events")]
        [SerializeField] private MBCollectLetterTypeEvent onLetterCollected;
        [SerializeField] private UnityEvent onAnyProgressChanged;
        [SerializeField] private UnityEvent onCompleted;
        [SerializeField] private UnityEvent onReset;

        private readonly HashSet<MBCollectLetter.LetterType> collectedLetters = new HashSet<MBCollectLetter.LetterType>();
        private bool hasCompleted;

        public int CollectedCount => collectedLetters.Count;
        public int TotalLetters => GetLetters().Count;
        public bool IsComplete => hasCompleted;

        private void Awake()
        {
            ResyncChildren();
        }

        private void OnValidate()
        {
            ResyncChildren();
        }

        private void OnTransformChildrenChanged()
        {
            ResyncChildren();
        }

        public void CollectLetter(MBCollectLetter letter)
        {
            if (letter == null)
                return;

            CollectLetter(letter.Letter);
        }

        public void CollectLetter(MBCollectLetter.LetterType letterType)
        {
            if (!collectedLetters.Add(letterType))
                return;

            onLetterCollected?.Invoke(letterType);
            onAnyProgressChanged?.Invoke();

            if (!hasCompleted && HasCollectedAllLetters())
            {
                hasCompleted = true;
                onCompleted?.Invoke();
            }
        }

        public void ResetChallenge()
        {
            collectedLetters.Clear();
            hasCompleted = false;

            foreach (var letter in GetLetters())
                letter.SetCollectedState(false, notifyGroup: false);

            onReset?.Invoke();
            onAnyProgressChanged?.Invoke();
        }

        public bool HasCollected(MBCollectLetter.LetterType letterType)
        {
            return collectedLetters.Contains(letterType);
        }

        internal void RegisterLetter(MBCollectLetter letter)
        {
            if (letter == null)
                return;

            if (letter.IsCollected)
                collectedLetters.Add(letter.Letter);
            else
                collectedLetters.Remove(letter.Letter);

            hasCompleted = HasCollectedAllLetters();
        }

        private bool HasCollectedAllLetters()
        {
            var letters = GetLetters();
            if (letters.Count == 0)
                return false;

            return letters.All(letter => collectedLetters.Contains(letter.Letter));
        }

        private List<MBCollectLetter> GetLetters()
        {
            return GetComponentsInChildren<MBCollectLetter>(true)
                .OrderBy(letter => letter.transform.GetSiblingIndex())
                .ToList();
        }

        public void ResyncChildren()
        {
            collectedLetters.Clear();
            hasCompleted = false;

            foreach (var letter in GetLetters())
            {
                letter.AssignChallenge(this);
                RegisterLetter(letter);
            }
        }
    }
}
