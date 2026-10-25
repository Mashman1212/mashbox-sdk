using System;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [CreateAssetMenu(menuName = "MashBox/Animation/Default Character", fileName = "Default Male Character")]
    public sealed class StudioDefaultCharacter : ScriptableObject
    {
        [Tooltip("The animation skeleton or rigged character prefab. Leave empty to use the project's MashBox base.")]
        public GameObject character;
        [Tooltip("Optional skinned male body. Leave empty to use the project's MashBox body when using its base skeleton.")]
        public GameObject body;
        public OutfitPiece[] outfit = Array.Empty<OutfitPiece>();
        [SerializeField, HideInInspector] private StudioModelReference characterReference = new StudioModelReference();
        [SerializeField, HideInInspector] private StudioModelReference bodyReference = new StudioModelReference();

        private void OnEnable() => StudioDefaultReferences.Queue();
        internal void ResolveReferences()
        {
            character = (characterReference ??= new StudioModelReference()).Resolve(character);
            body = (bodyReference ??= new StudioModelReference()).Resolve(body);
            foreach (var piece in outfit ?? Array.Empty<OutfitPiece>())
                if (piece != null) piece.prefab = (piece.modelReference ??= new StudioModelReference()).Resolve(piece.prefab);
        }

        [Serializable]
        public sealed class OutfitPiece
        {
            public bool enabled = true;
            public GameObject prefab;
            [SerializeField, HideInInspector] internal StudioModelReference modelReference = new StudioModelReference();
            public OutfitOptions options = new OutfitOptions { scale = 1, cutoutDistance = .03f };
        }
    }
}
