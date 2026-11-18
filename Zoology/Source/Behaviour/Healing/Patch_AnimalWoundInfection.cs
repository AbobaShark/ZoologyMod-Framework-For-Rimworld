using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed class ModExtension_WoundInfectionChanceFactor : DefModExtension
    {
        public float factor = 1f;
    }

    [HarmonyPatch(typeof(HediffComp_Infecter), nameof(HediffComp_Infecter.CompPostPostAdd))]
    internal static class Patch_AnimalWoundInfectionVanilla
    {
        private static readonly AccessTools.FieldRef<HediffComp_Infecter, int> TicksUntilInfect =
            AccessTools.FieldRefAccess<HediffComp_Infecter, int>("ticksUntilInfect");

        private static void Postfix(HediffComp_Infecter __instance)
        {
            var pawn = __instance.Pawn;
            if (pawn?.RaceProps?.Animal != true)
            {
                return;
            }

            var extension = pawn.def.GetModExtension<ModExtension_WoundInfectionChanceFactor>();
            if (extension == null || extension.factor >= 1f)
            {
                return;
            }

            if (TicksUntilInfect(__instance) > 0 && Rand.Value >= extension.factor)
            {
                TicksUntilInfect(__instance) = -2;
            }
        }
    }

    [HarmonyPatch(typeof(ImmunityHandler), nameof(ImmunityHandler.DiseaseContractChanceFactor),
        new[] { typeof(HediffDef), typeof(BodyPartRecord) })]
    internal static class Patch_AnimalWoundInfectionCE
    {
        private static void Postfix(ImmunityHandler __instance, HediffDef diseaseDef, ref float __result)
        {
            if (diseaseDef != HediffDefOf.WoundInfection || __result <= 0f)
            {
                return;
            }

            var pawn = __instance.pawn;
            if (pawn?.RaceProps?.Animal != true)
            {
                return;
            }

            var extension = pawn.def.GetModExtension<ModExtension_WoundInfectionChanceFactor>();
            if (extension != null)
            {
                __result *= extension.factor;
            }
        }
    }
}
