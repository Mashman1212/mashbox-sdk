using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool materialRefreshPending;
        private void OnMaterialAssetChanges(ref ObjectChangeEventStream changes)
        {
            if (!Document) return;
            for (int i=0;i<changes.length;i++)
            {
                if (changes.GetEventType(i)!=ObjectChangeKind.ChangeAssetObjectProperties) continue;
                changes.GetChangeAssetObjectPropertiesEvent(i,out var change);
                var asset=EditorUtility.InstanceIDToObject(change.instanceId);
                if (!(asset is Material) && !(asset is Shader)) continue;
                var checker=StudioMeshMaterialPreview.CheckerMaterial;
                if(asset==checker || checker && asset==checker.shader){QueueMaterialPreviewRefresh();return;}
                var standard=StudioMeshMaterialPreview.DefaultMaterial;
                if (asset==standard || standard && asset==standard.shader) { QueueMaterialPreviewRefresh();return; }
                foreach (var node in Document.nodes)
                    if (node.materials!=null) foreach (var source in node.materials)
                        for (var material=source;material;material=material.parent)
                            if (asset==material || asset==material.shader) { QueueMaterialPreviewRefresh();return; }
            }
        }
        private void QueueMaterialPreviewRefresh()
        {
            if (!Document || materialRefreshPending) return;
            materialRefreshPending=true;
            EditorApplication.delayCall+=RefreshChangedMaterials;
        }
        private void RefreshChangedMaterials()
        {
            materialRefreshPending=false;
            if (!Document) return;
            ApplyDisplayMaterials();
            repaint();
        }
    }
}
