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

        [Serializable]
        public sealed class OutfitPiece
        {
            public bool enabled = true;
            public GameObject prefab;
            public OutfitOptions options = new OutfitOptions { scale = 1, cutoutDistance = .03f };
        }
    }
}
