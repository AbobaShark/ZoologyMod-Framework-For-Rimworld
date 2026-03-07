using System;
using HarmonyLib;
using Verse;

namespace ZoologyMod
{
    public static class CEChecker
    {
        
        private const string CE_PACKAGE_ID = "CETeam.CombatExtended";

        
        
        
        
        
        public static bool IsCEInstalled()
        {
            try
            {
                
                bool byType = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration") != null
                              || AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;

                if (byType) return true;

                
                try
                {
                    var mod = ModLister.GetActiveModWithIdentifier(CE_PACKAGE_ID);
                    if (mod != null) return true;
                }
                catch
                {
                    
                }

                
                return false;
            }
            catch
            {
                
                return false;
            }
        }
    }
}
