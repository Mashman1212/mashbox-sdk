using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void GroupSelectedObjects()
        {
            var previousTool = tool;
            PrepareMeshSelection(ComponentMode.Object, () => Document && objects.Count > 0,
                "Select objects to group under a new parent.", ComponentMode.Object);
            CloseLiveToolPanes();CloseCombineToolWindow();
            // A selected ancestor already carries its descendants into the group.
            var roots = Document.OrderedHierarchy().Where(i => objects.Contains(i) && !HasSelectedParent(i)).ToArray();
            var center = Vector3.zero;
            foreach (int i in roots)
            {
                var renderer=Transforms[i].GetComponent<Renderer>();
                center += renderer ? renderer.bounds.center : Document.nodes[i].mesh ? BoundsFor(i).center :
                    Transforms[i].GetComponentsInChildren<Renderer>().Length > 0 ? BoundsFor(i).center : Transforms[i].position;
            }
            center /= roots.Length;
            int group=Document.GroupObjects(roots,Transforms,center);
            ClearAllComponentSelections();objects.Clear();objects.Add(group);Active=group;
            mode=ComponentMode.Object;pivot=false;search="";tool=previousTool;
            Rebuild();Changed();RememberAction(RepeatKind.Group);
        }
    }
}
