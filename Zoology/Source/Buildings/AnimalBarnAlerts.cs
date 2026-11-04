#if RIMWORLD17
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public sealed class Alert_BarnYoungWithoutMilk : Alert
    {
        private readonly List<Pawn> unsupportedYoung = new List<Pawn>();
        private readonly StringBuilder description = new StringBuilder();
        private int lastCheckedTick = -1;
        private Game cachedGame;

        public Alert_BarnYoungWithoutMilk()
        {
            defaultLabel = "Zoology_BarnYoungWithoutMilk".Translate();
            defaultPriority = AlertPriority.High;
        }

        private List<Pawn> FindUnsupportedYoung()
        {
            int tick = GenTicks.TicksGame;
            if (cachedGame == Current.Game && lastCheckedTick >= 0
                && tick >= lastCheckedTick && tick - lastCheckedTick < 250)
            {
                return unsupportedYoung;
            }

            cachedGame = Current.Game;
            lastCheckedTick = tick;
            unsupportedYoung.Clear();
            if (!Building_AnimalBarn.Enabled || !LactationSettingsGate.Enabled())
            {
                return unsupportedYoung;
            }

            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                {
                    continue;
                }

                foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(map))
                {
                    foreach (Pawn young in barn.Animals)
                    {
                        if (young != null && !young.Dead && young.IsMammal()
                            && AnimalLactationUtility.IsAnimalBaby(young)
                            && !barn.HasSuitableLactatingFemale(young))
                        {
                            unsupportedYoung.Add(young);
                        }
                    }
                }
            }

            return unsupportedYoung;
        }

        public override AlertReport GetReport()
        {
            return AlertReport.CulpritsAre(FindUnsupportedYoung());
        }

        public override TaggedString GetExplanation()
        {
            description.Length = 0;
            foreach (Pawn pawn in FindUnsupportedYoung())
            {
                description.Append("  - ").Append(pawn.LabelShortCap).AppendLine();
            }

            return "Zoology_BarnYoungWithoutMilkDesc".Translate(description.ToString().TrimEnd());
        }
    }

    // Vanilla's starvation alert normally discovers only spawned or world
    // animals. Add the actual contained Pawns to the very same vanilla list.
    // This preserves vanilla timing, label, explanation and culprit selection.
    [HarmonyPatch]
    internal static class Patch_BarnAnimalsInVanillaStarvationAlert
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Alert_StarvationAnimals), "StarvingAnimals");
        }

        private static bool Prepare()
        {
            return Building_AnimalBarn.Enabled && TargetMethod() != null;
        }

        private static void Postfix(ref List<Pawn> __result)
        {
            if (!Building_AnimalBarn.Enabled || __result == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                {
                    continue;
                }

                foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(map))
                {
                    foreach (Pawn animal in barn.Animals)
                    {
                        if (animal == null || animal.Dead || animal.HostFaction != null
                            || animal.needs?.food == null
                            || !(animal.needs.food.TicksStarving > 30000
                                || animal.health.hediffSet.HasHediff(HediffDefOf.Pregnant, mustBeVisible: true)
                                && animal.needs.food.TicksStarving > 5000)
                            || __result.Contains(animal))
                        {
                            continue;
                        }

                        __result.Add(animal);
                    }
                }
            }
        }
    }

    // The vanilla alert checks only CompAnimalPenMarker and hitching posts.
    // A reachable barn is a valid alternative destination for the same roamer.
    // Only the existing alert's culprits are filtered; no global scans are
    // introduced. Cache path checks briefly to avoid repeated GUI work.
    [HarmonyPatch(typeof(Alert_AnimalPenNeeded), nameof(Alert_AnimalPenNeeded.GetReport))]
    internal static class Patch_BarnCountsAsPenForVanillaAlert
    {
        private static bool Prepare() => Building_AnimalBarn.Enabled;

        private static readonly Dictionary<Pawn, bool> suitableBarnCache =
            new Dictionary<Pawn, bool>();
        private static Game cachedGame;
        private static int lastCheckedTick = -1;

        private static void Postfix(ref AlertReport __result)
        {
            if (!__result.active || __result.culpritsTargets == null
                || !Building_AnimalBarn.Enabled)
            {
                return;
            }

            int tick = GenTicks.TicksGame;
            if (cachedGame != Current.Game || lastCheckedTick < 0
                || tick < lastCheckedTick || tick - lastCheckedTick >= 250)
            {
                cachedGame = Current.Game;
                lastCheckedTick = tick;
                suitableBarnCache.Clear();
            }

            List<GlobalTargetInfo> remaining = null;
            bool removed = false;
            List<GlobalTargetInfo> targets = __result.culpritsTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                GlobalTargetInfo target = targets[i];
                Pawn animal = target.Pawn;
                bool covered = false;
                if (animal != null)
                {
                    if (!suitableBarnCache.TryGetValue(animal, out covered))
                    {
                        covered = AnimalBarnUtility.CanUseBarnAsPen(animal);
                        suitableBarnCache[animal] = covered;
                    }
                }

                if (covered)
                {
                    removed = true;
                    continue;
                }

                remaining ??= new List<GlobalTargetInfo>(targets.Count);
                remaining.Add(target);
            }

            if (removed)
            {
                __result = AlertReport.CulpritsAre(remaining ??
                    new List<GlobalTargetInfo>());
            }
        }
    }

    internal static class AnimalBarnUtility
    {
        // The roper supplies traversal parameters so fence-blocked animals
        // can follow the same paths they use for vanilla pen handling.
        public static bool CanReachBarn(Pawn animal, Building_AnimalBarn barn,
            Pawn handler = null)
        {
            if (animal?.Map == null || barn?.Map != animal.Map)
            {
                return false;
            }

            TraverseParms traversal = handler != null
                ? TraverseParms.For(handler).WithFenceblockedOf(animal)
                : TraverseParms.For(TraverseMode.PassDoors).WithFenceblockedOf(animal);
            return animal.Map.reachability.CanReach(
                animal.Position, barn.InteractionCell, PathEndMode.OnCell, traversal);
        }

        public static bool CanUseBarnAsPen(Pawn animal)
        {
            if (!Building_AnimalBarn.Enabled || animal?.Map == null
                || !AnimalPenUtility.NeedsToBeManagedByRope(animal))
            {
                return false;
            }

            bool hasHandler = false;
            List<Pawn> handlers = animal.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < handlers.Count; i++)
            {
                Pawn worker = handlers[i];
                if (worker.workSettings?.WorkIsActive(WorkTypeDefOf.Handling) == true)
                {
                    hasHandler = true;
                    break;
                }
            }

            if (!hasHandler)
            {
                return false;
            }

            foreach (Building_AnimalBarn barn in BarnsOnMap(animal.Map))
            {
                if (!barn.IsForbidden(Faction.OfPlayer)
                    && barn.CanAcceptAnimal(animal)
                    && CanReachBarn(animal, barn))
                {
                    return true;
                }
            }

            return false;
        }

        public static IEnumerable<Building_AnimalBarn> BarnsOnMap(Map map)
        {
            ThingDef def = Building_AnimalBarn.BarnDef;
            if (map == null || def == null)
            {
                yield break;
            }

            List<Thing> barns = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < barns.Count; i++)
            {
                if (barns[i] is Building_AnimalBarn barn && barn.Spawned)
                {
                    yield return barn;
                }
            }
        }
    }
}
#endif
