using System;
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IDataIcon
    {
        Texture GetDataIcon();
        Action<Texture> OnIconUpdated { get; set; }
        void DownloadIconRequest();
        void CancelDownloadIconRequest();
    }
    public interface ISelectionIcon
    {
        Texture GetSelectionIcon();
    } 
    
}