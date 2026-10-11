using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.Editing
{
    /// <summary>Saved by Prepare Scene; references survive bundle builds and scene copies.</summary>
    public sealed class MapEditSceneIdentity : MonoBehaviour
    {
        [Serializable] public sealed class Item { public Transform target; public string id; }
        public List<Item> objects = new List<Item>();
    }
}
