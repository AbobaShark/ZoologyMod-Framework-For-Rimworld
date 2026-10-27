using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalEffectiveArea
    {
        public static bool TryGetFollowingMother(Pawn baby, out Pawn mother)
        {
            mother = null;
            if (baby?.IsAnimal != true || !baby.Spawned || !ChildcareUtility.IsChildcareEnabled
                || !ChildcareUtility.HasChildcareExtension(baby)
                || !(AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage)
                    || baby.DevelopmentalStage == DevelopmentalStage.Baby)) return false;
            return ChildcareUtility.TryGetBiologicalMother(baby, out mother)
                && mother != baby && !mother.Dead && mother.MapHeld == baby.Map
                && (mother.Spawned || mother.ParentHolder is Building_AnimalBurrow)
                && mother.playerSettings != null;
        }
    }

    // Override only the effective restriction; the child's saved assignment is untouched.
    [HarmonyPatch(typeof(Pawn_PlayerSettings), "get_EffectiveAreaRestrictionInPawnCurrentMap")]
    internal static class Patch_BabyEffectiveArea
    {
        static void Postfix(Pawn ___pawn, ref Area __result)
        {
            if (AnimalEffectiveArea.TryGetFollowingMother(___pawn, out Pawn mother))
                __result = mother.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_AllowedArea), nameof(PawnColumnWorker_AllowedArea.DoCell))]
    internal static class Patch_BabyAreaDisplay
    {
        static bool Prefix(Rect rect, Pawn pawn)
        {
            if (!AnimalEffectiveArea.TryGetFollowingMother(pawn, out Pawn mother)) return true;
            Area area = mother.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;
            Widgets.Label(rect, "Zoology_BabyFollowsArea".Translate(mother.LabelShort,
                area?.Label ?? "Zoology_UnrestrictedArea".Translate().ToString()));
            TooltipHandler.TipRegion(rect, "Zoology_BabyAreaTip".Translate());
            return false;
        }
    }

    [HarmonyPatch(typeof(DesignationManager), nameof(DesignationManager.AddDesignation))]
    internal static class Patch_ManualSlaughterLactatingWarning
    {
        static void Prefix(DesignationManager __instance, Designation newDes, out bool __state)
        {
            __state = newDes.def == DesignationDefOf.Slaughter && newDes.target.Thing is Pawn p
                && __instance.DesignationOn(p, DesignationDefOf.Slaughter) == null;
        }

        static void Postfix(Designation newDes, bool __state)
        {
            if (!__state || !(newDes.target.Thing is Pawn pawn) || pawn.gender != Gender.Female
                || !LactationSettingsGate.Enabled()) return;
            HediffDef lactating = AnimalLactationUtility.LactatingHediffDef;
            if (lactating != null && pawn.health.hediffSet.HasHediff(lactating))
                Messages.Message("Zoology_SlaughterLactatingWarning".Translate(pawn.Named("ANIMAL")),
                    pawn, MessageTypeDefOf.CautionInput, false);
        }
    }
}
