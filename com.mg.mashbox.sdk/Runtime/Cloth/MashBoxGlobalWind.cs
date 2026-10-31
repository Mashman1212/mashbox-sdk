using UnityEngine;
namespace MashBoxSDK.Cloth
{
    [AddComponentMenu("MashBox/Global Wind"), DisallowMultipleComponent]
    public sealed class MashBoxGlobalWind : MonoBehaviour
    {
        [Min(0)] public float speed = 4;
        [Range(0,1)] public float gust = .25f;
        [Min(0)] public float gustFrequency = .5f;
        private static MashBoxGlobalWind owner;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwner() => owner=null;
        void OnEnable() { owner=this; Apply(); }
        void Update() { if(owner==this)Apply(); }
        void OnDisable() { if(owner==this){owner=null;MashBoxWind.Set(default);} }
        void Apply() => MashBoxWind.Set(new MashBoxWindSettings { velocity=transform.forward*Mathf.Max(0,speed),gust=gust,frequency=gustFrequency });
    }
}
