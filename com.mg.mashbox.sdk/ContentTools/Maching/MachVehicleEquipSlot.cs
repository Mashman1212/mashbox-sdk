using UnityEngine;
using System.Globalization;

namespace MashBoxSDK.Maching
{
    [ExecuteAlways]
    public class MachVehicleEquipSlot : MonoBehaviour, IMachEquipSlot
    {
        public string SlotID => slotTag;

        [SerializeField] private string slotTag = "default";
        [SerializeField] private GameObject equippedItem;


        [SerializeField] private bool _isDriveSide;
        private void OnEnable()
        {
            // Auto-detect first child if no equipped item assigned.
            if (equippedItem == null && transform.childCount > 0)
            {
                equippedItem = transform.GetChild(0).gameObject;
#if UNITY_EDITOR
                Debug.Log($"[MachEquipSlot] {name} detected '{equippedItem.name}' as equipped item.");
#endif
            }

            HandleHubGuard();
        }

        /// <summary>
        /// Attempts to equip a new item to this slot.
        /// Only allows items whose name begins with this slot's tag (case-insensitive).
        /// </summary>
        public void Equip(GameObject item)
        {
            if (item == null)
            {
                Debug.LogWarning($"[MachEquipSlot] Tried to equip null item on {name}");
                return;
            }

            // Case-insensitive tag check
            string itemName = item.name.ToLower(CultureInfo.InvariantCulture);
            string tagLower = slotTag.ToLower(CultureInfo.InvariantCulture);

            if (!itemName.Contains("_" +tagLower + "_"))
            {
                Debug.LogWarning($"[MachEquipSlot] '{item.name}' does not match slot tag '{slotTag}' on {name}. Equip canceled.");
                return;
            }

            ClearChildren();

            GameObject newItem = Instantiate(item, transform);
            newItem.name = item.name;
            equippedItem = newItem;

#if UNITY_EDITOR
            Debug.Log($"[MachEquipSlot] Equipped {item.name} on {name}");
#endif

            HandleHubGuard();
        }

        public void Unequip()
        {
            ClearChildren();
            equippedItem = null;
        }

        public void HandleHubGuard()
        {
            if (this.slotTag != "Hub Guard")
            {
                return;
            }


            foreach (Transform child in this.GetComponentsInChildren<Transform>())
            {
                child.gameObject.SetActive(true);
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                
                if (!_isDriveSide && child.gameObject.name == "Standard")
                {
                    child.gameObject.SetActive(true);
                }
                if (_isDriveSide && child.gameObject.name == "Standard")
                {
                    child.gameObject.SetActive(false);
                }
                
                if (_isDriveSide && child.gameObject.name == "Hub Driver")
                {
                    child.gameObject.SetActive(true);
                }
                if (!_isDriveSide && child.gameObject.name == "Hub Driver")
                {
                    child.gameObject.SetActive(false);
                }
            }
            
        }

        public GameObject GetEquippedItem() => equippedItem;

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(transform.GetChild(i).gameObject);
                else
                    Destroy(transform.GetChild(i).gameObject);
#else
                Destroy(transform.GetChild(i).gameObject);
#endif
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(slotTag))
                slotTag = "default";
        }
#endif
    }
}
