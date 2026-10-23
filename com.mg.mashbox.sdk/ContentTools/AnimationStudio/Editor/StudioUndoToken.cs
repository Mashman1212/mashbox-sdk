using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // First object in a complete-object Undo record is its native identifier.
    // A tiny, unique token lets us release that action without clearing later
    // snapshots of the same mesh or unrelated Unity history.
    internal sealed class StudioUndoToken : ScriptableObject { }
}
