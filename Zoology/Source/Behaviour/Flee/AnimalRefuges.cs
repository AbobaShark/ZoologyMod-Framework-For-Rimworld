using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class AnimalRefugeDef : Def
    {
        public ThingDef burrow;
        public float searchRadius = 24f;
        public int digTicks = 2500;
        public int unusedDecayInterval = 60000;
        public int occupiedRepairInterval = 2500;
        public float maxBurrowBodySize = 2f;
        public float socialCapacityMultiplier = 4f;
        public TerrainAffordanceDef diggableTerrainAffordance;
        public float hungerExitThreshold = 0.2f;
        public float foodToReuseShelter = 0.35f;
        public int forcedExitReentryDelay = 2500;
    }

    public class AnimalRefugeMapComponent : MapComponent
    {
        public readonly HashSet<Plant> Trees = new HashSet<Plant>();
        public readonly HashSet<Building_AnimalBurrow> Burrows = new HashSet<Building_AnimalBurrow>();
        private int budgetTick, plans;
        private readonly Dictionary<int, HashSet<Thing>> buckets = new Dictionary<int, HashSet<Thing>>();
        private readonly Dictionary<int, HashSet<IntVec3>> waterBuckets = new Dictionary<int, HashSet<IntVec3>>();
        private bool waterIndexed;
        private static int Key(IntVec3 c) => ((c.x / 16) << 16) | (c.z / 16);
        public IEnumerable<Thing> Nearby(IntVec3 center, int radius)
        {
            for (int x = Math.Max(0, center.x - radius) / 16; x <= (center.x + radius) / 16; x++)
                for (int z = Math.Max(0, center.z - radius) / 16; z <= (center.z + radius) / 16; z++)
                    if (buckets.TryGetValue((x << 16) | z, out var bucket))
                        foreach (Thing thing in bucket)
                            if (thing.Spawned && thing.Position.InHorDistOf(center, radius)) yield return thing;
        }
        public AnimalRefugeMapComponent(Map map) : base(map) { }
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Trees.Clear(); Burrows.Clear(); buckets.Clear();
            foreach (Thing thing in map.listerThings.AllThings) Register(thing);
            waterBuckets.Clear(); waterIndexed = true;
            foreach (IntVec3 cell in map.AllCells)
                if (cell.GetTerrain(map).IsWater) UpdateWater(cell, true);
        }
        internal void UpdateWater(IntVec3 cell, bool water)
        {
            if (!waterIndexed) return;
            int key = Key(cell);
            if (water)
            {
                if (!waterBuckets.TryGetValue(key, out var cells)) waterBuckets[key] = cells = new HashSet<IntVec3>();
                cells.Add(cell);
            }
            else if (waterBuckets.TryGetValue(key, out var cells)) cells.Remove(cell);
        }
        internal bool TryWaterCell(Pawn pawn, IntVec3 center, float radius, out IntVec3 result)
        {
            float nearest = radius * radius + .001f; result = IntVec3.Invalid;
            PathGrid grid = pawn.Map.pathing.For(pawn).pathGrid;
            for (int x = Math.Max(0, center.x - (int)Math.Ceiling(radius)) / 16; x <= (center.x + radius) / 16; x++)
                for (int z = Math.Max(0, center.z - (int)Math.Ceiling(radius)) / 16; z <= (center.z + radius) / 16; z++)
                    if (waterBuckets.TryGetValue((x << 16) | z, out var cells))
                        foreach (IntVec3 cell in cells)
                        {
                            float distance = (cell - center).LengthHorizontalSquared;
                            if (distance < nearest && grid.WalkableFast(cell))
                            { nearest = distance; result = cell; }
                        }
            return result.IsValid;
        }
        public void Register(Thing thing)
        {
            if (thing is Plant plant && plant.def.plant.IsTree) Trees.Add(plant);
            if (thing is Building_AnimalBurrow burrow) Burrows.Add(burrow);
            if (!(thing is Building_AnimalBurrow) && !(thing is Plant tree && tree.def.plant.IsTree)) return;
            int key = Key(thing.Position);
            if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new HashSet<Thing>();
            bucket.Add(thing);
        }
        public void Remove(Thing thing)
        {
            if (thing is Plant plant) Trees.Remove(plant);
            if (thing is Building_AnimalBurrow burrow) Burrows.Remove(burrow);
            if (buckets.TryGetValue(Key(thing.Position), out var bucket)) bucket.Remove(thing);
        }
        public bool TryPlan()
        {
            if (budgetTick != GenTicks.TicksGame) { budgetTick = GenTicks.TicksGame; plans = 0; }
            return plans++ < ZoologyTickLimiter.Refuges.PlansPerMapPerTick;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class Patch_RefugeSpawnIndex
    {
        static void Postfix(Thing __instance, Map map)
        {
            if (__instance is Building_AnimalBurrow || (__instance is Plant && __instance.def.plant.IsTree))
                map.GetComponent<AnimalRefugeMapComponent>().Register(__instance);
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    internal static class Patch_RefugeDespawnIndex
    {
        static void Prefix(Thing __instance)
        {
            if (__instance is Building_AnimalBurrow || (__instance is Plant && __instance.def.plant.IsTree))
                __instance.Map?.GetComponent<AnimalRefugeMapComponent>().Remove(__instance);
        }
    }

    internal static class AnimalDefense
    {
        public static bool IsInTree(Pawn pawn) => pawn?.jobs?.curDriver is JobDriver_AnimalTreeRefuge tree && tree.AtHeight;
        public static bool CanClimbTree(Pawn pawn, Plant tree) => pawn != null && UsableTree(pawn, tree);
        public static bool BlocksMelee(Pawn attacker, Pawn target) => IsInTree(target) && attacker?.Flying != true
            && !CanClimbTree(attacker, target.CurJob.targetA.Thing as Plant);
        public static bool DirectClimbingThreat(Pawn threat, Pawn prey, Plant tree) => threat?.CurJob?.targetA.Pawn == prey
            && (IsMeleeJob(threat.CurJob) || threat.jobs.curDriver is JobDriver_AnimalStalk) && CanClimbTree(threat, tree);
        public static bool CanUseShelter(Pawn pawn)
        {
            if (!FamilyFoodReady(pawn)) return false;
            if (ChildcareUtility.IsAnimalChild(pawn) && ChildcareUtility.TryGetKnownBiologicalMother(pawn, out Pawn mother)
                && mother != pawn && mother.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState parentState
                && (parentState.burrowReentryAfter > GenTicks.TicksGame
                    || !FamilyFoodReady(mother))) return false;
            return true;
        }
        private static bool FamilyFoodReady(Pawn pawn)
        {
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            var settings = Building_AnimalBurrow.Settings;
            float food = pawn.needs?.food?.CurLevelPercentage ?? 1f;
            bool hungryChild = false;
            if (pawn.gender == Gender.Female && pawn.relations != null)
                foreach (Pawn child in pawn.relations.Children)
                    if (child != null && !child.Dead && ChildcareUtility.IsAnimalChild(child) && child.MapHeld == pawn.MapHeld
                        && child.PositionHeld.InHorDistOf(pawn.PositionHeld, settings.searchRadius))
                    {
                        float childFood = child.needs?.food?.CurLevelPercentage ?? 1f;
                        if (childFood < settings.hungerExitThreshold || (child.TryGetComp<CompAnimalStrategyState>()?.groundEscapeUntilFed == true
                            && childFood < settings.foodToReuseShelter)) { hungryChild = true; break; }
                    }
            if (food >= settings.foodToReuseShelter && !hungryChild && state != null) state.groundEscapeUntilFed = false;
            return food >= settings.hungerExitThreshold && !hungryChild && state?.groundEscapeUntilFed != true;
        }
        public static void StartGroundEscape(Pawn pawn, Pawn threat, bool hunger)
        {
            if (hunger && pawn.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState state) state.groundEscapeUntilFed = true;
            if (!pawn.Spawned || pawn.Dead || pawn.Downed) return;
            pawn.flight?.ForceLand();
            if (threat?.Spawned != true || threat.Map != pawn.Map || ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn)) return;
            Job escape = FleeUtility.FleeJob(pawn, threat, 24);
            if (escape != null) { escape.flying = false; pawn.jobs.StartJob(escape, JobCondition.InterruptForced); }
        }
        internal static bool IsMeleeJob(Job job) => job != null && (job.def == JobDefOf.AttackMelee
            || job.def == JobDefOf.PredatorHunt || job.def.driverClass == typeof(JobDriver_Constrict));
        public static bool IsRanged(Pawn threat) => RangedVerb(threat) != null;
        private static Verb RangedVerb(Pawn threat)
        {
            if (threat == null) return null;
            // CurrentEffectiveVerb selects a weighted random melee attack when no gun is
            // equipped. A capability query must neither roll combat RNG nor rebuild melee scores.
            Verb current = threat.CurJob?.verbToUse;
            if (current?.verbProps.IsMeleeAttack == false) return current;
            Verb weapon = threat.equipment?.PrimaryEq?.PrimaryVerb;
            if (weapon?.verbProps.IsMeleeAttack == false) return weapon;
            if (threat.MannedThing() is Building_Turret turret) return turret.AttackVerb;
            Verb apparel = threat.apparel?.FirstApparelVerb;
            if (apparel?.verbProps.IsMeleeAttack == false && apparel.Available()) return apparel;
            if (threat.VerbTracker != null)
                foreach (Verb natural in threat.VerbTracker.AllVerbs)
                    if (!natural.verbProps.IsMeleeAttack && natural.Available()) return natural;
            return null;
        }
        private static bool RememberedRangedThreat(Pawn pawn, CompAnimalStrategyState state)
        {
            Pawn source = state.lastRangedSource;
            if (source?.Spawned != true || source.Dead || source.Downed || source.Map != pawn.Map) return false;
            Verb ranged = RangedVerb(source);
            if (ranged == null) return false;
            bool aimed = source.CurJob?.targetA.Pawn == pawn;
            if (!aimed && GenTicks.TicksGame - state.lastRangedTick >= 600) return false;
            float range = Math.Max(30f, ranged.verbProps.range + 3f);
            return source.Position.InHorDistOf(pawn.Position, range);
        }
        public static bool UsableTree(Pawn pawn, Plant tree)
        {
            var ext = DefModExtensionCache<ModExtension_TreeClimber>.Get(pawn.def);
            return ext != null && pawn.BodySize <= ext.maxBodySize && tree?.Spawned == true && tree.Map == pawn.Map
                && tree.def.plant.IsTree && tree.Growth >= ext.minTreeGrowth && !tree.IsBurning() && !tree.IsForbidden(pawn);
        }
        public static void InvalidatePrey(Pawn prey, Map map)
        {
            PredationDecisionCache.InvalidatePrey(prey);
            if (map == null) return;
            // A shelter transition is rare; promptly cancel every active hunter/pack member targeting this pawn.
            foreach (Pawn hunter in map.mapPawns.AllPawnsSpawned.ToList())
                if (hunter != prey && hunter.CurJob?.targetA.Thing == prey
                    && (!prey.Spawned || BlocksMelee(hunter, prey))
                    && (AnimalHuntStrategy.IsHunt(hunter, prey) || IsMeleeJob(hunter.CurJob)))
                    hunter.jobs.EndCurrentJob(JobCondition.Incompletable);
        }
        public static bool SafePath(Pawn pawn, IntVec3 target, List<Pawn> threats, out float length, bool? medium = null)
        {
            length = float.MaxValue;
            // Most nearby refuges have a clear straight corridor. Avoid a second A* search before
            // the job's own path request. Its actual next cells are checked again by RefugeGoto.
            if (StraightSafePath(pawn, target, threats, medium, out length, out bool obstructed)) return true;
            // On clear ground the engine's unconstrained A* chooses the same unsafe corridor.
            // Try a different refuge instead of paying for a route we will immediately reject.
            if (!obstructed) return false;
            using (PawnPath path = AnimalGameApi.FindPath(pawn, target))
            {
                if (!path.Found) return false;
                var nodes = path.NodesReversed;
                bool enteredMedium = medium.HasValue && pawn.Position.GetTerrain(pawn.Map).IsWater == medium.Value;
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    IntVec3 c = nodes[i];
                    if (c.IsForbidden(pawn)) return false;
                    if (medium.HasValue)
                    {
                        bool matches = c.GetTerrain(pawn.Map).IsWater == medium.Value;
                        if (enteredMedium && !matches) return false;
                        enteredMedium |= matches;
                    }
                    foreach (Pawn threat in threats)
                    {
                        float startDistance = (pawn.Position - threat.Position).LengthHorizontal;
                        float distance = (c - threat.Position).LengthHorizontal;
                        // Permit a diagonal/sideways step around an obstacle, but never approach contact.
                        if (distance < Math.Min(startDistance, 3f)
                            || (nodes.Count - 1 - i <= 3 && distance < startDistance - 1.1f)) return false;
                    }
                }
                length = nodes.Count;
                return true;
            }
        }
        private static bool StraightSafePath(Pawn pawn, IntVec3 target, List<Pawn> threats, bool? medium, out float length, out bool obstructed)
        {
            IntVec3 origin = pawn.Position, previous = origin;
            int dx = Math.Abs(target.x - origin.x), dz = Math.Abs(target.z - origin.z);
            int sx = Math.Sign(target.x - origin.x), sz = Math.Sign(target.z - origin.z), error = dx - dz, steps = 0;
            bool entered = medium.HasValue && origin.GetTerrain(pawn.Map).IsWater == medium.Value;
            IntVec3 cell = origin;
            length = float.MaxValue;
            obstructed = false;
            while (true)
            {
                if (!cell.InBounds(pawn.Map) || !cell.Walkable(pawn.Map) || cell.IsForbidden(pawn)) { obstructed = true; return false; }
                if (cell.x != previous.x && cell.z != previous.z
                    && (!(new IntVec3(cell.x, 0, previous.z)).Walkable(pawn.Map)
                        || !(new IntVec3(previous.x, 0, cell.z)).Walkable(pawn.Map))) { obstructed = true; return false; }
                if (medium.HasValue)
                {
                    bool matches = cell.GetTerrain(pawn.Map).IsWater == medium.Value;
                    if (entered && !matches) return false;
                    entered |= matches;
                }
                if (!SafeNode(origin, cell, threats, steps)) return false;
                if (cell == target) { length = steps + 1; return true; }
                previous = cell; int twice = 2 * error;
                if (twice > -dz) { error -= dz; cell.x += sx; }
                if (twice < dx) { error += dx; cell.z += sz; }
                steps++;
            }
        }
        private static bool SafeNode(IntVec3 origin, IntVec3 cell, List<Pawn> threats, int steps)
        {
            foreach (Pawn threat in threats)
            {
                if (threat?.Spawned != true || threat.Dead) continue;
                float start = origin.DistanceTo(threat.Position), distance = cell.DistanceTo(threat.Position);
                if (distance < Math.Min(start, 3f) || (steps <= 3 && distance < start - 1.1f)) return false;
            }
            return true;
        }
        internal static Toil RefugeGoto(Pawn pawn)
        {
            Toil go = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            List<Pawn> threats = null; IntVec3 origin = pawn.Position;
            go.AddPreTickAction(() =>
            {
                if (pawn.CurJob?.targetB.Pawn == null || pawn.pather?.Moving != true) return;
                if (threats == null) threats = Patch_AnimalFleeFromPredators.RefugeThreats(pawn, pawn.CurJob.targetB.Pawn);
                if (!SafeNode(origin, pawn.pather.nextCell, threats, (int)origin.DistanceTo(pawn.Position)))
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            });
            return go;
        }
        public static Job Choose(Pawn pawn, Job flee)
        {
            Job choice = ChooseCore(pawn, flee);
            if (flee != null && choice != flee && flee != pawn?.CurJob) JobMaker.ReturnToPool(flee);
            return choice;
        }
        private static Job ChooseCore(Pawn pawn, Job flee)
        {
            if (flee?.def != JobDefOf.Flee || pawn?.Spawned != true || pawn.Downed
                || ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn)) return flee;
            Pawn threat = flee.targetB.Pawn;
            if (threat == null || !threat.Spawned || threat.Map != pawn.Map) return flee;
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null) return flee;
            bool continuingMedium = state.environmentThreat == threat && state.environmentUntil > GenTicks.TicksGame;
            if (state.nextDefensePlan > GenTicks.TicksGame)
            {
                if (!continuingMedium) return flee;
                if (state.environmentTarget.InBounds(pawn.Map) && !pawn.Position.InHorDistOf(state.environmentTarget, 1f)
                    && state.environmentTarget.GetTerrain(pawn.Map).IsWater == state.chosenWater)
                    return EnvironmentJob(state.environmentTarget, threat);
                // Reaching the cached destination needs a new safe destination, even within the TTL.
            }
            if (ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)) return flee;
            var index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (!index.TryPlan()) return flee;
            state.nextDefensePlan = GenTicks.TicksGame + ZoologyTickLimiter.Refuges.ReplanTicks;
            var threats = Patch_AnimalFleeFromPredators.RefugeThreats(pawn, threat);
            var safeDestinationRanges = new float[threats.Count];
            for (int i = 0; i < threats.Count; i++)
            {
                float distance = pawn.Position.DistanceTo(threats[i].Position);
                float minimum = Math.Max(Math.Min(distance, 3f), distance - 1.1f);
                safeDestinationRanges[i] = minimum * minimum;
            }
            bool ranged = threats.Any(IsRanged);
            bool treeReachableByThreat = threats.Any(t => t.Flying || t.flight?.CanFlyNow == true);
            var candidates = new List<Thing>();
            bool canShelter = CanUseShelter(pawn);
            if (canShelter && DefModExtensionCache<ModExtension_Burrower>.Has(pawn))
            {
                candidates.AddRange(state.familiarBurrows.Where(b => b != null && b.Spawned && b.Map == pawn.Map && b.Accepts(pawn)));
                foreach (var b in index.Nearby(pawn.Position, 12).OfType<Building_AnimalBurrow>())
                    if (b.Spawned && b.Accepts(pawn) && b.Position.InHorDistOf(pawn.Position, 12f) && !candidates.Contains(b))
                    { candidates.Add(b); state.Remember(b); }
            }
            var climber = DefModExtensionCache<ModExtension_TreeClimber>.Get(pawn.def);
            if (canShelter && climber != null && pawn.BodySize <= climber.maxBodySize
                && !ranged && !treeReachableByThreat && !RememberedRangedThreat(pawn, state))
                candidates.AddRange(index.Nearby(pawn.Position, 24).OfType<Plant>().Where(t => t.Growth >= climber.minTreeGrowth
                    && !threats.Any(source => DirectClimbingThreat(source, pawn, t))));
            Thing best = null;
            float bestLength = float.MaxValue;
            foreach (Thing candidate in candidates.Where(t => t.Position.InHorDistOf(pawn.Position, 24f)
                && SafeDestination(t.Position, threats, safeDestinationRanges))
                .OrderBy(t => (t.Position - pawn.Position).LengthHorizontalSquared)
                .Where(t => !(t is Plant tree) || UsableTree(pawn, tree)).Take(ZoologyTickLimiter.Refuges.MaxPathCandidates))
            {
                IntVec3 delta = candidate.Position - pawn.Position;
                // An eight-way path cannot use fewer than Chebyshev distance + 1 nodes.
                // Skip only candidates mathematically unable to improve the best safe path.
                if (Math.Max(Math.Abs(delta.x), Math.Abs(delta.z)) + 1 >= bestLength) continue;
                if (pawn.CanReserve(candidate, candidate is Building_AnimalBurrow ? int.MaxValue : 1, candidate is Building_AnimalBurrow ? 0 : -1)
                    && SafePath(pawn, candidate.Position, threats, out float length) && length < bestLength)
                { best = candidate; bestLength = length; }
            }
            if (best != null)
            {
                state.shelter = best;
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(best is Building_AnimalBurrow
                    ? "Zoology_EnterBurrow" : "Zoology_TreeRefuge"), best, threat);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                return job;
            }
            if (pawn.flight?.CanFlyNow == true && !ranged && state.groundEscapeUntilFed == false) return flee;
            if (AnimalLocomotion.TrySample(pawn, true, pawn.Position, 12f, out IntVec3 water)
                && AnimalLocomotion.TrySample(pawn, false, pawn.Position, 12f, out IntVec3 land))
            {
                float waterRatio = AnimalLocomotion.SpeedAt(pawn, water) / Math.Max(0.001f, AnimalLocomotion.SpeedAt(threat, water));
                float landRatio = AnimalLocomotion.SpeedAt(pawn, land) / Math.Max(0.001f, AnimalLocomotion.SpeedAt(threat, land));
                bool chosen = waterRatio > landRatio;
                if (state.environmentThreat == threat && state.environmentUntil > GenTicks.TicksGame) chosen = state.chosenWater;
                if (continuingMedium || !Mathf.Approximately(waterRatio, landRatio))
                {
                    int pathChecks = 0;
                    foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, 18f, false).Reverse())
                    {
                        if (!c.InBounds(pawn.Map) || !c.Standable(pawn.Map) || c.GetTerrain(pawn.Map).IsWater != chosen
                            || c.IsForbidden(pawn) || (c - threat.Position).LengthHorizontalSquared <= (pawn.Position - threat.Position).LengthHorizontalSquared) continue;
                        if (++pathChecks > ZoologyTickLimiter.Refuges.MaxPathCandidates) break;
                        if (!SafePath(pawn, c, threats, out _, chosen)) continue;
                        state.environmentThreat = threat; state.environmentUntil = GenTicks.TicksGame + 600; state.chosenWater = chosen;
                        state.environmentTarget = c;
                        return EnvironmentJob(c, threat);
                    }
                }
            }
            state.environmentUntil = 0;
            return flee;
        }
        private static bool SafeDestination(IntVec3 destination, List<Pawn> threats, float[] minimumSquared)
        {
            for (int i = 0; i < threats.Count; i++)
                if ((destination - threats[i].Position).LengthHorizontalSquared < minimumSquared[i]) return false;
            return true;
        }
        private static Job EnvironmentJob(IntVec3 destination, Pawn threat)
        {
            Job escape = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_EnvironmentFlee"), destination, threat);
            escape.locomotionUrgency = LocomotionUrgency.Sprint;
            return escape;
        }
    }

    public class JobGiver_AnimalBurrow : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            bool burrower = DefModExtensionCache<ModExtension_Burrower>.Has(pawn);
            bool climber = DefModExtensionCache<ModExtension_TreeClimber>.Has(pawn);
            if ((!burrower && !climber) || !pawn.Spawned || pawn.Downed) return null;
            bool wantsSleep = pawn.needs?.rest?.CurLevelPercentage < 0.3f;
            if (!wantsSleep && !burrower) return null;
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || (wantsSleep ? state.nextRestPlan : state.nextDigPlan) > GenTicks.TicksGame) return null;
            if (!AnimalDefense.CanUseShelter(pawn)
                || pawn.InMentalState
                || pawn.needs?.food?.CurLevelPercentage < 0.4f || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                || pawn.mindState?.meleeThreat != null || pawn.mindState?.enemyTarget != null) return null;
            var index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (!index.TryPlan()) return null;
            // Rest/dig bookkeeping must never postpone an urgent defensive climb.
            if (wantsSleep) state.nextRestPlan = GenTicks.TicksGame + 180;
            else state.nextDigPlan = GenTicks.TicksGame + 2500;
            var threats = Patch_AnimalFleeFromPredators.RefugeThreats(pawn, null);
            if (threats.Count != 0) return null;
            if (wantsSleep)
            {
                var beds = index.Nearby(pawn.Position, 16).Where(t => t is Building_AnimalBurrow b ? burrower && b.Accepts(pawn)
                    : climber && t is Plant tree && AnimalDefense.UsableTree(pawn, tree));
                foreach (Thing bed in beds.OrderBy(t => (t.Position - pawn.Position).LengthHorizontalSquared).Take(3))
                    if (!bed.IsForbidden(pawn) && pawn.CanReserveAndReach(bed, PathEndMode.OnCell, Danger.None,
                        bed is Building_AnimalBurrow ? int.MaxValue : 1, bed is Building_AnimalBurrow ? 0 : -1))
                    {
                        if (bed is Building_AnimalBurrow known) state.Remember(known);
                        return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(bed is Building_AnimalBurrow
                            ? "Zoology_EnterBurrow" : "Zoology_SleepInTree"), bed);
                    }
            }
            if (!burrower || pawn.BodySize > Math.Min(Building_AnimalBurrow.Settings.maxBurrowBodySize,
                DefModExtensionCache<ModExtension_Burrower>.Get(pawn.def).maxBodySize)) return null;
            // Awake animals with an existing nearby burrow need no sleep-path search.
            foreach (var burrow in index.Nearby(pawn.Position, 16).OfType<Building_AnimalBurrow>())
                if (burrow.Spawned && burrow.Accepts(pawn) && !burrow.IsForbidden(pawn))
                {
                    state.Remember(burrow);
                    return null;
                }
            if (state.familiarBurrows.Exists(b => b != null && b.Spawned && b.Map == pawn.Map)) return null;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, 8f, true).InRandomOrder())
                if (Diggable(pawn, c) && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.None))
                    return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_DigBurrow"), c);
            return null;
        }
        internal static bool Diggable(Pawn pawn, IntVec3 c) => c.InBounds(pawn.Map) && c.Standable(pawn.Map)
            && !c.IsForbidden(pawn) && c.GetTerrain(pawn.Map).affordances.Contains(Building_AnimalBurrow.Settings.diggableTerrainAffordance)
            && c.GetEdifice(pawn.Map) == null && c.GetFirstPawn(pawn.Map) == null;
    }
    public class JobDriver_DigAnimalBurrow : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.mindState.meleeThreat != null || pawn.mindState.enemyTarget != null || pawn.Downed);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(Building_AnimalBurrow.Settings.digTicks);
            yield return Toils_General.Do(() =>
            {
                if (TargetA.Cell.GetEdifice(Map) != null || !TargetA.Cell.GetTerrain(Map).affordances.Contains(Building_AnimalBurrow.Settings.diggableTerrainAffordance)) return;
                var burrow = (Building_AnimalBurrow)ThingMaker.MakeThing(Building_AnimalBurrow.Settings.burrow);
                burrow.InitializeFor(pawn);
                GenSpawn.Spawn(burrow, TargetA.Cell, Map);
                pawn.TryGetComp<CompAnimalStrategyState>().Remember(burrow);
            });
        }
    }
    public class JobDriver_EnterAnimalBurrow : JobDriver
    {
        // A refuge has shared approach slots, not consumable stack units. Contents enforce BodySize capacity.
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, int.MaxValue, 0, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !(TargetA.Thing is Building_AnimalBurrow burrow) || !burrow.Accepts(pawn));
            yield return AnimalDefense.RefugeGoto(pawn);
            yield return Toils_General.Do(() =>
            {
                var burrow = (Building_AnimalBurrow)TargetA.Thing;
                burrow.SetThreat(TargetB.Pawn);
                burrow.TryAcceptThing(pawn);
            });
        }
    }
    public class JobDriver_AnimalTreeRefuge : JobDriver
    {
        public bool AtHeight;
        // Native draw offset changes only the displayed body, never Position, pathing or reservations.
        public override Vector3 ForcedBodyOffset => AtHeight
            ? new Vector3(0f, 0f, Mathf.Clamp((TargetA.Thing?.def.graphicData?.drawSize.y ?? 2f) * 0.45f, 0.8f, 1.8f))
            : Vector3.zero;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref AtHeight, "atHeight"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !AnimalDefense.UsableTree(pawn, TargetA.Thing as Plant) || pawn.Downed);
            AddFinishAction(_ => { AtHeight = false; asleep = false; PredationDecisionCache.InvalidatePrey(pawn); });
            yield return AnimalDefense.RefugeGoto(pawn);
            Toil stay = ToilMaker.MakeToil();
            bool sleep = job.def.defName == "Zoology_SleepInTree";
            stay.initAction = () =>
            {
                AtHeight = true; asleep = sleep;
                if (sleep) pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
                pawn.pather.StopDead(); AnimalDefense.InvalidatePrey(pawn, Map);
            };
            stay.tickAction = () =>
            {
                if (sleep) pawn.needs?.rest?.TickResting(1f);
                if (!pawn.IsHashIntervalTick(60)) return;
                bool hungry = pawn.needs?.food?.CurLevelPercentage < Building_AnimalBurrow.Settings.hungerExitThreshold;
                // Ground melee actors cannot disturb a protected sleeper. Only query their full
                // suitability when hunger actually requires choosing an escape on the ground.
                var threats = Patch_AnimalFleeFromPredators.RefugeThreats(pawn, TargetB.Pawn, hungry ? null : TargetA.Thing as Plant);
                Pawn reachableThreat = threats.FirstOrDefault(t => AnimalDefense.IsRanged(t)
                    || t.Flying || t.flight?.CanFlyNow == true
                    || AnimalDefense.DirectClimbingThreat(t, pawn, TargetA.Thing as Plant));
                if (hungry || reachableThreat != null)
                {
                    Pawn source = reachableThreat ?? threats.FirstOrDefault();
                    EndJobWith(JobCondition.InterruptForced);
                    AnimalDefense.StartGroundEscape(pawn, source, hungry);
                    return;
                }
                if (sleep ? pawn.needs?.rest?.CurLevelPercentage >= 0.95f
                    : TargetB.Pawn?.Spawned != true || TargetB.Pawn.Map != Map || TargetB.Pawn.Dead || TargetB.Pawn.Downed
                        || !TargetB.Pawn.Position.InHorDistOf(pawn.Position, 24f)) EndJobWith(JobCondition.Succeeded);
            };
            stay.defaultCompleteMode = ToilCompleteMode.Never;
            yield return stay;
        }
        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            if (dinfo.Def.isRanged || (dinfo.Instigator is Pawn source && AnimalDefense.IsRanged(source)))
            {
                var state = pawn.TryGetComp<CompAnimalStrategyState>();
                state.lastRangedSource = dinfo.Instigator as Pawn; state.lastRangedTick = GenTicks.TicksGame;
                AtHeight = false;
                EndJobWith(JobCondition.InterruptForced);
                AnimalDefense.StartGroundEscape(pawn, dinfo.Instigator as Pawn, false);
            }
        }
    }
    public class JobDriver_AnimalEnvironmentFlee : JobDriver
    {
        private bool entered;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref entered, "enteredMedium"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => TargetB.Pawn?.Spawned != true || TargetB.Pawn.Map != Map || pawn.Downed);
            Toil go = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            go.AddPreTickAction(() =>
            {
                var state = pawn.TryGetComp<CompAnimalStrategyState>();
                bool matches = pawn.Position.GetTerrain(Map).IsWater == state.chosenWater;
                entered |= matches;
                if (entered && pawn.pather.Moving && pawn.pather.nextCell.GetTerrain(Map).IsWater != state.chosenWater)
                {
                    pawn.pather.StopDead(); state.environmentUntil = 0; state.nextDefensePlan = GenTicks.TicksGame + 250;
                    EndJobWith(JobCondition.Incompletable);
                }
            });
            yield return go;
        }
    }

    // Melee-only gate. Pawns in trees remain spawned and visible to bullets, explosions and selection.
    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    internal static class Patch_TreeMeleeAccess
    {
        static bool Prefix(Verb __instance, LocalTargetInfo targ, ref bool __result)
        {
            if (!__instance.verbProps.IsMeleeAttack || !AnimalDefense.BlocksMelee(__instance.CasterPawn, targ.Pawn)) return true;
            __result = false; return false;
        }
    }
}
