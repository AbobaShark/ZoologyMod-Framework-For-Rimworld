#if RIMWORLD17
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Ideology's Ranching: Central thought reads this exact vanilla density
    // calculation in ShouldHaveThought and PostProcessDescription. Its
    // CurrentStateInternal does not enumerate animals: patching that method
    // previously installed an ineffective IL transpiler.
    [HarmonyPatch]
    internal static class Patch_BarnRanchingAnimalMass
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnUtility),
                "PlayerAnimalBodySizePerCapita");
        }

        private static bool Prepare()
        {
            return ModsConfig.IdeologyActive && Building_AnimalBarn.Enabled
                && TargetMethod() != null;
        }

        // Preserve vanilla's own numerator, denominator, thresholds and
        // thought-state rules. Only add the body size of real animals that
        // are absent from PawnsFinder because they are inside a barn.
        private static void Postfix(ref float __result)
        {
            if (!ModsConfig.IdeologyActive || !Building_AnimalBarn.Enabled
                || Find.Maps == null)
                return;

            // These are the very pawns used by the original vanilla method,
            // including colonists in caravans and travelling transporters.
            List<Pawn> playerPawns =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            if (playerPawns == null) return;

            int colonists = 0;
            foreach (Pawn pawn in playerPawns)
                if (pawn != null && pawn.IsFreeColonist && !pawn.IsQuestLodger())
                    colonists++;
            if (colonists == 0) return;

            float additionalMass = 0f;
            HashSet<Pawn> countedPawns = null;
            ThingDef barnDef = Building_AnimalBarn.BarnDef;
            if (barnDef == null) return;
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                List<Thing> barns = map.listerThings.ThingsOfDef(barnDef);
                for (int i = 0; i < barns.Count; i++)
                {
                    if (barns[i] is not Building_AnimalBarn barn
                        || !barn.Spawned || barn.Faction != Faction.OfPlayer)
                        continue;

                    foreach (Pawn animal in barn.Animals)
                    {
                        if (animal == null || animal.Dead || animal.Destroyed
                            || animal.Faction != Faction.OfPlayer
                            || !animal.IsAnimal || animal.ParentHolder != barn)
                            continue;

                        // Hash membership avoids repeated linear searches
                        // through the colony roster for large herds.
                        countedPawns ??= new HashSet<Pawn>(playerPawns);
                        if (!countedPawns.Add(animal))
                            continue;

                        // Ideology uses current size, not the race-size
                        // reservation used for barn capacity.
                        float size = animal.BodySize;
                        if (size > 0f && !float.IsNaN(size) && !float.IsInfinity(size))
                            additionalMass += size;
                    }
                }
            }

            if (additionalMass > 0f)
                __result += additionalMass / colonists;
        }
    }
}
#endif
