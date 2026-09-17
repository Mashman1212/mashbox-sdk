using System;
public static class Mathf { public static float Clamp01(float x) {return Math.Max(0,Math.Min(1,x));} }
public class RoundingRegression {
        internal static int AllocateCellShare(int population, float scale, int remainingBudget, ref double remainder)
        {
            if (population <= 0 || remainingBudget <= 0) return 0;
            double share = population * (double)Mathf.Clamp01(scale) + remainder;
            int allocated = (int)Math.Min(population, Math.Min(remainingBudget, Math.Floor(share)));
            remainder = share - Math.Floor(share);
            return allocated;
        }


public static string Run() {
 int budget=100000, remaining=budget; double carry=0;
 float scale=budget/(float)(40000L*60000+3L*40000*250);
 int[] totals=new int[4];
 for(int tile=0;tile<4;tile++) for(int cell=0;cell<40000;cell++) {
  int n=AllocateCellShare(tile==0?60000:250,scale,remaining,ref carry);
  totals[tile]+=n;remaining-=n;
 }
 if(totals[1]==0 || totals[2]==0 || totals[3]==0 || remaining<0 || remaining>1) throw new Exception("Uneven tile regression failed");
 if(Math.Floor(250*scale)!=0) throw new Exception("Test did not reproduce previous zero-allocation case");
 carry=0; int small=0; for(int i=0;i<100;i++)small+=AllocateCellShare(1,.1f,10-small,ref carry);
 if(small!=10)throw new Exception("Fractional cells did not retain ten shares");
 carry=0; if(AllocateCellShare(3,1,100,ref carry)!=3)throw new Exception("Population cap failed");
 if(AllocateCellShare(30,1,2,ref carry)!=2)throw new Exception("Budget cap failed");
 return "PASS: dense/sparse tile allocations="+string.Join(",",totals)+"; remaining="+remaining+". Fractional-cell, population, and budget caps passed.";
}}
