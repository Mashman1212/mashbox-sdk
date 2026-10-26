using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [CreateAssetMenu(menuName = "MashBox/Skinning/Default Skinning Body", fileName = "Default Skinning Body")]
    public sealed class StudioSkinningDefaults : ScriptableObject
    {
        [Tooltip("Body model loaded into every new Skinning workspace. The SDK Skeleton is used for binding.")]
        public GameObject body;
        [Tooltip("Optional skinned bust/head model loaded alongside the body. Drop the original model or prefab here.")]
        public GameObject bust;
        [Tooltip("Optional pants reference loaded hidden in new Skinning workspaces. Toggle visibility in the mesh list to check clothing fit.")]
        public GameObject pants;
        [Tooltip("Optional shirt reference loaded locked and hidden in new Skinning workspaces. Toggle visibility in the mesh list to check clothing fit.")]
        public GameObject shirt;
        [SerializeField, HideInInspector] private StudioModelReference bodyReference = new StudioModelReference();
        [SerializeField, HideInInspector] private StudioModelReference bustReference = new StudioModelReference();
        [SerializeField, HideInInspector] private StudioModelReference pantsReference = new StudioModelReference();
        [SerializeField, HideInInspector] private StudioModelReference shirtReference = new StudioModelReference();

        private void OnEnable() => StudioDefaultReferences.Queue();
        internal void ResolveReferences()
        {
            body = (bodyReference ??= new StudioModelReference()).Resolve(body);
            bust = (bustReference ??= new StudioModelReference()).Resolve(bust);
            pants = (pantsReference ??= new StudioModelReference()).Resolve(pants);
            shirt = (shirtReference ??= new StudioModelReference()).Resolve(shirt);
        }
    }
}
