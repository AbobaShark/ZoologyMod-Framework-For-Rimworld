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
        public bool suppressReaction, revealed;
        public Pawn lastRangedSource;
        public int lastRangedTick = -60000;
        public List<Building_AnimalBurrow> familiarBurrows = new List<Building_AnimalBurrow>();
        public Pawn environmentThreat;
        public bool chosenWater;
        public IntVec3 environmentTarget = IntVec3.Invalid;
        public int environmentUntil, nextDefensePlan, nextRestPlan, nextDigPlan;
        public Thing shelter;
        public bool groundEscapeUntilFed;
        public int burrowReentryAfter, nextBurrowHuntPlan;
        public int fleeingSince = -1;
        public Pawn fleeThreat;
        public Pawn constrictedPrey;
        public override void PostExposeData()
        {
            Scribe_References.Look(ref ambushPrey, "ambushPrey");
            Scribe_Values.Look(ref ambushPhase, "ambushPhase");
            Scribe_Values.Look(ref ambushUntil, "ambushUntil");
            Scribe_Values.Look(ref ambushApproachDistance, "ambushApproachDistance", 1f);
            Scribe_Values.Look(ref sprintStart, "sprintStart");
            Scribe_Values.Look(ref suppressReaction, "suppressReaction");
            Scribe_Values.Look(ref revealed, "ambushRevealed");
            Scribe_References.Look(ref lastRangedSource, "lastRangedSource");
            Scribe_Values.Look(ref lastRangedTick, "lastRangedTick", -60000);
            Scribe_Collections.Look(ref familiarBurrows, "familiarBurrows", LookMode.Reference);
            Scribe_References.Look(ref environmentThreat, "environmentThreat");
            Scribe_Values.Look(ref chosenWater, "chosenWater");
            Scribe_Values.Look(ref environmentTarget, "environmentTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref environmentUntil, "environmentUntil");
            Scribe_References.Look(ref shelter, "defenseShelter");
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
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(prey.Position, distance + 2f, false))
            {
                float rangeSq = (cell - prey.Position).LengthHorizontalSquared;
                if (rangeSq < distance * distance || rangeSq > (distance + 1.5f) * (distance + 1.5f)
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

        // Per-interval hazard, not a per-check fixed chance: longer intervals
        // preserve the same chance of detection over the same elapsed time.
        public static bool DetectedWhileStalking(Pawn predator, Pawn prey, int elapsedTicks)
        {
            if (prey?.Awake() != true || prey.Downed) return false;
            float distance = (predator.Position - prey.Position).LengthHorizontal;
            float size = Mathf.Sqrt(Mathf.Max(0.05f, predator.BodySize));
            float proximity = Mathf.Lerp(0.25f, 2.5f, Mathf.Clamp01((10f - distance) / 9f));
            float ectotherm = ZoologyCacheUtility.HasEctothermicExtension(predator.def) ? 0.35f : 1f;
            float moving = predator.pather?.Moving == true ? 1f : 0.3f;
            float visibility = GenSight.LineOfSight(predator.Position, prey.Position, predator.Map) ? 1f : 0.2f;
            float hazard = 0.00015f * size * proximity * ectotherm * moving * visibility;
            return Rand.Chance(1f - Mathf.Exp(-hazard * Mathf.Max(1, elapsedTicks)));
        }

        public static bool HiddenFrom(Pawn predator, Pawn prey)
        {
            PredationStrategy strategy = For(predator);
            if (strategy != PredationStrategy.Ambush && strategy != PredationStrategy.WaterAmbush) return false;
            if (predator == null || prey == null || predator.InMentalState || !predator.Spawned || !prey.Spawned
                || predator.Map != prey.Map) return false;
            if (strategy == PredationStrategy.WaterAmbush) return predator.Position.GetTerrain(predator.Map).IsWater
                && !prey.Position.GetTerrain(prey.Map).IsWater;

            CompAnimalStrategyState state = predator.TryGetComp<CompAnimalStrategyState>();
            if (state?.ambushPrey != prey || state.revealed || state.ambushUntil <= GenTicks.TicksGame)
                return false;
            // Preserve concealment across the stalk-to-hunt job boundary. The
            // successful approach remains undetected until the first hit.
            return (state.ambushPhase == 1 && predator.jobs.curDriver is JobDriver_AnimalStalk)
                || (state.ambushPhase == 2 && state.suppressReaction
                    && (predator.jobs.curDriver is JobDriver_AnimalStalk || IsHunt(predator, prey)));
        }
        public static bool IsFleeing(Job job) => job?.def == JobDefOf.Flee || job?.def.defName == "Zoology_EnvironmentFlee";
        public static bool HopelessPursuit(Pawn predator, Pawn prey)
        {
            if (For(predator) == PredationStrategy.No || prey?.Spawned != true || prey.Downed || !prey.Awake()
                || predator.Position.InHorDistOf(prey.Position, 2f)) return false;
            var escape = prey.TryGetComp<CompAnimalStrategyState>();
            bool recentlyEscaped = escape?.fleeThreat == predator && escape.fleeingSince >= 0
                && GenTicks.TicksGame - escape.fleeingSince < 3000;
            if (!IsFleeing(prey.CurJob) && !recentlyEscaped) return false;
            // Both ends of the pursuit must favor the runner: a single muddy tile is not enough.
            float preySpeed = AnimalLocomotion.SpeedAt(prey, prey.Position, recentlyEscaped);
            return preySpeed > AnimalLocomotion.SpeedAt(predator, prey.Position) * 1.5f
                && preySpeed > AnimalLocomotion.SpeedAt(predator, predator.Position) * 1.5f;
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
            if (__result <= 0f || prey?.Spawned != true || AnimalHuntStrategy.For(predator) != PredationStrategy.Chasing || !prey.Awake()) return;
            IntVec3 cell = prey.Position;
            float predatorSpeed = AnimalLocomotion.SpeedAt(predator, cell);
            float preySpeed = AnimalLocomotion.SpeedAt(prey, cell);
            if (preySpeed > predatorSpeed) __result *= Mathf.Clamp01(predatorSpeed / preySpeed);
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
            state.suppressReaction = false;
            state.revealed = false;
            state.ambushApproachDistance = distance;

            if (___pawn.Position.InHorDistOf(prey.Position, distance))
            {
                state.suppressReaction = true;
                state.ambushUntil = GenTicks.TicksGame + AnimalHuntStrategy.ChaseLimit(___pawn);
                return;
            }
            if (!AnimalHuntStrategy.TryFindApproachCell(___pawn, prey, distance, out IntVec3 cell))
                return;
            state.ambushPhase = 1;
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
        private IntVec3 lastPreyPosition = IntVec3.Invalid;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed || pawn.InMentalState || TargetA.Pawn.Dead);
            Toil stalk = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            stalk.initAction = () =>
            {
                lastPreyPosition = TargetA.Pawn.Position;
                lastCheckTick = GenTicks.TicksGame;
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
                if (pawn.Position.InHorDistOf(prey.Position, Mathf.Max(1f, state.ambushApproachDistance)))
                {
                    state.ambushPhase = 2;
                    state.suppressReaction = true;
                    state.ambushUntil = now + AnimalHuntStrategy.ChaseLimit(pawn);
                    pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.PredatorHunt, prey));
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                if (AnimalHuntStrategy.DetectedWhileStalking(pawn, prey, elapsed))
                {
                    FailAmbush(prey);
                    return;
                }
                if (now < nextRepathTick) return;
                bool preyMoved = (prey.Position - lastPreyPosition).LengthHorizontalSquared >= 4;
                if (!preyMoved && pawn.pather.Moving) return;
                nextRepathTick = now + 300;
                if (!AnimalHuntStrategy.TryFindApproachCell(pawn, prey,
                    Mathf.Max(1f, state.ambushApproachDistance), out IntVec3 cell))
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

        private void FailAmbush(Pawn prey)
        {
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state != null)
            {
                state.revealed = true;
                state.suppressReaction = false;
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
