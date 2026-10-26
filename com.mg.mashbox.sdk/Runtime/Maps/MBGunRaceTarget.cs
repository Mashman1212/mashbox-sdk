using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Challenges/Gun Race Target")]
    [DisallowMultipleComponent]
    public sealed class MBGunRaceTarget : MonoBehaviour
    {
        [Tooltip("A resettable destructible prefab, spawned at this exact transform on each attempt.")]
        public GameObject Prefab;
        [Tooltip("Map prefab catalog key used by portable challenge sets.")]
        public string PrefabKey;
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, .2f, .15f, .8f);
            Gizmos.DrawWireCube(transform.position + transform.up * .6f, new Vector3(.9f, 1.2f, .9f));
        }
    }
}
