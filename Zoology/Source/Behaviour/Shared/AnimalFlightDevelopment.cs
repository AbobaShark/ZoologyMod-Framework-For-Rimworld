using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Applied to particular bird races, not to AnimalBaby globally: flying
    // hatchlings (including pterosaurs) retain the normal flight mechanics.
    public sealed class ModExtension_FlightDevelopment : DefModExtension { }

    internal static class AnimalFlightDevelopment
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");

        internal static bool PermitsFlight(Pawn_FlightTracker flight)
        {
            Pawn pawn = flight == null ? null : PawnField(flight);
            if (pawn?.RaceProps?.Animal != true) return true;
            LifeStageDef stage = pawn.ageTracker?.CurLifeStage;
            if (stage == null) return true;

            // A species-specific LifeStageProfiles rule takes precedence over
            // the race-level hatchling default. True only removes the Zoology
            // restriction; it never creates a flight ability.
            if (ZoologyLifeStageProfileInstaller.TryGetFlightRule(pawn, out bool allowed))
                return allowed;
            return !DefModExtensionCache<ModExtension_FlightDevelopment>.Has(pawn)
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(stage);
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), "get_CanEverFly")]
    internal static class Patch_AnimalLifeStageCanEverFly
    {
        static void Postfix(Pawn_FlightTracker __instance, ref bool __result)
        {
            if (__result && !AnimalFlightDevelopment.PermitsFlight(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), "get_CanFlyNow")]
    internal static class Patch_AnimalLifeStageCanFlyNow
    {
        static void Postfix(Pawn_FlightTracker __instance, ref bool __result)
        {
            if (__result && !AnimalFlightDevelopment.PermitsFlight(__instance)) __result = false;
        }
    }

    // Notify_JobStarted in Zoology may invoke this directly to bypass the
    // normal cooldown; it must obey the same stage restriction.
    [HarmonyPatch(typeof(Pawn_FlightTracker), "StartFlyingInternal")]
    internal static class Patch_AnimalLifeStageStartFlyingInternal
    {
        static bool Prefix(Pawn_FlightTracker __instance)
            => AnimalFlightDevelopment.PermitsFlight(__instance);
    }
}
