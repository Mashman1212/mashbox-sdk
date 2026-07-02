using UnityEngine;

namespace MashBoxSDK.Maching
{
    public interface IMachEquipSlot
    {
        public string SlotID { get; }
        public void Equip(GameObject go);
        public GameObject GetEquippedItem();
    }
}
