using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps
{
    public sealed class ModIOPaid : MonoBehaviour
    {
        [Header("Legacy Single Mod ID")]
        [SerializeField] private long _modId;

        [Header("Accepted Mod IDs")]
        [SerializeField] private List<long> _modIds = new List<long>();

        public long ModId => _modId;
        public IReadOnlyList<long> ModIds => _modIds;
        public bool HasValidModId
        {
            get
            {
                if (_modId > 0)
                    return true;

                if (_modIds == null)
                    return false;

                for (int i = 0; i < _modIds.Count; i++)
                {
                    if (_modIds[i] > 0)
                        return true;
                }

                return false;
            }
        }

        public void SetModId(long modId)
        {
            _modId = modId;
        }

        public List<long> GetValidModIds()
        {
            List<long> validModIds = new List<long>();
            AddValidModId(validModIds, _modId);

            if (_modIds != null)
            {
                for (int i = 0; i < _modIds.Count; i++)
                    AddValidModId(validModIds, _modIds[i]);
            }

            return validModIds;
        }

        private static void AddValidModId(List<long> modIds, long modId)
        {
            if (modId <= 0 || modIds.Contains(modId))
                return;

            modIds.Add(modId);
        }
    }
}
