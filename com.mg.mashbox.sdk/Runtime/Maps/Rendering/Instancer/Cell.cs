using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.Map.Rendering.Instancer
{

    class Cell
    {
        public Bounds bounds;
        public List<Batch> batches = new List<Batch>();
        public bool isVisible;
    }
}