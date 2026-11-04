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
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(baby.ageTracker?.CurLifeStage)) return false;
            return ChildcareUtility.TryGetBiologicalMother(baby, out mother)
                && mother != baby && !mother.Dead && mother.MapHeld == baby.Map
                && (mother.Spawned || mother.ParentHolder is Building_AnimalBurrow)
                && mother.playerSettings != null;
        }

        // The vanilla setter performs the same job interruption as a manual area change.
        public static void SyncWithMother(Pawn child, Pawn mother)
        {
            if (child?.playerSettings == null || mother?.playerSettings == null
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(child.ageTracker?.CurLifeStage)
                || child.Faction != Faction.OfPlayer || child.Faction != mother.Faction)
            {
                return;
            }

            Area target = mother.playerSettings.AreaRestrictionInPawnCurrentMap;
            if (!ReferenceEquals(child.playerSettings.AreaRestrictionInPawnCurrentMap, target))
            {
                child.playerSettings.AreaRestrictionInPawnCurrentMap = target;
            }
        }
    }

    // Also cover the period before an existing child has been synchronized.
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


    // Executed only when an animal parent changes its colony restriction.
    [HarmonyPatch(typeof(Pawn_PlayerSettings), "set_AreaRestrictionInPawnCurrentMap")]
    internal static class Patch_ParentAllowedAreaChanged
    {
        static void Prefix(Pawn ___pawn, out Area __state)
        {
            __state = ___pawn?.playerSettings?.AreaRestrictionInPawnCurrentMap;
        }

        static void Postfix(Pawn ___pawn, Area __state)
        {
            if (!ChildcareUtility.IsChildcareEnabled || ___pawn?.IsAnimal != true
                || !___pawn.Spawned || ___pawn.Faction != Faction.OfPlayer
                || !ChildcareUtility.HasChildcareExtension(___pawn)
                || ChildcareUtility.IsAnimalChild(___pawn)
                || ReferenceEquals(__state, ___pawn.playerSettings?.AreaRestrictionInPawnCurrentMap))
            {
                return;
            }

            var pawns = ___pawn.Map.mapPawns.SpawnedPawnsInFaction(___pawn.Faction);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn child = pawns[i];
                if (child != ___pawn && child.IsAnimal
                    && AnimalLifeStageUtility.IsAnimalInfantLifeStage(child.ageTracker?.CurLifeStage)
                    && AnimalEffectiveArea.TryGetFollowingMother(child, out Pawn mother)
                    && ReferenceEquals(mother, ___pawn))
                {
                    AnimalEffectiveArea.SyncWithMother(child, mother);
                }
            }
        }
    }

    // Existing saves and a parent moving away require only rare, per-child work.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TickRare))]
    internal static class Patch_Childcare_FollowMotherTickRare
    {
        private const int MaximumSleepingSeparationSquared = 20 * 20;

        static void Postfix(Pawn __instance)
        {
            if (!ChildcareUtility.IsChildcareEnabled || __instance?.IsAnimal != true
                || !__instance.Spawned || __instance.Downed
                || !AnimalLifeStageUtility.IsAnimalInfantLifeStage(__instance.ageTracker?.CurLifeStage)
                || !AnimalEffectiveArea.TryGetFollowingMother(__instance, out Pawn mother))
            {
                return;
            }

            AnimalEffectiveArea.SyncWithMother(__instance, mother);
            if (!mother.Spawned || __instance.Awake()
                || (mother.Position - __instance.Position).LengthHorizontalSquared <= MaximumSleepingSeparationSquared
                || !ChildcareMotherReachability.CanFollow(__instance, mother))
            {
                return;
            }

            __instance.mindState.canSleepTick = (Find.TickManager?.TicksGame ?? 0) + 1000;
            RestUtility.WakeUp(__instance);
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
