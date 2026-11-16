using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new[] { typeof(PawnGenerationRequest) })]
    internal static class Patch_AquaticAdultGenerationRequest
    {
        private static void Prefix(ref PawnGenerationRequest request)
        {
            if (!AquaticEggLayerUtility.UsesAdultEmergence(request.KindDef?.race)
                || request.FixedBiologicalAge.HasValue
                || request.AllowedDevelopmentalStages == DevelopmentalStage.Newborn)
                return;
            request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateRandomAge",
        new[] { typeof(Pawn), typeof(PawnGenerationRequest) })]
    internal static class Patch_AquaticAdultGenerationAge
    {
        private static void Postfix(Pawn pawn, PawnGenerationRequest request)
        {
            if (pawn?.ageTracker == null
                || !AquaticEggLayerUtility.UsesAdultEmergence(pawn.def)
                || request.FixedBiologicalAge.HasValue
                || request.AllowedDevelopmentalStages == DevelopmentalStage.Newborn)
                return;
            var stages = pawn.RaceProps.lifeStageAges;
            if (stages == null || stages.Count == 0) return;
            long minimumTicks = (long)Mathf.Ceil(stages[stages.Count - 1].minAge
                * (60f * GenDate.TicksPerDay)) + 1L;
            if (pawn.ageTracker.AgeBiologicalTicks >= minimumTicks) return;
            pawn.ageTracker.AgeBiologicalTicks = minimumTicks;
            if (pawn.ageTracker.AgeChronologicalTicks < minimumTicks)
                pawn.ageTracker.AgeChronologicalTicks = minimumTicks;
        }
    }
}
