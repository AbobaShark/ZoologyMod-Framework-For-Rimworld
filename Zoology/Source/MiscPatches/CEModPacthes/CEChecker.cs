using System;
using HarmonyLib;
using Verse;

namespace ZoologyMod
{
    public static class CEChecker
    {
        private const string CE_PACKAGE_ID = "CETeam.CombatExtended";
        private static bool? isCEInstalledCache;

        public static bool IsCEInstalled()
        {
            if (isCEInstalledCache.HasValue)
            {
                return isCEInstalledCache.Value;
            }

            try
            {
                bool byType = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration") != null
                              || AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;

                if (byType)
                {
                    isCEInstalledCache = true;
                    return true;
                }

                try
                {
                    var mod = ModLister.GetActiveModWithIdentifier(CE_PACKAGE_ID);
                    if (mod != null)
                    {
                        isCEInstalledCache = true;
                        return true;
                    }
                }
                catch
                {
                }

                isCEInstalledCache = false;
                return false;
            }
            catch
            {
                isCEInstalledCache = false;
                return false;
            }
        }
    }
}
