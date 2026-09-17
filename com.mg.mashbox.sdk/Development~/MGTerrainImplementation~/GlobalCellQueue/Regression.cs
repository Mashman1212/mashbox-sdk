using System; using System.Collections.Generic;
public class MGTerrain { public struct DetailChunkKey { public int id; public DetailChunkKey(int value) { id=value; } } }
public static class Mathf { public static int RoundToInt(float x) { return (int)Math.Round(x); } public static float Clamp01(float x) { return Math.Max(0,Math.Min(1,x)); } }
public class QueueRegression {
 int m_RemainingBuilds; int VisibleDetailBudget=100;
 class Quality { public float m_DistantDetailBudgetReserve=.25f; }
 Quality m_Quality=new Quality(); float NearDetailScale, DistantDetailScale;
 bool ConsumeCellBuildBudget() { if(m_RemainingBuilds<=0)return false; m_RemainingBuilds--; return true; }
 static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        struct CellBuildRequest
        {
            internal readonly MGTerrain tile;
            internal readonly MGTerrain.DetailChunkKey key;
            internal readonly float distance;
            internal CellBuildRequest(MGTerrain tile, MGTerrain.DetailChunkKey key, float distance)
            { this.tile = tile; this.key = key; this.distance = distance; }
        }
        readonly List<CellBuildRequest> m_CellBuildRequests = new List<CellBuildRequest>();
        readonly List<MGTerrain> m_CellBuildTiles = new List<MGTerrain>();
        bool m_CollectingCellBuilds;

        internal bool TryBuildCell(MGTerrain tile, MGTerrain.DetailChunkKey key, float distance)
        {
            if (m_RemainingBuilds <= 0) return false;
            if (m_CollectingCellBuilds)
            {
                // Keep only the nearest frame-budget worth of requests. Memory is
                // bounded by the world budget, not the number of tiles or missing cells.
                int index = 0;
                while (index < m_CellBuildRequests.Count && m_CellBuildRequests[index].distance <= distance) index++;
                if (index >= m_RemainingBuilds) return false;
                m_CellBuildRequests.Insert(index, new CellBuildRequest(tile, key, distance));
                if (m_CellBuildRequests.Count > m_RemainingBuilds)
                    m_CellBuildRequests.RemoveAt(m_CellBuildRequests.Count - 1);
                return false;
            }
            for (int i = 0; i < m_CellBuildRequests.Count; i++)
            {
                var request = m_CellBuildRequests[i];
                if (request.tile != tile || !request.key.Equals(key)) continue;
                if (!ConsumeCellBuildBudget()) return false;
                m_CellBuildRequests.RemoveAt(i);
                return true;
            }
            return false;
        }

        void UpdateDetailScales(long near, long distant)
        {
            NearDetailScale = DistantDetailScale = 1f;
            int budget = VisibleDetailBudget;
            if (near + distant <= budget) return;
            long farAllocation = Math.Min(distant, Mathf.RoundToInt(budget
                * Mathf.Clamp01(m_Quality.m_DistantDetailBudgetReserve)));
            long nearAllocation = Math.Min(near, budget - farAllocation);
            long extra = budget - nearAllocation - farAllocation;
            long extraNear = Math.Min(near - nearAllocation, extra);
            nearAllocation += extraNear;
            farAllocation += Math.Min(distant - farAllocation, extra - extraNear);
            NearDetailScale = nearAllocation / (float)Math.Max(1L, near);
            DistantDetailScale = farAllocation / (float)Math.Max(1L, distant);
        }


 public static string Run() {
  var q=new QueueRegression(); var a=new MGTerrain(); var b=new MGTerrain();
  q.m_RemainingBuilds=2; q.m_CollectingCellBuilds=true;
  Check(!q.TryBuildCell(a,new MGTerrain.DetailChunkKey(1),100),"Build happened during collection");
  q.TryBuildCell(a,new MGTerrain.DetailChunkKey(2),80);
  q.TryBuildCell(b,new MGTerrain.DetailChunkKey(1),2);
  q.TryBuildCell(a,new MGTerrain.DetailChunkKey(3),3);
  Check(q.m_CellBuildRequests.Count==2 && q.m_CellBuildRequests[0].tile==b,"Closest neighbouring tile lost priority");
  q.m_CollectingCellBuilds=false;
  Check(!q.TryBuildCell(a,new MGTerrain.DetailChunkKey(1),100),"Ungranted cell built");
  Check(q.TryBuildCell(a,new MGTerrain.DetailChunkKey(3),3),"Cross-tile grant lost");
  Check(q.TryBuildCell(b,new MGTerrain.DetailChunkKey(1),2),"Neighbour grant lost");
  Check(q.m_RemainingBuilds==0,"World build budget not respected");
  q.m_RemainingBuilds=8; q.m_CollectingCellBuilds=true;
  for(int i=10000;i>0;i--) q.TryBuildCell(i%2==0?a:b,new MGTerrain.DetailChunkKey(i),i);
  Check(q.m_CellBuildRequests.Count==8 && q.m_CellBuildRequests[0].distance==1 && q.m_CellBuildRequests[7].distance==8,"Bounded nearest selection failed");
  q.UpdateDetailScales(100,900);
  Check(Math.Abs(q.NearDetailScale-.75f)<.0001 && Math.Abs(q.DistantDetailScale-25f/900)<.0001,"Global LOD allocation failed");
  q.UpdateDetailScales(0,900); Check(Math.Abs(q.DistantDetailScale-100f/900)<.0001,"Unused near budget was not returned");
  q.UpdateDetailScales(10,10); Check(q.NearDetailScale==1 && q.DistantDetailScale==1,"Under-budget density changed");
  return "PASS: cross-tile nearest selection, bounded 10,000-cell queue, grant enforcement, shared build cap, global near/far budgets.";
 }
}
