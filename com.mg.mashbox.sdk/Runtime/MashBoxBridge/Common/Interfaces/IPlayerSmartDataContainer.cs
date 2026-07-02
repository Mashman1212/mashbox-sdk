namespace MashBoxBridge.Common.Interfaces
{
    public interface IPlayerSmartDataContainer
    {
        float GetValue(ISmartDataBase data);
        void SetValue(ISmartDataBase data, float value);
    }
}
