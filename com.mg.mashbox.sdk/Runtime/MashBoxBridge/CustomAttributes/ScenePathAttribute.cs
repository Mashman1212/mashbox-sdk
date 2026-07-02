using System.Diagnostics;
using UnityEngine;

namespace MashBoxBridge.CustomAttributes
{
    [Conditional("UNITY_EDITOR")]
    public class ScenePathAttribute : PropertyAttribute
    {
    }
}