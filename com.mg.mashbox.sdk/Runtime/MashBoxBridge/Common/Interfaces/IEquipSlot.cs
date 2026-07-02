
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IEquipSlot
    {
        GameObject GetEquipItem();
        
        string SlotID { get; }

        string EquipItemID { get; }


        public enum Type
        {
            Bust,
            Body,
            Shirt,
            Pants,
            Shoes,
            Socks,
            Cape,
            Hat,
            Gloves,
            Hair,
            Eyes,
            Back,
            Eyewear,
            Accessory
        }

        Type SlotType { get; }

        void Equip(UnityEngine.GameObject go);
        void Preview(UnityEngine.GameObject go);
        void EquipPreviewed();
    }
}