#if RIMWORLD17
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalBarnReproduction
    {
        // Native animal mating uses RaceProperties.mateMtbHours (2500 game
        // ticks per hour). Keep that same species-specific schedule while
        // removing the 500-tick animation/pathfinding job for stored pawns.
        public static void TryMateResidents(Building_AnimalBarn barn)
        {
            if (!Building_AnimalBarn.Enabled || barn == null || barn.AnimalCount < 2)
            {
                return;
            }

            var animals = new List<Pawn>(barn.Animals);
            for (int i = 0; i < animals.Count; i++)
            {
                Pawn male = animals[i];
                if (!CanMate(male) || male.gender != Gender.Male)
                {
                    continue;
                }

                float mtbHours = male.RaceProps.mateMtbHours;
                if (mtbHours <= 0f || !Rand.MTBEventOccurs(mtbHours, 2500f, 2500f))
                {
                    continue;
                }

                // Reservoir sampling gives all compatible females the same
                // opportunity without constructing temporary mate lists.
                Pawn chosen = null;
                int eligibleCount = 0;
                for (int j = 0; j < animals.Count; j++)
                {
                    Pawn female = animals[j];
                    if (female == male || female.gender != Gender.Female
                        || !CanMate(female) || !CanPair(male, female)
                        || !PawnUtility.FertileMateTarget(male, female)
                        || female.health.hediffSet.HasHediff(HediffDefOf.Pregnant))
                    {
                        continue;
                    }

                    if (Rand.Range(0, ++eligibleCount) == 0)
                    {
                        chosen = female;
                    }
                }

                if (chosen != null)
                {
                    // The original RimWorld mating routine performs pregnancy,
                    // egg fertilization, genetics and compatibility callbacks.
                    PawnUtility.Mated(male, chosen);
                }
            }
        }

        private static bool CanMate(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed
                && !pawn.Downed && !pawn.InMentalState
                && pawn.ageTracker?.CurLifeStage?.reproductive == true
                && pawn.RaceProps?.disableMating != true
                && !pawn.Sterile();
        }

        private static bool CanPair(Pawn male, Pawn female)
        {
            if (male.Faction != female.Faction)
            {
                return false;
            }

            if (male.def == female.def)
            {
                return true;
            }

            List<ThingDef> allowed = male.RaceProps.canCrossBreedWith;
            return allowed != null && allowed.Contains(female.def);
        }
    }

    // Preserve the original vanilla animal birth, parentage, genes and
    // litter-size logic. Only redirect its placement from the barn's
    // impassable occupied cell to the entrance and collect the born pawns.
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn),
        new[] { typeof(Pawn), typeof(Thing), typeof(IntVec3?) })]
    internal static class Patch_AnimalBarnBornPawn
    {
        private static void Prefix(Thing motherOrEgg, ref IntVec3? positionOverride)
        {
            if (motherOrEgg is Pawn mother
                && ThingOwnerUtility.GetAnyParent<Building_AnimalBarn>(mother)
                    is Building_AnimalBarn barn
                && barn.Spawned)
            {
                positionOverride = barn.InteractionCell;
            }
        }

        private static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result)
        {
            if (__result && pawn != null && motherOrEgg is Pawn mother
                && ThingOwnerUtility.GetAnyParent<Building_AnimalBarn>(mother)
                    is Building_AnimalBarn barn)
            {
                barn.RegisterNewborn(pawn);
            }
        }
    }
}
#endif
