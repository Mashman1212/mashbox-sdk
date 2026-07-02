using System;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IDataChangedEventHandler
    {
        public event EventHandler OnDataChanged;
    }
}