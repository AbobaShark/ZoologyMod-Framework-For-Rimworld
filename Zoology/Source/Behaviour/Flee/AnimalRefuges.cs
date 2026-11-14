using System;
using System.Collections.Generic;
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
        public TerrainAffordanceDef diggableTerrainAffordance;
        public float hungerExitThreshold = 0.2f;
        public float foodToReuseShelter = 0.35f;
        public int forcedExitReentryDelay = 2500;
    }

    public partial class AnimalRefugeMapComponent : MapComponent
    {
        public readonly HashSet<Plant> Trees = new HashSet<Plant>();
        public readonly HashSet<Building_AnimalBurrow> Burrows = new HashSet<Building_AnimalBurrow>();
        public readonly HashSet<Building_AnimalNursery> Nurseries = new HashSet<Building_AnimalNursery>();
        public readonly HashSet<Building_Grave> Graves = new HashSet<Building_Grave>();
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
            RebuildIndexes();
        }

        // A one-time repair after re-enabling the master runtime switch.
        // While Harmony was off, tree spawns and terrain changes were not
        // indexed. Never call this from a game tick or a normal feature toggle.
        internal void RebuildIndexes()
        {
            Trees.Clear(); Burrows.Clear(); Nurseries.Clear(); Graves.Clear(); buckets.Clear(); ClearTreeClaims();
            foreach (Thing thing in map.listerThings.AllThings) Register(thing);
            waterBuckets.Clear(); waterIndexed = true;
            herdWaterExitAnchors.Clear(); waterSearchRetry.Clear(); herdEscapeRetry.Clear();
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
            if (thing is Plant plant && plant.def.plant.IsTree && !plant.def.plant.isStump) Trees.Add(plant);
            if (thing is Building_AnimalBurrow burrow) Burrows.Add(burrow);
            if (thing is Building_AnimalNursery nursery) Nurseries.Add(nursery);
            if (thing is Building_Grave grave && grave is not Building_Sarcophagus
                && grave.GetType() == typeof(Building_Grave)) Graves.Add(grave);
            if (!(thing is Building_AnimalBurrow) && !(thing is Building_AnimalNursery)
                && !(thing is Building_Grave graveThing
                    && graveThing is not Building_Sarcophagus
                    && graveThing.GetType() == typeof(Building_Grave))
                && !(thing is Plant tree && tree.def.plant.IsTree && !tree.def.plant.isStump)) return;
            int key = Key(thing.Position);
            if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new HashSet<Thing>();
            bucket.Add(thing);
        }
        public void Remove(Thing thing)
        {
            if (thing is Plant plant) { Trees.Remove(plant); ForgetTreeClaims(plant); }
            if (thing is Building_AnimalBurrow burrow) Burrows.Remove(burrow);
            if (thing is Building_AnimalNursery nursery) Nurseries.Remove(nursery);
            if (thing is Building_Grave grave) Graves.Remove(grave);
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
            if (__instance is Building_AnimalBurrow || __instance is Building_AnimalNursery
                || (__instance is Building_Grave grave && grave is not Building_Sarcophagus
                    && grave.GetType() == typeof(Building_Grave))
                || (__instance is Plant && __instance.def.plant.IsTree && !__instance.def.plant.isStump))
                map.GetComponent<AnimalRefugeMapComponent>().Register(__instance);
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    internal static class Patch_RefugeDespawnIndex
    {
        static void Prefix(Thing __instance)
        {
            if (__instance is Building_AnimalBurrow || __instance is Building_AnimalNursery
                || (__instance is Building_Grave grave && grave is not Building_Sarcophagus
                    && grave.GetType() == typeof(Building_Grave))
                || (__instance is Plant && __instance.def.plant.IsTree && !__instance.def.plant.isStump))
            {
                AnimalRefugeMapComponent index = __instance.Map?.GetComponent<AnimalRefugeMapComponent>();
                if (__instance is Plant tree && index != null)
                {
                    // Destroying a nest deregisters it from Nurseries.
                    // Find one at a time: never mutate a HashSet being enumerated.
                    while (true)
                    {
                        Building_AnimalNursery attached = null;
                        foreach (Building_AnimalNursery nest in index.Nurseries)
                            if (nest?.Spawned == true && nest.AnchorTree == tree)
                            {
                                attached = nest;
                                break;
                            }
                        if (attached == null) break;
                        attached.Destroy();
                    }
                }
                index?.Remove(__instance);
            }
        }
    }

    internal static class AnimalDefense
    {
        public static bool IsInTree(Pawn pawn) => pawn?.jobs?.curDriver is JobDriver_AnimalTreeRefuge tree && tree.AtHeight;
        public static bool CanClimbTree(Pawn pawn, Plant tree) => pawn != null && UsableTree(pawn, tree);
        public static bool BlocksMelee(Pawn attacker, Pawn target) => IsInTree(target) && ModConstants.AdvancedThreatAvoidance && attacker?.Flying != true
            && !CanClimbTree(attacker, target.CurJob.targetA.Thing as Plant);
        public static bool DirectClimbingThreat(Pawn threat, Pawn prey, Plant tree) => threat?.CurJob?.targetA.Pawn == prey
            && (IsMeleeJob(threat.CurJob) || threat.jobs.curDriver is JobDriver_AnimalStalk) && CanClimbTree(threat, tree);

        // Shelters are needed for a predator that can actually intercept this
        // animal, or for a visible predator already attacking this individual.
        // Merely being an edible prey for a much slower passive predator is
        // not enough. This applies at admission AND at shelter reevaluation.
        internal static bool NeedsCoverAgainst(Pawn prey, Pawn threat)
        {
            // Burrow occupants are held in a ThingOwner and have Map == null.
            // Always use MapHeld here rather than the spawned-only flee
            // validator; otherwise all contained animals appear threat-free.
            if (prey == null || threat == null || threat == prey
                || threat.Spawned != true || threat.Dead || threat.Destroyed
                || threat.Downed || !threat.Awake()
                || prey.MapHeld != threat.Map
                || AnimalHuntStrategy.HiddenFrom(threat, prey))
                return false;

            // Ranged attacks and non-animal hazards are not outrun simply by
            // having a higher MoveSpeed stat.
            if (IsRanged(threat) || threat.RaceProps?.Animal != true)
                return true;

            // Once a predator is openly pursuing this individual, remaining
            // in sight is risky even if the target usually runs faster.
            if (Patch_AnimalFleeFromPredators.IsThreatTargetingPawn(threat, prey)
                || threat.Position.InHorDistOf(prey.PositionHeld, 2f))
                return true;

            if (threat.RaceProps?.predator != true)
                return true;

            // Spawned prey use the existing real terrain/water/flight movement
            // model. Contained burrow occupants use their current MoveSpeed
            // stat for comparison because pawn.Map is null while held.
            float escapeSpeed = prey.Spawned
                ? AnimalLocomotion.SpeedAt(prey, prey.Position)
                : prey.GetStatValue(StatDefOf.MoveSpeed, true);
            if (escapeSpeed <= 0f)
                return true;

            float predatorHere = AnimalLocomotion.SpeedAt(threat, threat.Position);
            float predatorAtPrey = AnimalLocomotion.SpeedAt(threat, prey.PositionHeld);
            // A held burrow occupant may occupy an impassable building cell.
            // Its cell is then not a movement sample for the predator; retain
            // the predator's current effective speed rather than treating
            // that cell's zero movement as proof it cannot reach the burrow.
            if (!prey.Spawned && predatorAtPrey <= 0f)
                predatorAtPrey = predatorHere;
            return predatorHere >= escapeSpeed && predatorAtPrey >= escapeSpeed;
        }

        internal static void RemoveUncatchablePassiveThreats(Pawn prey, List<Pawn> threats)
        {
            if (threats == null) return;
            for (int i = threats.Count - 1; i >= 0; i--)
                if (!NeedsCoverAgainst(prey, threats[i]))
                    threats.RemoveAt(i);
        }

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
            if (pawn == null) return false;
            var ext = DefModExtensionCache<ModExtension_TreeClimber>.Get(pawn.def);
            return ext != null && ModConstants.AdvancedThreatAvoidance
                && pawn.BodySize <= ext.maxBodySize && tree?.Spawned == true && tree.Map == pawn.Map
                && tree.def.plant.IsTree && !tree.def.plant.isStump && tree.Growth >= ext.minTreeGrowth && !tree.IsBurning() && !tree.IsForbidden(pawn);
        }
        // Only inspect the tree's own cell, and only during refuge selection or ascent.
        // Coexisting non-predatory tree users are not disturbed.
        internal static Pawn PredatorInTree(Pawn prey, Plant tree)
        {
            if (prey == null || tree?.Spawned != true || tree.Map != prey.Map) return null;
            List<Thing> things = tree.Position.GetThingList(tree.Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn predator && predator != prey && IsInTree(predator)
                    && predator.CurJob?.targetA.Thing == tree
                    && Patch_AnimalFleeFromPredators.IsDangerousTreeClimber(predator, prey, tree))
                    return predator;
            }
            return null;
        }

        internal static void EvictTreePrey(Pawn predator, Plant tree)
        {
            if (predator?.RaceProps?.predator != true || tree?.Spawned != true || predator.Map != tree.Map)
                return;

            List<Thing> things = tree.Position.GetThingList(tree.Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] is not Pawn prey || prey == predator || !IsInTree(prey)
                    || prey.CurJob?.targetA.Thing != tree
                    || !Patch_AnimalFleeFromPredators.IsDangerousTreeClimber(predator, prey, tree))
                    continue;

                prey.jobs.EndCurrentJob(JobCondition.InterruptForced);
                StartGroundEscape(prey, predator, false);
            }
        }

        public static void InvalidatePrey(Pawn prey, Map map)
        {
            PredationDecisionCache.InvalidatePrey(prey);
            if (map == null) return;
            // A shelter transition is rare; promptly cancel every active hunter/pack member targeting this pawn.
            IReadOnlyList<Pawn> hunters = map.mapPawns.AllPawnsSpawned;
            for (int i = hunters.Count - 1; i >= 0; i--)
            {
                if (i >= hunters.Count) continue;
                Pawn hunter = hunters[i];
                if (hunter != prey && hunter.CurJob?.targetA.Thing == prey
                    && (!prey.Spawned || BlocksMelee(hunter, prey))
                    && (AnimalHuntStrategy.IsHunt(hunter, prey) || IsMeleeJob(hunter.CurJob)))
                    hunter.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
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
            if (!ModConstants.AdvancedThreatAvoidance) return flee;
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
            RemoveUncatchablePassiveThreats(pawn, threats);
            // The ordinary flee job remains available, but trees, burrows
            // and water refuge must not be chosen for an irrelevant threat.
            if (threats.Count == 0) return flee;
            var safeDestinationRanges = new float[threats.Count];
            for (int i = 0; i < threats.Count; i++)
            {
                float distance = pawn.Position.DistanceTo(threats[i].Position);
                float minimum = Math.Max(Math.Min(distance, 3f), distance - 1.1f);
                safeDestinationRanges[i] = minimum * minimum;
            }
            bool ranged = false, treeThreat = false;
            for (int i = 0; i < threats.Count; i++)
            {
                Pawn source = threats[i];
                if (IsRanged(source)) ranged = true;
                if (source.Flying || source.flight?.CanFlyNow == true)
                    treeThreat = true;
            }
            bool canShelter = CanUseShelter(pawn);
            Building_AnimalBurrow familyDen = state.familyBurrow;
            bool burrower = DefModExtensionCache<ModExtension_Burrower>.Has(pawn)
                || AnimalOffspringShelter.For(pawn.def) == OffspringShelterMode.DEN;
            ModExtension_TreeClimber climber =
                DefModExtensionCache<ModExtension_TreeClimber>.Get(pawn.def);
            bool canClimb = canShelter && climber != null
                && pawn.BodySize <= climber.maxBodySize && !ranged && !treeThreat
                && !RememberedRangedThreat(pawn, state);

            // Keep three nearest candidates; no sorting/LINQ or temporary lists.
            Thing nearest1 = null, nearest2 = null, nearest3 = null;
            float d1 = float.MaxValue, d2 = float.MaxValue, d3 = float.MaxValue;
            void Consider(Thing candidate)
            {
                if (candidate == null || !candidate.Spawned || candidate.Map != pawn.Map
                    || candidate == nearest1 || candidate == nearest2
                    || candidate == nearest3
                    || !candidate.Position.InHorDistOf(pawn.Position, 24f)
                    || !SafeDestination(candidate.Position, threats, safeDestinationRanges))
                    return;
                if (candidate is Building_AnimalBurrow hole)
                {
                    if (!canShelter || (!burrower && candidate != familyDen)
                        || (candidate != familyDen
                            && !candidate.Position.InHorDistOf(pawn.Position, 12f))
                        || !hole.Accepts(pawn))
                        return;
                }
                else if (candidate is Plant tree)
                {
                    if (!canClimb || tree.Growth < climber.minTreeGrowth
                        || !UsableTree(pawn, tree)
                        || PredatorInTree(pawn, tree) != null
                        || !index.TreeHasRoom(tree, pawn))
                        return;
                    for (int j = 0; j < threats.Count; j++)
                        if (CanClimbTree(threats[j], tree)) return;
                }
                else return;
                float d = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                if (d < d1)
                {
                    nearest3 = nearest2; d3 = d2;
                    nearest2 = nearest1; d2 = d1;
                    nearest1 = candidate; d1 = d;
                }
                else if (d < d2)
                {
                    nearest3 = nearest2; d3 = d2;
                    nearest2 = candidate; d2 = d;
                }
                else if (d < d3)
                {
                    nearest3 = candidate; d3 = d;
                }
            }
            if (canShelter)
            {
                if (familyDen?.IsFamilyDenFor(pawn) == true) Consider(familyDen);
                foreach (Thing candidate in index.Nearby(pawn.Position, 24))
                    Consider(candidate);
            }
            Thing best = null;
            float bestLength = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Thing candidate = i == 0 ? nearest1 : i == 1 ? nearest2
                    : i == 2 ? nearest3 : familyDen;
                if (candidate == null || (i == 3 && (candidate == nearest1
                    || candidate == nearest2 || candidate == nearest3
                    || !candidate.Spawned || candidate.Map != pawn.Map
                    || !candidate.Position.InHorDistOf(pawn.Position, 24f)
                    || !SafeDestination(candidate.Position, threats, safeDestinationRanges)
                    || !familyDen.Accepts(pawn))))
                    continue;
                IntVec3 delta = candidate.Position - pawn.Position;
                if (Math.Max(Math.Abs(delta.x), Math.Abs(delta.z)) + 1 > bestLength
                    || !pawn.CanReserve(candidate, int.MaxValue, 0)
                    || !SafePath(pawn, candidate.Position, threats, out float length))
                    continue;
                if (length < bestLength
                    || candidate == familyDen && Math.Abs(length - bestLength) < 0.1f)
                {
                    best = candidate;
                    bestLength = length;
                }
            }
            if (best != null)
            {
                // Claim while issuing the job, not on arrival. This prevents
                // simultaneous AI decisions overbooking the same tree.
                if (best is Plant selectedTree && !index.TryClaimTree(selectedTree, pawn, out _))
                    return flee;
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
                    for (int i = GenRadial.NumCellsInRadius(18f) - 1; i >= 1; i--)
                    {
                        IntVec3 c = pawn.Position + GenRadial.RadialPattern[i];
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
            if (!ModConstants.AdvancedThreatAvoidance) return null;
            bool burrower = DefModExtensionCache<ModExtension_Burrower>.Has(pawn);
            bool climber = DefModExtensionCache<ModExtension_TreeClimber>.Has(pawn);
            bool hasFamilyDen = pawn.TryGetComp<CompAnimalStrategyState>()?.familyBurrow
                ?.IsFamilyDenFor(pawn) == true;
            bool denSpecies = AnimalOffspringShelter.For(pawn.def) == OffspringShelterMode.DEN;
            if ((!burrower && !climber && !hasFamilyDen && !denSpecies)
                || !pawn.Spawned || pawn.Downed)
                return null;
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
                // Test the remembered nursery first, before scanning any
                // alternative bed. Still require a safe, reachable path.
                Building_AnimalBurrow preferred = state.familyBurrow;
                if (preferred?.Spawned == true && preferred.Map == pawn.Map
                    && preferred.IsFamilyDenFor(pawn) && preferred.Accepts(pawn)
                    && !preferred.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(preferred, PathEndMode.OnCell,
                        Danger.None, int.MaxValue, 0))
                    return JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_EnterBurrow"), preferred);

                Thing first = null, second = null, third = null;
                float fd = float.MaxValue, sd = float.MaxValue, td = float.MaxValue;
                foreach (Thing bed in index.Nearby(pawn.Position, 16))
                {
                    if (bed.IsForbidden(pawn)) continue;
                    if (bed is Building_AnimalBurrow b)
                    {
                        if ((!burrower && !denSpecies) || !b.Accepts(pawn))
                            continue;
                    }
                    else if (bed is Plant tree)
                    {
                        if (!climber || !AnimalDefense.UsableTree(pawn, tree)
                            || AnimalDefense.PredatorInTree(pawn, tree) != null
                            || !index.TreeHasRoom(tree, pawn))
                            continue;
                    }
                    else continue;
                    float d = (bed.Position - pawn.Position).LengthHorizontalSquared;
                    if (d < fd)
                    {
                        third = second; td = sd;
                        second = first; sd = fd;
                        first = bed; fd = d;
                    }
                    else if (d < sd)
                    {
                        third = second; td = sd;
                        second = bed; sd = d;
                    }
                    else if (d < td)
                    {
                        third = bed; td = d;
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    Thing bed = i == 0 ? first : i == 1 ? second : third;
                    if (bed == null || !pawn.CanReserveAndReach(
                        bed, PathEndMode.OnCell, Danger.None, int.MaxValue, 0))
                        continue;
                    if (bed is Plant tree && !index.TryClaimTree(tree, pawn, out _))
                        continue;
                    if (bed is Building_AnimalBurrow known) state.Remember(known);
                    return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(
                        bed is Building_AnimalBurrow
                            ? "Zoology_EnterBurrow" : "Zoology_SleepInTree"), bed);
                }
            }
            if (!burrower || pawn.BodySize >
                DefModExtensionCache<ModExtension_Burrower>.Get(pawn.def).maxBodySize)
                return null;
            // Awake animals with an existing nearby burrow need no sleep-path search.
            foreach (Thing refuge in index.Nearby(pawn.Position, 16))
                if (refuge is Building_AnimalBurrow burrow && burrow.Spawned
                    && burrow.Accepts(pawn) && !burrow.IsForbidden(pawn))
                {
                    state.Remember(burrow);
                    return null;
                }
            for (int i = 0; i < state.familiarBurrows.Count; i++)
                if (state.familiarBurrows[i]?.Spawned == true
                    && state.familiarBurrows[i].Map == pawn.Map)
                    return null;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, 8f, true))
                if (Diggable(pawn, c) && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.None))
                    return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_DigBurrow"), c);
            return null;
        }
        internal static bool Diggable(Pawn pawn, IntVec3 c) =>
            Diggable(pawn, c, Building_AnimalBurrow.SelectDefFor(pawn));

        internal static bool Diggable(Pawn pawn, IntVec3 c, ThingDef burrowDef)
        {
            if (pawn?.Map == null || burrowDef == null) return false;
            foreach (IntVec3 part in GenAdj.OccupiedRect(
                c, Rot4.North, burrowDef.size).Cells)
                if (!part.InBounds(pawn.Map) || !part.Standable(pawn.Map)
                    || part.IsForbidden(pawn) || part.GetEdifice(pawn.Map) != null
                    || (part.GetFirstPawn(pawn.Map) is Pawn other && other != pawn)
                    || !pawn.CanReserve(part)
                    || !part.GetTerrain(pawn.Map).affordances.Contains(
                        Building_AnimalBurrow.Settings.diggableTerrainAffordance))
                    return false;
            return true;
        }
    }
    public class JobDriver_DigAnimalBurrow : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!ModConstants.AdvancedThreatAvoidance
                && !AnimalWinterDormancy.CanUseWinterDen(pawn)) return false;
            ThingDef shelter = Building_AnimalBurrow.SelectDefFor(pawn);
            if (!JobGiver_AnimalBurrow.Diggable(pawn, TargetA.Cell, shelter))
                return false;
            // Reserve the entire 1x1, 2x2 or 3x3 footprint. A second digger
            // must not start an overlapping excavation.
            foreach (IntVec3 part in GenAdj.OccupiedRect(
                TargetA.Cell, Rot4.North, shelter.size).Cells)
                if (!pawn.Reserve(new LocalTargetInfo(part), job, 1, -1, null,
                    errorOnFailed))
                    return false;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => (!ModConstants.AdvancedThreatAvoidance
                    && !AnimalWinterDormancy.CanUseWinterDen(pawn))
                || pawn.mindState.meleeThreat != null
                || pawn.mindState.enemyTarget != null || pawn.Downed);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(Building_AnimalBurrow.Settings.digTicks);
            yield return Toils_General.Do(() =>
            {
                ThingDef shelter = Building_AnimalBurrow.SelectDefFor(pawn);
                if (!JobGiver_AnimalBurrow.Diggable(pawn, TargetA.Cell, shelter))
                    return;
                var burrow = (Building_AnimalBurrow)ThingMaker.MakeThing(shelter);
                burrow.InitializeFor(pawn);
                GenSpawn.Spawn(burrow, TargetA.Cell, Map);
                CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
                state?.Remember(burrow);
                if (AnimalOffspringShelter.For(pawn.def) == OffspringShelterMode.DEN
                    && pawn.gender == Gender.Female && pawn.relations != null)
                    foreach (Pawn child in pawn.relations.Children)
                        if (child != null && !child.Dead && child.MapHeld == Map
                            && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                                child.ageTracker?.CurLifeStage))
                        {
                            if (burrow.TryAssignFamily(pawn) && state != null)
                                state.familyBurrow = burrow;
                            break;
                        }
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
        private int safeDescentTicks;
        private int treeWoundLickFinishTick;
        private bool hasCanopyOffset;
        private float canopyOffsetX, canopyOffsetY, canopyOffsetZ;
        // The position and whole-body depth are fixed at ascent. Rendering
        // never scans the tree, reads pixels or searches for other occupants.
        public override Vector3 ForcedBodyOffset => !AtHeight ? Vector3.zero
            : hasCanopyOffset
                ? new Vector3(canopyOffsetX, canopyOffsetY, canopyOffsetZ)
                : new Vector3(0f, 0f, Mathf.Clamp(
                    (TargetA.Thing?.def.graphicData?.drawSize.y ?? 2f) * 0.45f, 0.8f, 1.8f));
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AtHeight, "atHeight");
            Scribe_Values.Look(ref safeDescentTicks, "safeDescentTicks");
            Scribe_Values.Look(ref treeWoundLickFinishTick, "treeWoundLickFinishTick");
            Scribe_Values.Look(ref hasCanopyOffset, "hasCanopyOffset");
            Scribe_Values.Look(ref canopyOffsetX, "canopyOffsetX");
            Scribe_Values.Look(ref canopyOffsetY, "canopyOffsetY");
            Scribe_Values.Look(ref canopyOffsetZ, "canopyOffsetZ");
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!ModConstants.AdvancedThreatAvoidance) return false;
            Plant tree = TargetA.Thing as Plant;
            var component = pawn.Map?.GetComponent<AnimalRefugeMapComponent>();
            if (tree == null || component == null
                || !component.TryClaimTree(tree, pawn, out _))
                return false;
            // Vanilla's exclusive plant reservation cannot express summed
            // BodySize; the separate tree claim enforces that budget.
            bool reserved = pawn.Reserve(TargetA, job, int.MaxValue, 0, null, errorOnFailed);
            if (!reserved) component.ReleaseTreeClaim(tree, pawn);
            return reserved;
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !AnimalDefense.UsableTree(pawn, TargetA.Thing as Plant) || pawn.Downed);
            AddFinishAction(_ =>
            {
                pawn.Map?.GetComponent<AnimalRefugeMapComponent>()
                    ?.ReleaseTreeClaim(TargetA.Thing as Plant, pawn);
                AtHeight = false;
                asleep = false;
                hasCanopyOffset = false;
                treeWoundLickFinishTick = 0;
                PredationDecisionCache.InvalidatePrey(pawn);
            });
            yield return AnimalDefense.RefugeGoto(pawn);
            Toil stay = ToilMaker.MakeToil();
            bool sleep = job.def.defName == "Zoology_SleepInTree";
            stay.initAction = () =>
            {
                Plant tree = TargetA.Thing as Plant;
                Pawn predator = AnimalDefense.PredatorInTree(pawn, tree);
                if (predator != null)
                {
                    EndJobWith(JobCondition.InterruptForced);
                    AnimalDefense.StartGroundEscape(pawn, predator, false);
                    return;
                }

                var refuge = Map?.GetComponent<AnimalRefugeMapComponent>();
                if (refuge == null || !refuge.TryClaimTree(tree, pawn, out int slot))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Vector3 canopyPosition = AnimalTreeCanopy.DrawOffset(tree, slot, pawn.BodySize);
                canopyOffsetX = canopyPosition.x;
                canopyOffsetY = canopyPosition.y;
                canopyOffsetZ = canopyPosition.z;
                hasCanopyOffset = true;
                AtHeight = true; asleep = sleep; safeDescentTicks = 0;
                if (sleep) pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
                pawn.pather.StopDead(); AnimalDefense.InvalidatePrey(pawn, Map);
                TryStartWoundLicking();
                AnimalDefense.EvictTreePrey(pawn, tree);
            };
            stay.tickAction = () =>
            {
                if (sleep)
                {
                    pawn.needs?.rest?.TickResting(1f);
                    // Use the vanilla SleepZ fleck and keep it aligned with the elevated pawn.
                    if (pawn.IsHashIntervalTick(100) && pawn.Position.ShouldSpawnMotesAt(Map))
                    {
                        FleckCreationData fleck = FleckMaker.GetDataThrowMetaIcon(
                            pawn.Position, Map, FleckDefOf.SleepZ);
                        fleck.spawnPosition += ForcedBodyOffset;
                        Map.flecks.CreateFleck(fleck);
                    }
                }
                if (!pawn.IsHashIntervalTick(60)) return;
                if (!ModConstants.AdvancedThreatAvoidance)
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                TickWoundLicking();
                if (treeWoundLickFinishTick > 0 && pawn.IsHashIntervalTick(120)
                    && pawn.Position.ShouldSpawnMotesAt(Map))
                {
                    FleckCreationData healing = FleckMaker.GetDataThrowMetaIcon(
                        pawn.Position, Map, FleckDefOf.HealingCross);
                    healing.spawnPosition += ForcedBodyOffset;
                    Map.flecks.CreateFleck(healing);
                }
                bool hungry = pawn.needs?.food?.CurLevelPercentage < Building_AnimalBurrow.Settings.hungerExitThreshold;
                // Include ground threats in the descent decision: they cannot
                // strike the elevated animal but can make the ground unsafe.
                var threats = Patch_AnimalFleeFromPredators.RefugeThreats(pawn, TargetB.Pawn);
                AnimalDefense.RemoveUncatchablePassiveThreats(pawn, threats);
                Pawn reachableThreat = null;
                Plant refugeTree = TargetA.Thing as Plant;
                for (int i = 0; i < threats.Count; i++)
                {
                    Pawn attacker = threats[i];
                    if (AnimalDefense.IsRanged(attacker) || attacker.Flying
                        || attacker.flight?.CanFlyNow == true
                        || AnimalDefense.DirectClimbingThreat(attacker, pawn, refugeTree))
                    {
                        reachableThreat = attacker;
                        break;
                    }
                }
                if (hungry || reachableThreat != null)
                {
                    Pawn source = reachableThreat ?? (threats.Count > 0 ? threats[0] : null);
                    EndJobWith(JobCondition.InterruptForced);
                    AnimalDefense.StartGroundEscape(pawn, source, hungry);
                    return;
                }
                if (sleep)
                {
                    if (pawn.needs?.rest?.CurLevelPercentage >= 0.95f
                        && treeWoundLickFinishTick <= 0)
                        EndJobWith(JobCondition.Succeeded);
                }
                else if (threats.Count == 0)
                {
                    // Exit only after several consecutive threat-free observations.
                    // The original threat target alone does not prolong the refuge.
                    safeDescentTicks += 60;
                    if (safeDescentTicks >= 180)
                        EndJobWith(JobCondition.Succeeded);
                }
                else
                {
                    safeDescentTicks = 0;
                }
            };
            stay.defaultCompleteMode = ToilCompleteMode.Never;
            yield return stay;
        }
        // The normal wound-licking job cannot run during the continuous tree
        // refuge job. Run its same tend utility and duration in this existing
        // toil, only on the current 60-tick refuge check. No competing jobs.
        private void TryStartWoundLicking()
        {
            if (treeWoundLickFinishTick > 0
                || !AnimalWoundLickingUtility.HasLickableWounds(pawn))
                return;
            treeWoundLickFinishTick = GenTicks.TicksGame
                + AnimalWoundLickingUtility.GetLickDurationTicks();
        }

        private void TickWoundLicking()
        {
            if (!AnimalWoundLickingUtility.CanUseWoundLicking(pawn))
            {
                treeWoundLickFinishTick = 0;
                return;
            }

            if (treeWoundLickFinishTick > 0
                && GenTicks.TicksGame >= treeWoundLickFinishTick)
            {
                // Exact same TendUtility-backed treatment as the ground job.
                AnimalWoundLickingUtility.TryApplyWoundLicking(pawn);
                treeWoundLickFinishTick = 0;
            }

            TryStartWoundLicking();
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
            if (!__instance.verbProps.IsMeleeAttack) return true;
            if (targ.Thing is Building_AnimalNursery nest && nest.TreeNest
                && !nest.PredatorCanOpen(__instance.CasterPawn))
            {
                __result = false;
                return false;
            }
            if (!AnimalDefense.BlocksMelee(__instance.CasterPawn, targ.Pawn)) return true;
            __result = false;
            return false;
        }
    }
}
