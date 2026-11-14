using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public enum PredationStrategy { No, Chasing, WaterAmbush, Ambush }
    public class ModExtension_PredationStrategy : DefModExtension
    {
        public PredationStrategy strategy;
        // Optional: restrict a strategy to specified life stage indices.
        // Unspecified means all stages, preserving existing animal definitions.
        public List<int> lifeStageIndices;
        public override IEnumerable<string> ConfigErrors()
        {
            if (!Enum.IsDefined(typeof(PredationStrategy), strategy))
                yield return "Invalid Zoology predation strategy";
            if (lifeStageIndices != null)
                foreach (int stage in lifeStageIndices)
                    if (stage < 0)
                        yield return "Predation strategy lifeStageIndices must be nonnegative";
        }
    }
    public class ModExtension_Burrower : DefModExtension { public float maxBodySize = 2f; }
    public class ModExtension_TreeClimber : DefModExtension { public float maxBodySize = 2f; public float minTreeGrowth = 0.65f; }
    public class ModExtension_Constrictor : DefModExtension
    {
        public float maxPreySizeRatio = 1f;
        public float severityPerSecond = 0.025f;
        public float ectothermFactor = 0.25f;
    }

    // Event-driven state: no CompTick or extra pawn scans. References are owned by the pawn's save.
    public class CompAnimalStrategyState : ThingComp
    {
        public Pawn ambushPrey;
        public int ambushPhase, ambushUntil, sprintStart;
        public float ambushApproachDistance = 1f;
        public bool revealed;
        public Pawn lastRangedSource;
        public int lastRangedTick = -60000;
        public List<Building_AnimalBurrow> familiarBurrows = new List<Building_AnimalBurrow>();
        public Pawn environmentThreat;
        public bool chosenWater;
        public IntVec3 environmentTarget = IntVec3.Invalid;
        public int environmentUntil, nextDefensePlan, nextRestPlan, nextDigPlan;
        public Thing shelter;
        public Building_AnimalNursery nursery; // Legacy nests and pre-refactor dens only.
        public Building_AnimalBurrow familyBurrow;
        public int nextNurseryPlan, nextRoeFertilizationPlan;
        public bool inWinterDormancy;
        public int lastWinterWakeYear = int.MinValue;
        public int nextWinterDecision;
        public bool groundEscapeUntilFed;
        public int burrowReentryAfter, nextBurrowHuntPlan;
        public int nextWildHarvestPlan, nextWildForagePlan, nextNaturalFoodScan;
        public int fleeingSince = -1;
        public Pawn fleeThreat;
        public Pawn constrictedPrey;

        // The tree is selected through the animal's existing strategy comp.
        // No additional per-pawn component or simulated containment is needed.
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Plant tree = AnimalTreeSelection.OccupiedTree(parent as Pawn);
            if (tree == null) yield break;
            Gizmo select = AnimalTreeSelection.SelectTree(tree);
            if (select != null) yield return select;
        }

        public override void PostExposeData()
        {
            Scribe_References.Look(ref ambushPrey, "ambushPrey");
            Scribe_Values.Look(ref ambushPhase, "ambushPhase");
            Scribe_Values.Look(ref ambushUntil, "ambushUntil");
            Scribe_Values.Look(ref ambushApproachDistance, "ambushApproachDistance", 1f);
            Scribe_Values.Look(ref sprintStart, "sprintStart");
            Scribe_Values.Look(ref revealed, "ambushRevealed");
            Scribe_References.Look(ref lastRangedSource, "lastRangedSource");
            Scribe_Values.Look(ref lastRangedTick, "lastRangedTick", -60000);
            Scribe_Collections.Look(ref familiarBurrows, "familiarBurrows", LookMode.Reference);
            Scribe_References.Look(ref environmentThreat, "environmentThreat");
            Scribe_Values.Look(ref chosenWater, "chosenWater");
            Scribe_Values.Look(ref environmentTarget, "environmentTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref environmentUntil, "environmentUntil");
            Scribe_References.Look(ref shelter, "defenseShelter");
            Scribe_References.Look(ref nursery, "animalNursery");
            Scribe_References.Look(ref familyBurrow, "zoologyFamilyBurrow");
            Scribe_Values.Look(ref inWinterDormancy, "inWinterDormancy");
            Scribe_Values.Look(ref lastWinterWakeYear, "lastWinterWakeYear", int.MinValue);
            Scribe_Values.Look(ref groundEscapeUntilFed, "groundEscapeUntilFed");
            Scribe_Values.Look(ref burrowReentryAfter, "burrowReentryAfter");
            Scribe_Values.Look(ref fleeingSince, "fleeingSince", -1);
            Scribe_References.Look(ref fleeThreat, "fleeThreat");
            Scribe_References.Look(ref constrictedPrey, "constrictedPrey");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                familiarBurrows = familiarBurrows ?? new List<Building_AnimalBurrow>();
                familiarBurrows.RemoveAll(b => b == null || b.Destroyed);
                nextDefensePlan = 0;
                nextNurseryPlan = 0;
                nextRoeFertilizationPlan = 0;
                nextWinterDecision = 0;
                nextWildHarvestPlan = 0;
                nextWildForagePlan = 0;
                nextNaturalFoodScan = 0;
            }
        }
        public void Remember(Building_AnimalBurrow burrow)
        {
            familiarBurrows.RemoveAll(b => b == null || b.Destroyed);
            familiarBurrows.Remove(burrow);
            familiarBurrows.Insert(0, burrow);
            if (familiarBurrows.Count > 8) familiarBurrows.RemoveAt(8);
        }
    }

    [StaticConstructorOnStartup]
    internal static class AnimalStrategyStateBootstrap
    {
        static AnimalStrategyStateBootstrap()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race?.Animal != true) continue;
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (!def.comps.Exists(c => c.compClass == typeof(CompAnimalStrategyState)))
                    def.comps.Add(new CompProperties(typeof(CompAnimalStrategyState)));
            }
        }
    }

    internal static class AnimalLocomotion
    {
        private struct SpeedSlot
        {
            public int tick, revision, pawnId, mapId, cell;
            public bool flying;
            public float speed;
        }
        // Fixed-size, allocation-free memoization only within one simulation tick.
        // Health changes invalidate it immediately; no stale 90-tick prey-score modifier.
        private static readonly SpeedSlot[] speeds = new SpeedSlot[2048];
        private static int revision = 1;
        private static Game cacheGame;
        internal static void Invalidate() { unchecked { revision++; } }
        // Mirrors cardinal Pawn_PathFollower cost, including path-grid terrain/snow and water override.
        // Urgency is Jog for both contestants. Flight changes payment per tick, not waterCellCost.
        public static float SpeedAt(Pawn pawn, IntVec3 cell, bool availableFlight = true)
        {
            if (pawn?.Spawned != true || !cell.InBounds(pawn.Map)) return 0f;
            bool flying = pawn.Flying || (availableFlight && pawn.flight?.CanFlyNow == true);
            if (!cell.Walkable(pawn.Map) && !flying) return 0f;
            if (cacheGame != Current.Game) { cacheGame = Current.Game; Invalidate(); }
            int tick = GenTicks.TicksGame, mapId = pawn.Map.uniqueID, cellIndex = pawn.Map.cellIndices.CellToIndex(cell);
            int slotIndex = unchecked((pawn.thingIDNumber * 397 ^ cellIndex) & (speeds.Length - 1));
            SpeedSlot cached = speeds[slotIndex];
            if (cached.revision == revision && cached.tick == tick && cached.pawnId == pawn.thingIDNumber
                && cached.mapId == mapId && cached.cell == cellIndex && cached.flying == flying) return cached.speed;
            TerrainDef terrain = cell.GetTerrain(pawn.Map);
            int? water = terrain.IsWater ? pawn.WaterCellCost : null;
            float cost = pawn.TicksPerMoveCardinal
                + pawn.Map.pathing.For(pawn).pathGrid.CalculatedCostAt(cell, false, pawn.Position, water);
            Building building = cell.GetEdifice(pawn.Map);
            if (building != null) cost += building.PathWalkCostFor(pawn);
            if (terrain.tags != null)
                foreach (string tag in terrain.tags)
                    if (pawn.kindDef.moveSpeedFactorByTerrainTag.TryGetValue(tag, out float factor) && factor > 0f)
                        cost /= factor;
            cost = Mathf.Min(cost, 450f);
            float payment = pawn.stances.stagger.Staggered ? pawn.stances.stagger.StaggerMoveSpeedFactor : 1f;
            if (flying) payment *= pawn.RaceProps.flightSpeedFactor;
            payment = Mathf.Max(payment, cost / 450f);
            float speed = 60f * payment / Mathf.Max(cost, 1f);
            speeds[slotIndex] = new SpeedSlot { tick = tick, revision = revision, pawnId = pawn.thingIDNumber,
                mapId = mapId, cell = cellIndex, flying = flying, speed = speed };
            return speed;
        }
        public static bool TrySample(Pawn pawn, bool water, IntVec3 center, float radius, out IntVec3 cell)
        {
            if (center.InBounds(pawn.Map) && center.GetTerrain(pawn.Map).IsWater == water
                && pawn.Map.pathing.For(pawn).pathGrid.WalkableFast(center)) { cell = center; return true; }
            if (water && radius > 3f && pawn.Map.GetComponent<AnimalRefugeMapComponent>() is AnimalRefugeMapComponent index)
                return index.TryWaterCell(pawn, center, radius, out cell);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(center, radius, true))
                if (c.InBounds(pawn.Map) && pawn.Map.pathing.For(pawn).pathGrid.WalkableFast(c) && c.GetTerrain(pawn.Map).IsWater == water)
                { cell = c; return true; }
            cell = IntVec3.Invalid;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged))]
    internal static class Patch_LocomotionHealthInvalidation
    {
        static void Postfix() => AnimalLocomotion.Invalidate();
    }

    [HarmonyPatch(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain))]
    internal static class Patch_LocomotionTerrainInvalidation
    {
        static void Postfix(Map ___map, IntVec3 c, TerrainDef newTerr)
        {
            AnimalLocomotion.Invalidate();
            ___map.GetComponent<AnimalRefugeMapComponent>()?.UpdateWater(c, newTerr.IsWater);
        }
    }

    internal static class AnimalHuntStrategy
    {
        public static PredationStrategy For(Pawn pawn)
        {
            if (pawn?.RaceProps.predator != true)
                return PredationStrategy.No;

            ModExtension_PredationStrategy extension =
                DefModExtensionCache<ModExtension_PredationStrategy>.Get(pawn.def);
            if (extension == null)
                return PredationStrategy.No;

            List<int> stages = extension.lifeStageIndices;
            if (stages != null && (pawn.ageTracker == null
                || !stages.Contains(pawn.ageTracker.CurLifeStageIndex)))
                return PredationStrategy.No;

            return extension.strategy;
        }
        public static bool IsHunt(Pawn pawn, Pawn prey) => pawn?.RaceProps.predator == true
            && pawn.CurJob?.targetA.Thing == prey
            && (pawn.jobs.curDriver is JobDriver_PredatorHunt || pawn.jobs.curDriver is JobDriver_Constrict);
        public static int ChaseLimit(Pawn pawn) => For(pawn) == PredationStrategy.Ambush || For(pawn) == PredationStrategy.WaterAmbush
            ? ZoologyTickLimiter.Pursuit.ChaseTimeoutTicks / 2 : ZoologyTickLimiter.Pursuit.ChaseTimeoutTicks;
        // A chase is unnecessary when prey is much slower. Otherwise a predator
        // must approach closely enough to attack before a faster animal can escape.
        public static float DesiredApproachDistance(Pawn predator, Pawn prey)
        {
            float hunterSpeed = AnimalLocomotion.SpeedAt(predator, prey.Position);
            float preySpeed = AnimalLocomotion.SpeedAt(prey, prey.Position);
            if (hunterSpeed <= 0f || preySpeed <= 0f) return 0f;
            if (preySpeed < hunterSpeed * 0.65f) return 0f;

            float approach = Mathf.Clamp(1f + 3f * hunterSpeed / preySpeed, 1f, 6f);
            if (prey.flight?.CanFlyNow == true || prey.Flying)
                approach = Mathf.Min(approach, 1.5f);

            // The refuge index is already maintained for fleeing animals. Inspect
            // nearby candidates once at the start, never on every stalking tick.
            var index = prey.Map?.GetComponent<AnimalRefugeMapComponent>();
            if (index != null && AnimalDefense.CanUseShelter(prey))
            {
                float nearest = float.MaxValue;
                bool climber = DefModExtensionCache<ModExtension_TreeClimber>.Has(prey);
                bool burrower = DefModExtensionCache<ModExtension_Burrower>.Has(prey);
                if (climber || burrower)
                    foreach (Thing refuge in index.Nearby(prey.Position, 12))
                    {
                        bool usable = (climber && refuge is Plant tree && AnimalDefense.UsableTree(prey, tree))
                            || (burrower && refuge is Building_AnimalBurrow burrow && burrow.Accepts(prey));
                        if (!usable) continue;
                        float distance = (refuge.Position - prey.Position).LengthHorizontal;
                        if (distance < nearest) nearest = distance;
                    }
                if (nearest < float.MaxValue)
                    approach = Mathf.Min(approach, Mathf.Max(1f, nearest - 1f));
            }
            return approach;
        }

        // At most four path queries, only when starting or replanning a stalk.
        public static bool TryFindApproachCell(Pawn predator, Pawn prey, float distance, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (predator?.Map == null || predator.Map != prey?.Map) return false;
            var candidates = new List<IntVec3>(8);
            var scores = new List<float>(8);
            float currentDistanceSq = (predator.Position - prey.Position).LengthHorizontalSquared;
            // The stalking job switches to a hunt only at <= distance. Picking
            // destinations outside that radius leaves the hunter circling the
            // prey without ever satisfying its own completion condition.
            float innerDistance = Mathf.Max(1f, distance - 1f);
            float innerDistanceSq = innerDistance * innerDistance;
            float outerDistanceSq = distance * distance;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(prey.Position, distance + 1f, false))
            {
                float rangeSq = (cell - prey.Position).LengthHorizontalSquared;
                if (rangeSq < innerDistanceSq || rangeSq > outerDistanceSq
                    || !cell.InBounds(prey.Map) || !cell.Standable(prey.Map) || cell.IsForbidden(predator))
                    continue;
                float pathEstimate = (cell - predator.Position).LengthHorizontalSquared;
                if (pathEstimate >= currentDistanceSq && currentDistanceSq > (distance + 2f) * (distance + 2f))
                    continue;
                int insert = 0;
                while (insert < scores.Count && pathEstimate >= scores[insert]) insert++;
                if (insert >= 8) continue;
                candidates.Insert(insert, cell);
                scores.Insert(insert, pathEstimate);
                if (candidates.Count > 8) { candidates.RemoveAt(8); scores.RemoveAt(8); }
            }
            for (int i = 0; i < Mathf.Min(4, candidates.Count); i++)
                if (predator.CanReach(candidates[i], PathEndMode.OnCell, Danger.Some))
                {
                    result = candidates[i];
                    return true;
                }
            return false;
        }

        // Pure per-tick hazard. A target and all members of its local
        // conspecific herd use this same perception model.
        // Measured displacement replaces the old moving/not-moving boolean:
        // slower movement exposes less motion per observation interval.
        private static float StalkingDetectionHazard(Pawn predator, Pawn observer,
            float measuredMovement)
        {
            if (predator?.Spawned != true || observer?.Spawned != true
                || observer.Dead || observer.Downed || !observer.Awake()
                || predator.Map != observer.Map)
                return 0f;

            float distance = (predator.Position - observer.Position).LengthHorizontal;
            float size = Mathf.Sqrt(Mathf.Max(0.05f, predator.BodySize));
            float proximity = Mathf.Lerp(0.25f, 2.5f,
                Mathf.Clamp01((10f - distance) / 9f));
            float ectotherm = ZoologyCacheUtility.HasEctothermicExtension(predator.def)
                ? 0.35f : 1f;
            float visibility = GenSight.LineOfSight(predator.Position,
                observer.Position, predator.Map) ? 1f : 0.2f;
            return 0.00015f * size * proximity * ectotherm
                * measuredMovement * visibility;
        }

        // Keep the original stationary baseline of 0.3. Continuous movement
        // approaches the old 1.0 exposure at 3 cells/second; actual movement
        // in cells is taken from the stalk driver's existing 120-tick samples.
        private static float StalkingMovementExposure(float movedCells, int elapsedTicks)
        {
            float cellsPerSecond = movedCells * 60f / Mathf.Max(1, elapsedTicks);
            return Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(cellsPerSecond / 3f));
        }

        internal static bool RecentlyFledFrom(Pawn observer, Pawn predator)
        {
            var escape = observer?.TryGetComp<CompAnimalStrategyState>();
            return escape?.fleeThreat == predator && escape.fleeingSince >= 0
                && GenTicks.TicksGame - escape.fleeingSince < 3000;
        }

        // The intended prey and its local herd use one capped, correlated
        // detection roll. Other species may notice the same stalker on their
        // own, but never reveal it for that prey or end the stalk.
        internal static bool TryDetectStalkingPredator(Pawn predator, Pawn target,
            int elapsedTicks, float movedCells, out Pawn herdWitness,
            out Pawn independentWitness)
        {
            herdWitness = null;
            independentWitness = null;
            if (predator?.Spawned != true || target?.Spawned != true
                || predator.Map != target.Map || predator == target)
                return false;

            if (RecentlyFledFrom(target, predator))
            {
                herdWitness = target;
                return true;
            }

            float motion = StalkingMovementExposure(movedCells, elapsedTicks);
            float targetHazard = StalkingDetectionHazard(predator, target, motion);
            float strongest = targetHazard;
            float sumSquares = targetHazard * targetHazard;
            Pawn mostExposed = targetHazard > 0f ? target : null;

            var pawns = predator.Map.mapPawns.AllPawnsSpawned;
            int count = pawns.Count;
            int start = count > 0
                ? (int)((uint)(predator.thingIDNumber
                    + GenTicks.TicksGame / 120) % (uint)count) : 0;
            int herdChecks = 0;
            int independentChecks = 0;
            bool hasHerd = target.RaceProps?.herdAnimal == true;
            float radius = Patch_AnimalFleeFromPredators.GetPredatorSearchRadius();
            float radiusSq = radius * radius;
            // Only existing 120-tick stalk checks reach this loop. At most
            // 16 herd LoS queries plus 8 unrelated LoS queries are performed.
            for (int n = 0; n < count
                && ((hasHerd && herdChecks < 16)
                    || (independentWitness == null && independentChecks < 8)); n++)
            {
                Pawn observer = pawns[(start + n) % count];
                if (observer == null || observer == predator || observer == target
                    || observer.RaceProps?.Animal != true
                    || !observer.RaceProps.canBePredatorPrey
                    || !observer.Spawned || observer.Dead || observer.Downed
                    || !observer.Awake() || observer.def == predator.def
                    || (predator.Faction != null && observer.Faction == predator.Faction))
                    continue;

                bool inTargetHerd = hasHerd && observer.def == target.def
                    && observer.Faction == target.Faction
                    && (observer.Position - target.Position).LengthHorizontalSquared <= radiusSq;

                if (inTargetHerd)
                {
                    if (herdChecks >= 16) continue;
                    if (RecentlyFledFrom(observer, predator))
                    {
                        herdWitness = observer;
                        return true;
                    }

                    herdChecks++;
                    float hazard = StalkingDetectionHazard(predator, observer, motion);
                    sumSquares += hazard * hazard;
                    if (hazard > strongest)
                    {
                        strongest = hazard;
                        mostExposed = observer;
                    }
                }
                else if (observer.def != target.def
                    && independentWitness == null && independentChecks < 8
                    && (observer.Position - predator.Position).LengthHorizontalSquared <= radiusSq
                    && !RecentlyFledFrom(observer, predator))
                {
                    independentChecks++;
                    float hazard = StalkingDetectionHazard(predator, observer, motion);
                    if (hazard > 0f && Rand.Chance(
                        1f - Mathf.Exp(-hazard * Mathf.Max(1, elapsedTicks))))
                        independentWitness = observer;
                }
            }

            if (strongest <= 0f)
                return false;
            float groupHazard = Mathf.Min(Mathf.Sqrt(sumSquares), 2f * strongest);
            if (!Rand.Chance(1f - Mathf.Exp(-groupHazard * Mathf.Max(1, elapsedTicks))))
                return false;

            herdWitness = mostExposed;
            return herdWitness != null;
        }

        public static bool HiddenFrom(Pawn predator, Pawn prey)
        {
            PredationStrategy strategy = For(predator);
            if (strategy != PredationStrategy.Ambush && strategy != PredationStrategy.WaterAmbush) return false;
            if (predator == null || prey == null || predator.InMentalState
                || !predator.Spawned || predator.Map != prey.MapHeld)
                return false;
            // PositionHeld/MapHeld also work for burrow occupants; spawned-only
            // visibility would make hidden water ambushers appear fully known
            // as soon as their potential prey entered a burrow.
            if (strategy == PredationStrategy.WaterAmbush)
                return predator.Position.GetTerrain(predator.Map).IsWater
                    && !prey.PositionHeld.GetTerrain(predator.Map).IsWater;

            CompAnimalStrategyState state = predator.TryGetComp<CompAnimalStrategyState>();
            // Concealment is a property of the stalking predator, not a
            // privilege granted to only its intended target. All potential
            // victims must share the same detection/reveal state.
            if (state?.ambushPrey == null || state.revealed
                || state.ambushUntil <= GenTicks.TicksGame)
                return false;

            // The existing flee memory is also an awareness record. A herd
            // member that escaped this predator cannot immediately forget
            // it when the predator switches to another nearby target.
            if (RecentlyFledFrom(prey, predator))
                return false;
            return state.ambushPhase == 1
                && predator.jobs?.curDriver is JobDriver_AnimalStalk
                && predator.CurJob?.targetA.Pawn == state.ambushPrey;
        }
        public static bool IsFleeing(Job job) => job?.def == JobDefOf.Flee || job?.def.defName == "Zoology_EnvironmentFlee";
        // Shared speed criterion for abandoning an already escaping quarry
        // and deciding whether a prematurely detected stalk can become a chase.
        internal static bool OutrunsPredator(Pawn predator, Pawn prey)
        {
            if (predator?.Spawned != true || prey?.Spawned != true
                || prey.Downed || !prey.Awake()
                || predator.Map != prey.Map
                || predator.Position.InHorDistOf(prey.Position, 2f))
                return false;

            bool recentlyEscaped = RecentlyFledFrom(prey, predator);
            float preySpeed = AnimalLocomotion.SpeedAt(prey, prey.Position, recentlyEscaped);
            return preySpeed > AnimalLocomotion.SpeedAt(predator, prey.Position) * 1.5f
                && preySpeed > AnimalLocomotion.SpeedAt(predator, predator.Position) * 1.5f;
        }

        public static bool HopelessPursuit(Pawn predator, Pawn prey)
        {
            if (For(predator) == PredationStrategy.No || prey == null)
                return false;
            bool recentlyEscaped = RecentlyFledFrom(prey, predator);
            return (IsFleeing(prey.CurJob) || recentlyEscaped)
                && OutrunsPredator(predator, prey);
        }
        public static bool WaterPreyAllowed(Pawn predator, Pawn prey)
        {
            if (For(predator) != PredationStrategy.WaterAmbush || !prey.Awake()) return true;
            float hunter = AnimalLocomotion.SpeedAt(predator, prey.Position);
            float runner = AnimalLocomotion.SpeedAt(prey, prey.Position);
            if (runner <= hunter) return true;
            // A real walkable water cell at <=2 cells, not a terrain-name approximation of shore.
            return AnimalLocomotion.TrySample(predator, true, prey.Position, 2f, out _);
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetPreyScoreFor), new[] { typeof(Pawn), typeof(Pawn) })]
    internal static class Patch_StrategyPreyScore
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn predator, Pawn prey, ref float __result)
        {
            if (predator?.Spawned != true || prey?.Spawned != true
                || AnimalHuntStrategy.For(predator) != PredationStrategy.Chasing
                || !prey.Awake()) return;
            IntVec3 cell = prey.Position;
            float predatorSpeed = AnimalLocomotion.SpeedAt(predator, cell);
            float preySpeed = AnimalLocomotion.SpeedAt(prey, cell);
            if (predatorSpeed <= 0f || preySpeed <= predatorSpeed) return;

            // Scale the existing prey benefit, never the distance penalty.
            // Otherwise fast prey farther away can gain a higher score.
            float distance = (predator.Position - prey.Position).LengthHorizontal;
            float preyBenefit = __result + distance;
            if (preyBenefit > 0f)
                __result = preyBenefit * Mathf.Clamp01(predatorSpeed / preySpeed) - distance;
        }
    }
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor), new[] { typeof(Pawn), typeof(Pawn) })]
    internal static class Patch_StrategyPreyAccess
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn predator, Pawn prey, ref bool __result)
        {
            bool inspectingOccupant = prey != null && AnimalBurrowHunt.EvaluatingOccupant == prey;
            if (__result && ((prey?.Spawned != true && !inspectingOccupant) || AnimalDefense.BlocksMelee(predator, prey)
                || (!inspectingOccupant && !AnimalHuntStrategy.WaterPreyAllowed(predator, prey)))) __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class Patch_StartAnimalHuntStrategy
    {
        static void Prefix(Pawn ___pawn, ref Job newJob)
        {
            if (newJob?.def != JobDefOf.PredatorHunt || !(newJob.targetA.Thing is Pawn prey)
                || AnimalHuntStrategy.For(___pawn) != PredationStrategy.Ambush) return;
            if (ZoologyPursuitGameComponent.Instance?.IsPairBlockedNow(___pawn, prey) == true) return;
            CompAnimalStrategyState state = ___pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || (state.ambushPrey == prey && state.ambushUntil > GenTicks.TicksGame)) return;

            float distance = AnimalHuntStrategy.DesiredApproachDistance(___pawn, prey);
            if (distance <= 0f) return; // A much slower prey needs no stalking.
            state.ambushPrey = prey;
            state.ambushPhase = 2;
            state.ambushUntil = GenTicks.TicksGame + 10000;
            state.revealed = true;
            state.ambushApproachDistance = distance;

            if (___pawn.Position.InHorDistOf(prey.Position, distance))
            {
                state.ambushUntil = GenTicks.TicksGame + AnimalHuntStrategy.ChaseLimit(___pawn);
                return;
            }
            if (!AnimalHuntStrategy.TryFindApproachCell(___pawn, prey, distance, out IntVec3 cell))
            {
                // An inaccessible striking position must not silently turn a
                // slow ambusher into a futile open-ground pursuit.
                state.ambushPhase = 0;
                state.ambushUntil = 0;
                state.revealed = true;
                ZoologyPursuitGameComponent.Instance?.BlockPursuit(___pawn, prey);
                newJob = JobMaker.MakeJob(JobDefOf.Wait, ZoologyTickLimiter.Pursuit.StopPredatorWaitTicks);
                return;
            }
            state.ambushPhase = 1;
            state.revealed = false;
            newJob = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_AnimalStalk"), prey, cell);
            newJob.locomotionUrgency = LocomotionUrgency.Amble;
        }

        static void Postfix(Pawn ___pawn)
        {
            var state = ___pawn?.TryGetComp<CompAnimalStrategyState>();
            if (state != null && AnimalHuntStrategy.IsFleeing(___pawn.CurJob))
            {
                Pawn source = ___pawn.CurJob.targetB.Pawn;
                if (state.fleeingSince < 0 || state.fleeThreat != source || GenTicks.TicksGame - state.fleeingSince >= 3000)
                    state.fleeingSince = GenTicks.TicksGame;
                state.fleeThreat = source;
            }
            if (___pawn?.CurJob?.def == JobDefOf.PredatorHunt && AnimalHuntStrategy.For(___pawn) != PredationStrategy.No
                && ___pawn.flight?.CanFlyNow == true) ___pawn.flight.StartFlying();
        }
    }

    public class JobDriver_AnimalStalk : JobDriver
    {
        private int nextCheckTick;
        private int lastCheckTick;
        private int nextRepathTick;
        private int lastClosingProgressTick;
        private float closestPreyDistance;
        private IntVec3 lastPreyPosition = IntVec3.Invalid;
        private IntVec3 lastDetectionPosition = IntVec3.Invalid;
        private const int StalkNoClosingProgressLimitTicks = 1200;
        private const float MeaningfulClosingProgress = 0.75f;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed || pawn.InMentalState || TargetA.Pawn.Dead);
            Toil stalk = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            stalk.initAction = () =>
            {
                lastPreyPosition = TargetA.Pawn.Position;
                lastDetectionPosition = pawn.Position;
                lastCheckTick = GenTicks.TicksGame;
                lastClosingProgressTick = lastCheckTick;
                closestPreyDistance = (pawn.Position - lastPreyPosition).LengthHorizontal;
                nextCheckTick = lastCheckTick;
                nextRepathTick = lastCheckTick + 300;
                if (TargetB.Cell.IsValid && TargetB.Cell != pawn.Position)
                    pawn.pather.StartPath(TargetB.Cell, PathEndMode.OnCell);
            };
            stalk.tickAction = () =>
            {
                int now = GenTicks.TicksGame;
                if (now < nextCheckTick) return;
                int elapsed = Mathf.Max(1, now - lastCheckTick);
                float movedCells = lastDetectionPosition.IsValid
                    ? (pawn.Position - lastDetectionPosition).LengthHorizontal : 0f;
                lastDetectionPosition = pawn.Position;
                lastCheckTick = now;
                nextCheckTick = now + 120;

                Pawn prey = TargetA.Pawn;
                CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
                if (prey?.Spawned != true || state == null || state.ambushPrey != prey
                    || state.revealed || now >= state.ambushUntil || AnimalDefense.IsInTree(prey))
                {
                    FailAmbush(prey);
                    return;
                }
                // A conspecific member of the target's local herd may spot
                // the stalker first. Other species cannot reveal it for them.
                bool detectedByPreyHerd = AnimalHuntStrategy.TryDetectStalkingPredator(
                    pawn, prey, elapsed, movedCells, out Pawn witness,
                    out Pawn unrelatedWitness);
                if (detectedByPreyHerd)
                {
                    DetectedAmbush(prey, witness);
                    return;
                }

                // The witness may flee and become temporarily unavailable as
                // prey without revealing the stalker to its current quarry.
                if (unrelatedWitness != null)
                    Patch_AnimalFleeFromPredators.AlertIndependentStalkWitness(
                        pawn, unrelatedWitness);

                float approachDistance = Mathf.Max(1f, state.ambushApproachDistance);
                float currentDistance = (pawn.Position - prey.Position).LengthHorizontal;
                if (currentDistance <= approachDistance)
                {
                    state.ambushPhase = 2;
                    state.revealed = true;
                    state.ambushUntil = now + AnimalHuntStrategy.ChaseLimit(pawn);
                    pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.PredatorHunt, prey));
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                // If a faster prey continually maintains the same gap, the
                // hunter must not perpetually follow it in concealed stalk mode.
                // Reset the deadline only after measurable net closing, not
                // merely because the hunter moved or repathed.
                if (currentDistance <= closestPreyDistance - MeaningfulClosingProgress)
                {
                    closestPreyDistance = currentDistance;
                    lastClosingProgressTick = now;
                }
                if (now - lastClosingProgressTick >= StalkNoClosingProgressLimitTicks)
                {
                    FailAmbush(prey);
                    return;
                }

                if (now < nextRepathTick) return;
                bool preyMoved = (prey.Position - lastPreyPosition).LengthHorizontalSquared >= 4;
                if (!preyMoved && pawn.pather.Moving) return;
                nextRepathTick = now + 300;
                if (!AnimalHuntStrategy.TryFindApproachCell(pawn, prey,
                    approachDistance, out IntVec3 cell))
                {
                    FailAmbush(prey);
                    return;
                }
                lastPreyPosition = prey.Position;
                job.targetB = cell;
                if (cell != pawn.Position)
                    pawn.pather.StartPath(cell, PathEndMode.OnCell);
            };
            yield return stalk;
        }

        private void DetectedAmbush(Pawn prey, Pawn witness)
        {
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || prey?.Spawned != true || prey.Map != pawn.Map)
            {
                FailAmbush(prey);
                return;
            }

            // Publish the reveal before asking other animals to respond:
            // HiddenFrom must now return false for target and witnesses alike.
            state.revealed = true;
            // Keep the attempt active through StartJob(PredatorHunt):
            // otherwise its Prefix would turn this revealed chase back into
            // a new stealth approach.
            state.ambushPhase = 2;
            state.ambushUntil = GenTicks.TicksGame + AnimalHuntStrategy.ChaseLimit(pawn);

            var pursuit = ZoologyPursuitGameComponent.Instance;
            bool targetOutruns = AnimalHuntStrategy.OutrunsPredator(pawn, prey);
            Patch_AnimalFleeFromPredators.AlertDetectedAmbush(
                pawn, prey, witness, targetOutruns);

            if (targetOutruns || AnimalDefense.IsInTree(prey)
                || pursuit?.IsPairBlockedNow(pawn, prey) == true)
            {
                state.ambushPhase = 0;
                state.ambushUntil = 0;
                pursuit?.BlockPursuit(pawn, prey);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            // A detected tiger can still chase a slower gorilla. Only a
            // pursuit that cannot catch its prey is abandoned immediately.
            pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.PredatorHunt, prey));
            EndJobWith(JobCondition.Succeeded);
        }

        private void FailAmbush(Pawn prey)
        {
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state != null)
            {
                state.revealed = true;
                state.ambushPhase = 0;
                state.ambushUntil = 0;
            }
            if (prey?.Spawned == true && prey.Map == pawn.Map)
                ZoologyPursuitGameComponent.Instance?.BlockPursuit(pawn, prey);
            EndJobWith(JobCondition.Incompletable);
        }

        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            base.Notify_DamageTaken(dinfo);
            FailAmbush(TargetA.Pawn);
        }
    }
}
