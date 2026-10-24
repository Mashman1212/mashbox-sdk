using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    /// <summary>Fixed-layout destruction race. The host reports real destruction and local player position.</summary>
    [AddComponentMenu("MashBox/Challenges/Gun Race")]
    [DisallowMultipleComponent, RequireComponent(typeof(MBRace))]
    public sealed class MBGunRace : MonoBehaviour
    {
        public Transform FinishPad;
        [Min(.5f)] public float FinishRadius = 3f;
        [Min(.1f)] public float FinishHeight = 2f;
        private readonly HashSet<int> destroyed = new HashSet<int>();
        private MBRace race;
        private bool occupied;
        public int TargetCount { get; private set; }
        public int DestroyedCount => destroyed.Count;
        public bool Running { get; private set; }
        public bool CanFinish => enabled && Running && TargetCount > 0 && destroyed.Count == TargetCount && occupied;

        private void OnEnable()
        {
            race = GetComponent<MBRace>();
            race.OnStaged += ResetAttempt;
            race.OnFailed += ResetAttempt;
        }
        private void OnDisable()
        {
            if (race) { race.OnStaged -= ResetAttempt; race.OnFailed -= ResetAttempt; }
            ResetAttempt();
        }
        public void BeginAttempt(int count)
        {
            ResetAttempt();
            TargetCount = Mathf.Max(0, count);
            Running = TargetCount > 0;
        }
        public void ResetAttempt() { Running = false; occupied = false; TargetCount = 0; destroyed.Clear(); }
        public void ReportDestroyed(int index)
        {
            if (Running && MBGameplayStateGuard.IsGameplayActive && index >= 0 && index < TargetCount)
                destroyed.Add(index);
        }
        public void UpdatePlayerPosition(Vector3 position, bool grounded = true)
        {
            occupied = false;
            if (!FinishPad || !Running || !grounded) return;
            Vector3 delta = position - FinishPad.position;
            occupied = delta.y >= -.25f && delta.y <= FinishHeight &&
                delta.x * delta.x + delta.z * delta.z <= FinishRadius * FinishRadius;
        }
        public bool CanPassGate(MBRaceGate gate)
        {
            var gates = GetComponent<MBRace>().GetOrderedGates();
            return gates.Count >= 2 && (gate == gates[0] || gate == gates[gates.Count - 1] && CanFinish);
        }
    }
}
