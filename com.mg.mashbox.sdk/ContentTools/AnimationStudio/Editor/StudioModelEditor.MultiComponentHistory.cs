using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable] internal sealed class StudioComponentSelectionRecord
    {
        public int node,meshId,mode;
        public int[] components=Array.Empty<int>();
    }
    internal sealed partial class StudioModelEditor
    {
        private StudioComponentSelectionRecord[] CaptureComponentTargets()
        {
            SyncComponentTarget();var result=new List<StudioComponentSelectionRecord>();
            foreach(int index in objects.OrderBy(i=>i))
            {
                var target=ComponentTargetAt(index,false);if(target==null)continue;
                var mesh=Document.nodes[index].mesh;
                result.Add(new StudioComponentSelectionRecord{node=index,meshId=mesh?mesh.GetInstanceID():0,mode=(int)target.mode,components=target.selection.OrderBy(i=>i).ToArray()});
            }
            return result.ToArray();
        }
        private bool ComponentTargetsMatch(StudioComponentSelectionRecord[] records)
        {
            var current=CaptureComponentTargets();if(records==null)return current.All(r=>r.node==Active||r.components.Length==0);
            if(current.Length!=records.Length)return false;
            for(int i=0;i<current.Length;i++)if(current[i].node!=records[i].node||current[i].meshId!=records[i].meshId||current[i].mode!=records[i].mode||!current[i].components.SequenceEqual(records[i].components))return false;
            return true;
        }
        private void RestoreComponentTargets(StudioComponentSelectionRecord[] records)
        {
            if(records==null||!Document)return;
            foreach(var record in records)
            {
                if(record.node<0||record.node>=Document.nodes.Count||record.mode<0||record.mode>3)continue;
                var mesh=Document.nodes[record.node].mesh;
                if((mesh?mesh.GetInstanceID():0)!=record.meshId)continue;
                var target=ComponentTargetAt(record.node);target.mode=(ComponentMode)record.mode;
                using(new ComponentTargetScope(this,record.node))
                {
                    Components.Clear();int count=mode==ComponentMode.Object?0:ComponentCount();
                    foreach(int component in record.components??Array.Empty<int>())if(component>=0&&component<count)Components.Add(component);
                }
            }
        }
    }
}
