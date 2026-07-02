using UnityEngine;

namespace MashBoxSDK.Map.Rendering.Instancer
{

    [AddComponentMenu("MashBox/Maps/Rendering/Instance Group")]
    public class InstanceGroup : MonoBehaviour
    {
        void OnEnable()
        {
            if (InstancingManager.Instance != null)
                InstancingManager.Instance.RegisterGroup(this);
        }

        void OnDisable()
        {
            if (InstancingManager.Instance != null)
                InstancingManager.Instance.UnregisterGroup(this);
        }

        public MeshRenderer[] GetRenderers()
        {
            return GetComponentsInChildren<MeshRenderer>();
        }
    }
}
