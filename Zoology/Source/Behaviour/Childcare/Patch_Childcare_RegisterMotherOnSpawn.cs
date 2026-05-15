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
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Zoology: failed to register childcare mother on spawn: {ex}");
            }
        }
    }
}
