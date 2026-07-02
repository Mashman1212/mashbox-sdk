using System.Diagnostics;

namespace MashBoxBridge.CustomAttributes
{
    [Conditional("UNITY_EDITOR")]
    public class InspectorButtonAttribute : System.Attribute { }
}
