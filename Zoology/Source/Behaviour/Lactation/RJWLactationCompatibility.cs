using System;
using HarmonyLib;

namespace ZoologyMod
{
    internal static class RJWLactationCompatibility
    {
        private const string RJWBasePregnancyTypeName = "rjw.Hediff_BasePregnancy";
        private static bool resolved;
        private static bool isActive;

        internal static bool IsRJWActive
        {
            get
            {
                if (!resolved)
                {
                    isActive = AccessTools.TypeByName(RJWBasePregnancyTypeName) != null;
                    resolved = true;
                }

                return isActive;
            }
        }

        internal static bool ShouldObserveRecentBirths()
        {
            if (!LactationSettingsGate.Enabled())
            {
                return false;
            }

            return IsRJWActive;
        }

        internal static void ResetCache()
        {
            resolved = false;
            isActive = false;
        }
    }
}
