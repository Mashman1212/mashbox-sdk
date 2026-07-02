using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface ICharacterData
    {
        string CharacterName { get; }
        public void SetSlotData(IEquipSlot.Type slotType, GameObject go);
        public void Equip();
        public bool IsCustomCharacter { get; }
    }
}