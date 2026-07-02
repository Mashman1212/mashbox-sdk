namespace MashBoxBridge.Common.Interfaces
{
    public interface ITimeInterpolator
    {
        void SetTimeNormal();
        void InterpolateNormal();
        void SetSlowMo();
    }
}