using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn), new[] { typeof(Pawn), typeof(Thing), typeof(IntVec3?) })]
    internal static class Patch_Childcare_RegisterMotherOnSpawn
    {
        private static void Prefix(Thing motherOrEgg, ref IntVec3? __2,
            out Building_AnimalBurrow __state)
        {
            __state = null;
            if (motherOrEgg is Pawn mother
                && ThingOwnerUtility.GetAnyParent<Building_AnimalBurrow>(mother) is Building_AnimalBurrow den
                && den.PrepareContainedBirth(mother, out IntVec3 cell))
            {
                __state = den;
                __2 = cell;
            }
        }

        public static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result,
            Building_AnimalBurrow __state)
        {
            try
            {
                if (!__result || pawn == null || motherOrEgg == null || !pawn.IsAnimal)
                {
                    return;
                }

                Pawn mother = null;
                if (motherOrEgg is Pawn pawnMother && pawnMother.gender == Gender.Female)
                {
                    mother = pawnMother;
                }
                else
                {
                    mother = motherOrEgg.TryGetComp<CompHatcher>()?.hatcheeParent;
                    if (mother == null)
                    {
                        EggClutchDefenseGameComponent.Instance?.TryGetAnyMother(motherOrEgg, out mother);
                    }
                }

                ChildcareUtility.RegisterObservedMother(pawn, mother);
                if (ChildcareUtility.UsesBiparentalProtection(pawn.def)
                    && pawn.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState state
                    && motherOrEgg.TryGetComp<CompHatcher>()?.otherParent is Pawn father)
                    state.offspringProtectionFather = father;
                if (__state != null && mother != null)
                {
                    if (__state.ReceiveNewborn(mother, pawn)) return;
                    __state.ReleaseMotherForBirth(mother);
                }
                if (mother != null && AnimalEffectiveArea.TryGetFollowingMother(pawn, out Pawn currentMother)
                    && ReferenceEquals(currentMother, mother))
                {
                    AnimalEffectiveArea.SyncWithMother(pawn, mother);
                }
                // A newborn may have been spawned next to a barn containing
                // its mother. Both kinds of owner preserve the newborn Pawn.
                if (mother != null && mother.gender == Gender.Female
                    && AnimalOffspringShelter.For(mother.def) == OffspringShelterMode.POUCH)
                    mother.TryGetComp<CompAnimalPouch>()?.TryStoreBaby(pawn);
                else if (motherOrEgg is Thing egg && egg.ParentHolder is CompEggContainer eggs
                    && eggs.parent is Building_AnimalNursery nest)
                {
                    CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
                    nest.AssignParent(hatcher?.hatcheeParent);
                    nest.AssignParent(hatcher?.otherParent);
                    nest.TryReceiveBaby(pawn, egg);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Zoology: failed to register childcare mother on spawn: {ex}");
            }
        }
    }
}
