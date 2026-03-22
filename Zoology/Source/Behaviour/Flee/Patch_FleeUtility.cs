using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FleeUtility), nameof(FleeUtility.ShouldAnimalFleeDanger))]
    public static class Patch_ShouldAnimalFleeDanger_AdjustSafeSize
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableCustomFleeDanger;
        }

        public static void Postfix(Pawn pawn, ref bool __result)
        {
            var settings = ModConstants.Settings;
            if (settings == null || !settings.EnableCustomFleeDanger)
            {
                return;
            }

            if (!__result)
            {
                return;
            }

            if (pawn == null || !pawn.IsAnimal || pawn.Dead)
            {
                return;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return;
            }

            if (pawn.Map == null || !pawn.Map.IsPlayerHome)
            {
                return;
            }

            var raceProps = pawn.RaceProps;
            bool predator = raceProps?.predator ?? false;
            float baseSize = raceProps?.baseBodySize ?? pawn.BodySize;
            bool safeOnHome =
                (predator && baseSize >= settings.SafePredatorBodySizeThreshold)
                || (!predator && baseSize > settings.SafeNonPredatorBodySizeThreshold);

            if (safeOnHome)
            {
                __result = false;
            }
        }
    }
}
