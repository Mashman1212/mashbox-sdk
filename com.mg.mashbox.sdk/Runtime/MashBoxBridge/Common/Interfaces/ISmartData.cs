using System;

namespace MashBoxBridge.Common.Interfaces
{
    [Serializable]
    public enum DataStyle
    {
        Slider,
        Button,
        Selector
    }
    
    public interface ISmartDataBase
    {
        float FValue { get; set; }
        
        string ID { get; }
        bool Initialized { get; }

        void Initialize();
        
        DataStyle Style { get; }
        void OnChanged();
        string GetValueText();
        void SaveData();
        void LoadData();
        void ResetData();
        string GetDataLabel();
        string GetDataDescription();
        void DataSubmitted();

        float GetNormalizedValue();

        bool DontHide { get; }

        event Action OnChangedEvent;

        bool IsEnabled { get; }
        void SetEnabled(bool enabled);
        event Action<bool> OnEnabledStateChanged;
    }
    public interface ISmartData<T> : ISmartDataBase
    {
        T Value { get; set; }
    }
}