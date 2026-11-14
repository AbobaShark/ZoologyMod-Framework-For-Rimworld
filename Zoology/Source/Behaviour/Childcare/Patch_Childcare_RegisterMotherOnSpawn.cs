using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn), new[] { typeof(Pawn), typeof(Thing), typeof(IntVec3?) })]
    internal static class Patch_Childcare_RegisterMotherOnSpawn
    {
        public static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result)
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
