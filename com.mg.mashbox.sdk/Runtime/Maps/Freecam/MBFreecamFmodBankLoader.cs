using System;
using UnityEngine;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class MBFreecamFmodBankLoader : MonoBehaviour
    {
        [SerializeField] private string[] banks = { "Environment", "Props", "Gameplay" };

        private void Awake()
        {
#if MGFMOD
            LoadBanks();
#endif
        }

        private void OnDestroy()
        {
#if MGFMOD
            UnloadBanks();
#endif
        }

#if MGFMOD
        private void LoadBanks()
        {
            InvokeRuntimeManagerBankMethod("LoadBank");
        }

        private void UnloadBanks()
        {
            InvokeRuntimeManagerBankMethod("UnloadBank");
        }

        private void InvokeRuntimeManagerBankMethod(string methodName)
        {
            try
            {
                foreach (var bankName in banks)
                {
                    if (string.IsNullOrWhiteSpace(bankName))
                        continue;

                    if (string.Equals(methodName, "LoadBank", StringComparison.Ordinal))
                        MBFmodReflection.LoadBank(bankName, true);
                    else if (string.Equals(methodName, "UnloadBank", StringComparison.Ordinal))
                        MBFmodReflection.UnloadBank(bankName);
                }
            }
            catch
            {
                // Optional FMOD integration only.
            }
        }
#endif
    }
}
