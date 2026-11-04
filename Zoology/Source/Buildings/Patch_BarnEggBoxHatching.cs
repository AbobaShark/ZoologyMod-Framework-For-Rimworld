#if RIMWORLD17
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // CompHatcher.Hatch finishes creating the entire clutch and establishing
    // parent relationships before its postfix runs. This avoids collecting
    // newborns from PawnUtility while vanilla is still assigning their genes
    // and parents. There is no periodic scan or tick-time overhead.
    [HarmonyPatch(typeof(CompHatcher), nameof(CompHatcher.Hatch))]
    internal static class Patch_BarnEggBoxHatching
    {
        private static bool Prepare() => Building_AnimalBarn.Enabled;

        private sealed class HatchContext
        {
            public Map map;
            public Thing eggBox;
            public readonly List<Building_AnimalBarn> barns = new List<Building_AnimalBarn>();
            public readonly HashSet<Pawn> pawnsBefore = new HashSet<Pawn>();
        }

        private static void Prefix(CompHatcher __instance, out HatchContext __state)
        {
            __state = null;
            if (!Building_AnimalBarn.Enabled
                || __instance?.parent?.ParentHolder is not CompEggContainer container)
            {
                return;
            }

            Thing box = container.parent;
            Map map = box?.Map;
            if (map == null || !box.Spawned)
            {
                return;
            }

            // A box can interact only with barns occupying one of its
            // eight neighboring cells. Query the map's native ThingGrid,
            // not the list of every barn on the map. No allocations are
            // made when the box is not adjacent to an eligible barn.
            HatchContext context = null;
            IntVec3 position = box.Position;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    IntVec3 cell = new IntVec3(position.x + dx, 0, position.z + dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    Building_AnimalBarn barn = map.thingGrid.ThingAt<Building_AnimalBarn>(cell);
                    if (barn == null || !AnimalBarnEggUtility.IsAdjacentEggBox(barn, box))
                    {
                        continue;
                    }

                    context ??= new HatchContext { map = map, eggBox = box };
                    if (!context.barns.Contains(barn))
                    {
                        context.barns.Add(barn);
                    }
                }
            }

            if (context == null)
            {
                return;
            }

            // Only the actual hatch event pays for this small snapshot.
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                context.pawnsBefore.Add(pawns[i]);
            }

            __state = context;
        }

        private static void Postfix(HatchContext __state)
        {
            if (__state == null || !Building_AnimalBarn.Enabled
                || __state.eggBox == null || !__state.eggBox.Spawned)
            {
                return;
            }

            // Build a snapshot before storing animals: storing despawns them
            // and would otherwise mutate mapPawns during enumeration.
            var hatched = new List<Pawn>();
            IReadOnlyList<Pawn> pawns = __state.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !__state.pawnsBefore.Contains(pawn)
                    && pawn.Spawned && pawn.Map == __state.map
                    && pawn.Position.InHorDistOf(__state.eggBox.Position, 6f))
                {
                    hatched.Add(pawn);
                }
            }

            for (int i = 0; i < hatched.Count; i++)
            {
                Pawn newborn = hatched[i];
                for (int j = 0; j < __state.barns.Count; j++)
                {
                    if (__state.barns[j].TryStoreHatchedAnimal(
                        newborn, __state.eggBox))
                    {
                        break;
                    }
                }
            }
        }
    }
}
#endif
