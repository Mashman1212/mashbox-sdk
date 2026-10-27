using System;
using UnityEngine;

namespace MashBoxSDK.Cloth
{
    [DisallowMultipleComponent, AddComponentMenu("MashBox/Cloth")]
    [DefaultExecutionOrder(10000)]
    public sealed class MashBoxCloth : MonoBehaviour
    {
        public Renderer garment;
        public Transform collisionRoot;
        public MashBoxClothProfile profile;
        [Tooltip("Distance culling requires a camera. Zero disables distance culling.")]
        public Camera viewCamera;
        [Min(0)] public float maximumDistance = 30;
        public MashBoxClothSession Session { get; private set; }
        bool suspended;
        void OnEnable() { if (Application.isPlaying) Rebuild(); }
        void OnDisable() { Session?.Dispose(); Session = null; }
        public void Rebuild()
        {
            OnDisable();
            suspended = false;
            if (!garment) garment = GetComponent<Renderer>();
            if (!garment || !profile) return;
            try { Session = new MashBoxClothSession(garment, profile, collisionRoot ? collisionRoot : transform.root); }
            catch (Exception e) { Debug.LogError("MashBox Cloth: " + e.Message, this); enabled = false; }
        }
        void LateUpdate()
        {
            if (!garment || !profile) { OnDisable(); return; }
            bool cull = !garment.enabled || !garment.gameObject.activeInHierarchy ||
                (viewCamera && maximumDistance > 0 && (viewCamera.transform.position - garment.bounds.center).sqrMagnitude > maximumDistance * maximumDistance);
            if (cull) { if (!suspended) { OnDisable(); suspended = true; } return; }
            if (suspended) { suspended = false; Rebuild(); }
            try { Session?.Advance(Time.deltaTime); }
            catch (Exception e) { Debug.LogError("MashBox Cloth: " + e.Message, this); enabled = false; }
        }
        public void ResetSimulation() => Session?.Reset();
    }
}
