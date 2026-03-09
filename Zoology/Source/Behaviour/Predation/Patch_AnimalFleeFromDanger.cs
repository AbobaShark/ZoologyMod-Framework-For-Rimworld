using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class PawnThreatUtility
    {
        private static readonly System.Reflection.PropertyInfo RacePropsIsMechanoidProperty =
            AccessTools.Property(typeof(RaceProperties), "IsMechanoid");

        public static bool IsHumanlikeOrMechanoid(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return true;
            }

            if (RacePropsIsMechanoidProperty == null)
            {
                return false;
            }

            try
            {
                object value = RacePropsIsMechanoidProperty.GetValue(pawn.RaceProps, null);
                return value is bool isMechanoid && isMechanoid;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_AnimalFleeFromPredators
    {
        private readonly struct NoThreatScanCacheEntry
        {
            public NoThreatScanCacheEntry(int nextScanTick, int mapId, bool predatorsEnabled, bool humansEnabled)
            {
                NextScanTick = nextScanTick;
                MapId = mapId;
                PredatorsEnabled = predatorsEnabled;
                HumansEnabled = humansEnabled;
            }

            public int NextScanTick { get; }
            public int MapId { get; }
            public bool PredatorsEnabled { get; }
            public bool HumansEnabled { get; }
        }

        private readonly struct AcceptablePreyCacheEntry
        {
            public AcceptablePreyCacheEntry(bool isAcceptable, int tick)
            {
                IsAcceptable = isAcceptable;
                Tick = tick;
            }

            public bool IsAcceptable { get; }
            public int Tick { get; }
        }

        private readonly struct ActivePredatorThreatEntry
        {
            public ActivePredatorThreatEntry(Pawn predator, Pawn targetPawn)
            {
                Predator = predator;
                TargetPawn = targetPawn;
            }

            public Pawn Predator { get; }
            public Pawn TargetPawn { get; }
        }

        private sealed class ThreatMapCacheData
        {
            public int RefreshTick = -ThreatMapRefreshIntervalTicks;
            public readonly Dictionary<int, List<Pawn>> HumanlikeThreatsByCell = new Dictionary<int, List<Pawn>>(256);
            public readonly Dictionary<int, List<Pawn>> PassivePredatorsByCell = new Dictionary<int, List<Pawn>>(256);
            public readonly Dictionary<int, List<ActivePredatorThreatEntry>> ActivePredatorsByCell = new Dictionary<int, List<ActivePredatorThreatEntry>>(256);
            public readonly List<List<Pawn>> PawnListPool = new List<List<Pawn>>(128);
            public readonly List<List<ActivePredatorThreatEntry>> ActivePredatorListPool = new List<List<ActivePredatorThreatEntry>>(64);
        }

        private static class ThreatMapCache
        {
            private static readonly Dictionary<int, ThreatMapCacheData> byMapId = new Dictionary<int, ThreatMapCacheData>();

            public static ThreatMapCacheData Get(Map map, int currentTick)
            {
                if (map == null)
                {
                    return null;
                }

                int mapId = map.uniqueID;
                if (!byMapId.TryGetValue(mapId, out ThreatMapCacheData data))
                {
                    data = new ThreatMapCacheData();
                    byMapId[mapId] = data;
                }

                if (currentTick - data.RefreshTick < ThreatMapRefreshIntervalTicks)
                {
                    return data;
                }

                data.RefreshTick = currentTick;
                RecyclePawnCellIndex(data.HumanlikeThreatsByCell, data.PawnListPool);
                RecyclePawnCellIndex(data.PassivePredatorsByCell, data.PawnListPool);
                RecycleActivePredatorCellIndex(data.ActivePredatorsByCell, data.ActivePredatorListPool);

                IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null || pawns.Count == 0)
                {
                    return data;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn candidate = pawns[i];
                    if (candidate == null || !candidate.Spawned || candidate.Dead || candidate.Destroyed || candidate.Downed)
                    {
                        continue;
                    }

                    if (PawnThreatUtility.IsHumanlikeOrMechanoid(candidate))
                    {
                        AddPawnThreatToCells(data.HumanlikeThreatsByCell, data.PawnListPool, map, candidate, HumanSearchRadius);
                    }

                    if (candidate.RaceProps?.predator != true)
                    {
                        continue;
                    }

                    if (TryGetThreatTargetPawn(candidate, out Pawn targetPawn))
                    {
                        AddActivePredatorThreatToCells(
                            data.ActivePredatorsByCell,
                            data.ActivePredatorListPool,
                            map,
                            new ActivePredatorThreatEntry(candidate, targetPawn),
                            PredatorSearchRadius);
                    }
                    else
                    {
                        AddPawnThreatToCells(
                            data.PassivePredatorsByCell,
                            data.PawnListPool,
                            map,
                            candidate,
                            PredatorSearchRadius * NonHostilePredatorSearchRadiusFactor);
                    }
                }

                return data;
            }

            private static void AddPawnThreatToCells(
                Dictionary<int, List<Pawn>> byCell,
                List<List<Pawn>> pool,
                Map map,
                Pawn threat,
                float radius)
            {
                if (map == null || threat == null)
                {
                    return;
                }

                int cellCount = GenRadial.NumCellsInRadius(radius);
                for (int i = 0; i < cellCount; i++)
                {
                    IntVec3 cell = threat.Position + GenRadial.RadialPattern[i];
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    int cellIndex = CellIndex(map, cell);
                    if (!byCell.TryGetValue(cellIndex, out List<Pawn> list))
                    {
                        list = RentPawnList(pool);
                        byCell[cellIndex] = list;
                    }

                    list.Add(threat);
                }
            }

            private static void AddActivePredatorThreatToCells(
                Dictionary<int, List<ActivePredatorThreatEntry>> byCell,
                List<List<ActivePredatorThreatEntry>> pool,
                Map map,
                ActivePredatorThreatEntry threat,
                float radius)
            {
                Pawn predator = threat.Predator;
                if (map == null || predator == null)
                {
                    return;
                }

                int cellCount = GenRadial.NumCellsInRadius(radius);
                for (int i = 0; i < cellCount; i++)
                {
                    IntVec3 cell = predator.Position + GenRadial.RadialPattern[i];
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    int cellIndex = CellIndex(map, cell);
                    if (!byCell.TryGetValue(cellIndex, out List<ActivePredatorThreatEntry> list))
                    {
                        list = RentActivePredatorList(pool);
                        byCell[cellIndex] = list;
                    }

                    list.Add(threat);
                }
            }

            private static void RecyclePawnCellIndex(Dictionary<int, List<Pawn>> byCell, List<List<Pawn>> pool)
            {
                foreach (List<Pawn> list in byCell.Values)
                {
                    list.Clear();
                    pool.Add(list);
                }

                byCell.Clear();
            }

            private static void RecycleActivePredatorCellIndex(
                Dictionary<int, List<ActivePredatorThreatEntry>> byCell,
                List<List<ActivePredatorThreatEntry>> pool)
            {
                foreach (List<ActivePredatorThreatEntry> list in byCell.Values)
                {
                    list.Clear();
                    pool.Add(list);
                }

                byCell.Clear();
            }

            private static List<Pawn> RentPawnList(List<List<Pawn>> pool)
            {
                int count = pool.Count;
                if (count == 0)
                {
                    return new List<Pawn>(4);
                }

                List<Pawn> list = pool[count - 1];
                pool.RemoveAt(count - 1);
                return list;
            }

            private static List<ActivePredatorThreatEntry> RentActivePredatorList(List<List<ActivePredatorThreatEntry>> pool)
            {
                int count = pool.Count;
                if (count == 0)
                {
                    return new List<ActivePredatorThreatEntry>(4);
                }

                List<ActivePredatorThreatEntry> list = pool[count - 1];
                pool.RemoveAt(count - 1);
                return list;
            }

            private static int CellIndex(Map map, IntVec3 cell)
            {
                return cell.z * map.Size.x + cell.x;
            }
        }

        private static readonly Dictionary<int, NoThreatScanCacheEntry> noThreatScanCacheByPawnId = new Dictionary<int, NoThreatScanCacheEntry>(128);
        private static readonly Dictionary<long, AcceptablePreyCacheEntry> acceptablePreyCacheByPairKey = new Dictionary<long, AcceptablePreyCacheEntry>(256);

        private const float PredatorSearchRadius = 12f;
        private const float NonHostilePredatorSearchRadiusFactor = 0.5f;
        private const float HumanSearchRadius = 6f;
        private const int FleeDistanceDefault = 12;
        private const int FleeDistanceTarget = 16;
        private const int NoThreatScanCooldownTicks = 60;
        private const int ThreatMapRefreshIntervalTicks = 30;
        private const int AcceptablePreyCacheDurationTicks = 30;
        private const int ThreatCacheCleanupIntervalTicks = 600;

        private static int lastThreatCacheCleanupTick = -ThreatCacheCleanupIntervalTicks;

        public static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null
                || settings.EnablePreyFleeFromPredators
                || settings.AnimalsFreeFromHumans;
        }

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                bool fleeFromPredatorsEnabled = settings == null || settings.EnablePreyFleeFromPredators;
                bool fleeFromHumansEnabled = settings != null && settings.AnimalsFreeFromHumans;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                if (pawn == null || pawn.Map == null || !pawn.RaceProps.Animal || pawn.Dead || pawn.Destroyed)
                {
                    return;
                }

                if (__result != null || pawn.jobs?.curJob?.def == JobDefOf.Flee)
                {
                    ClearNoThreatScanCache(pawn);
                    return;
                }

                if (ShouldSkipThreatScan(pawn, currentTick, fleeFromPredatorsEnabled, fleeFromHumansEnabled))
                {
                    return;
                }

                if (!fleeFromPredatorsEnabled)
                {
                    if (!fleeFromHumansEnabled)
                    {
                        RememberNoThreatScan(pawn, currentTick, fleeFromPredatorsEnabled, fleeFromHumansEnabled);
                        return;
                    }

                    Job humanFleeJob = TryCreateHumanFleeJob(pawn, settings);
                    if (humanFleeJob != null)
                    {
                        ClearNoThreatScanCache(pawn);
                        __result = humanFleeJob;
                        return;
                    }
                    
                    RememberNoThreatScan(pawn, currentTick, fleeFromPredatorsEnabled, fleeFromHumansEnabled);
                    return;
                }

                if (TryHandlePredatorThreat(pawn, settings, out Job predatorFleeJob))
                {
                    ClearNoThreatScanCache(pawn);
                    if (predatorFleeJob != null)
                    {
                        __result = predatorFleeJob;
                    }
                    return;
                }

                if (!fleeFromHumansEnabled || __result != null)
                {
                    if (__result == null)
                    {
                        RememberNoThreatScan(pawn, currentTick, fleeFromPredatorsEnabled, fleeFromHumansEnabled);
                    }
                    return;
                }

                Job humanJob = TryCreateHumanFleeJob(pawn, settings);
                if (humanJob != null)
                {
                    ClearNoThreatScanCache(pawn);
                    __result = humanJob;
                    return;
                }

                if (__result == null)
                {
                    RememberNoThreatScan(pawn, currentTick, fleeFromPredatorsEnabled, fleeFromHumansEnabled);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Patch_AnimalFleeFromPredators failed: {e}");
            }
        }

        private static bool TryHandlePredatorThreat(Pawn pawn, ZoologyModSettings settings, out Job fleeJob)
        {
            fleeJob = null;
            bool allowNonHostilePredators = settings != null
                && settings.EnablePreyFleeFromPredators
                && settings.AnimalsFleeFromNonHostlePredators;

            if (HasActiveCloseMeleeThreatFromPredator(pawn))
            {
                return true;
            }

            bool preyIsPhotonozoa = PredationCacheUtility.IsPhotonozoa(pawn.def);
            Pawn threat = FindNearestPredatorThreat(pawn, PredatorSearchRadius, allowNonHostilePredators);
            if (threat == null)
            {
                return false;
            }

            bool foodJobActive = IsFoodSeekingOrEatingJob(pawn);
            bool shouldAnimalFleeDanger = FleeUtility.ShouldAnimalFleeDanger(pawn);
            if (!preyIsPhotonozoa && !shouldAnimalFleeDanger)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool threatAimingAtPawn = IsThreatTargetingPawn(threat, pawn);
            if (foodJobActive && !threatAimingAtPawn)
            {
                return false;
            }

            int fleeDistance = threatAimingAtPawn ? FleeDistanceTarget : FleeDistanceDefault;

            bool bothPhotonozoaInTheirFaction = IsPhotonozoaPairInTheirFaction(threat, pawn);
            if (!bothPhotonozoaInTheirFaction)
            {
                if (preyIsPhotonozoa && !shouldAnimalFleeDanger)
                {
                    return false;
                }
            }
            else
            {
                if (pawn.InMentalState || pawn.IsFighting() || pawn.Downed)
                {
                    return false;
                }

                if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
                {
                    return false;
                }

                if (pawn.Faction == Faction.OfPlayer && pawn.Map.IsPlayerHome)
                {
                    return false;
                }

                if (HasFreshFleeJob(pawn, currentTick))
                {
                    return false;
                }
            }

            if (threatAimingAtPawn)
            {
                HandlePursuitAllowanceIfNeeded(threat, pawn);
            }

            fleeJob = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            return fleeJob != null;
        }

        private static Job TryCreateHumanFleeJob(Pawn pawn, ZoologyModSettings settings)
        {
            if (!CanAnimalFleeFromHumans(pawn, settings))
            {
                return null;
            }

            Pawn threat = FindNearestHumanlikeThreat(pawn, HumanSearchRadius);
            if (threat == null)
            {
                return null;
            }

            return FleeUtility.FleeJob(pawn, threat, FleeDistanceDefault);
        }

        private static bool CanAnimalFleeFromHumans(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null || settings == null || !settings.AnimalsFreeFromHumans)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map == null || pawn.Faction != null || pawn.RaceProps.Humanlike)
            {
                return false;
            }

            if (pawn.Downed || pawn.InMentalState || pawn.IsFighting())
            {
                return false;
            }

            if (IsFoodSeekingOrEatingJob(pawn))
            {
                return false;
            }

            if (HasActiveCloseMeleeThreatFromHumanlike(pawn))
            {
                return false;
            }

            if (!settings.GetAnimalsFreeFromHumansFor(pawn.def))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (HasFreshFleeJob(pawn, currentTick))
            {
                return false;
            }

            if (HasAggressiveJob(pawn))
            {
                return false;
            }

            if (IsPredatorGuardingCorpseAgainstHumans(pawn, settings))
            {
                return false;
            }

            return FleeUtility.ShouldAnimalFleeDanger(pawn);
        }

        private static bool ShouldSkipThreatScan(Pawn pawn, int currentTick, bool predatorsEnabled, bool humansEnabled)
        {
            if (pawn == null || pawn.Map == null || currentTick <= 0)
            {
                return false;
            }

            if (predatorsEnabled
                && HasActiveCloseMeleeThreatFromPredator(pawn))
            {
                ClearNoThreatScanCache(pawn);
                return false;
            }

            int pawnId = pawn.thingIDNumber;
            if (!noThreatScanCacheByPawnId.TryGetValue(pawnId, out NoThreatScanCacheEntry cached))
            {
                return false;
            }

            if (cached.MapId != pawn.Map.uniqueID
                || cached.PredatorsEnabled != predatorsEnabled
                || cached.HumansEnabled != humansEnabled
                || currentTick >= cached.NextScanTick)
            {
                noThreatScanCacheByPawnId.Remove(pawnId);
                return false;
            }

            return true;
        }

        private static void RememberNoThreatScan(Pawn pawn, int currentTick, bool predatorsEnabled, bool humansEnabled)
        {
            if (pawn?.Map == null || currentTick <= 0)
            {
                return;
            }

            noThreatScanCacheByPawnId[pawn.thingIDNumber] = new NoThreatScanCacheEntry(
                currentTick + NoThreatScanCooldownTicks,
                pawn.Map.uniqueID,
                predatorsEnabled,
                humansEnabled);
            CleanupThreatCachesIfNeeded(currentTick);
        }

        private static void ClearNoThreatScanCache(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            noThreatScanCacheByPawnId.Remove(pawn.thingIDNumber);
        }

        private static bool HasAggressiveJob(Pawn pawn)
        {
            Job curJob = pawn?.CurJob;
            if (curJob == null)
            {
                return false;
            }

            if (curJob.def == JobDefOf.AttackMelee)
            {
                return true;
            }

            if (ProtectPreyState.IsProtectPreyJob(curJob, pawn.jobs?.curDriver))
            {
                return true;
            }

            Type driverClass = curJob.def?.driverClass;
            return driverClass != null && typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass);
        }

        private static bool IsFoodSeekingOrEatingJob(Pawn pawn)
        {
            Job curJob = pawn?.CurJob;
            if (curJob?.def == null)
            {
                return false;
            }

            if (curJob.def == JobDefOf.Ingest)
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            if (driverClass != null && typeof(JobDriver_Ingest).IsAssignableFrom(driverClass))
            {
                return true;
            }

            string defName = curJob.def.defName;
            if (!string.IsNullOrEmpty(defName) && defName.IndexOf("Ingest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string driverName = driverClass?.Name;
            return !string.IsNullOrEmpty(driverName)
                && driverName.IndexOf("Ingest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPredatorGuardingCorpseAgainstHumans(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null
                || settings == null
                || !settings.AnimalsFreeFromHumans
                || !settings.EnablePredatorDefendCorpse
                || !ZoologyModSettings.CanPredatorDefendPreyFromHumansAndMechanoidsNow(pawn))
            {
                return false;
            }

            try
            {
                Corpse pairedCorpse = PredatorPreyPairGameComponent.Instance?.GetPairedCorpse(pawn);
                return pairedCorpse != null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[ZoologyMod] Failed to check paired corpse for {pawn?.LabelShort}: {ex}");
                return false;
            }
        }

        private static bool HasFreshFleeJob(Pawn pawn, int currentTick)
        {
            Job curJob = pawn?.jobs?.curJob;
            return curJob != null
                && curJob.def == JobDefOf.Flee
                && curJob.startTick == currentTick;
        }

        private static bool HasActiveCloseMeleeThreatFromPredator(Pawn pawn)
        {
            return HasActiveCloseMeleeThreat(pawn, IsPredatorLikeThreat);
        }

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn)
        {
            return HasActiveCloseMeleeThreat(pawn, IsHumanlikeThreat);
        }

        private static bool HasActiveCloseMeleeThreat(Pawn pawn, Predicate<Pawn> threatPredicate)
        {
            if (pawn == null || pawn.Map == null || !pawn.Spawned || threatPredicate == null)
            {
                return false;
            }

            Map map = pawn.Map;
            IntVec3 position = pawn.Position;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    IntVec3 cell = new IntVec3(position.x + dx, position.y, position.z + dz);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (!(things[i] is Pawn otherPawn) || otherPawn == pawn)
                        {
                            continue;
                        }

                        if (IsThreatMeleeAttackingPawn(otherPawn, pawn, threatPredicate))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsThreatMeleeAttackingPawn(Pawn threat, Pawn pawn, Predicate<Pawn> threatPredicate)
        {
            if (threat?.CurJob?.def != JobDefOf.AttackMelee)
            {
                return false;
            }

            Pawn targetPawn = threat.CurJob.GetTarget(TargetIndex.A).Thing as Pawn;
            return targetPawn == pawn
                && IsValidActiveMeleeThreatPair(threat, pawn, threatPredicate)
                && IsExplicitlyHostileToPawn(threat, pawn);
        }

        private static bool IsValidActiveMeleeThreatPair(Pawn threat, Pawn pawn, Predicate<Pawn> threatPredicate)
        {
            return threat != null
                && pawn != null
                && threat != pawn
                && !threat.Dead
                && !threat.Destroyed
                && !threat.Downed
                && threat.Spawned
                && pawn.Spawned
                && threat.Map == pawn.Map
                && threat.Position.AdjacentTo8WayOrInside(pawn.Position)
                && threatPredicate(threat);
        }

        private static bool IsExplicitlyHostileToPawn(Pawn threat, Pawn pawn)
        {
            if (threat == null || pawn == null)
            {
                return false;
            }

            if (threat.HostileTo(pawn))
            {
                return true;
            }

            return threat.CurJob?.def == JobDefOf.AttackMelee
                && threat.CurJob.GetTarget(TargetIndex.A).Thing == pawn;
        }

        private static bool IsHumanlikeThreat(Pawn pawn)
        {
            return PawnThreatUtility.IsHumanlikeOrMechanoid(pawn);
        }

        private static bool IsPredatorLikeThreat(Pawn pawn)
        {
            return pawn != null
                && ((pawn.RaceProps?.predator ?? false) || ProtectPreyState.IsProtectPreyJob(pawn));
        }

        private static bool IsHumanDoingAnimalTamingJob(Pawn human)
        {
            Job curJob = human?.CurJob;
            JobDef jobDef = curJob?.def;
            if (jobDef == null || !DoesJobTargetAnimal(curJob))
            {
                return false;
            }

            if (jobDef == JobDefOf.Tame || jobDef == JobDefOf.Train)
            {
                return true;
            }

            string defName = jobDef.defName;
            if (JobNameMatchesTaming(defName))
            {
                return true;
            }

            Type driverClass = jobDef.driverClass;
            return JobNameMatchesTaming(driverClass?.Name) || JobNameMatchesTaming(driverClass?.FullName);
        }

        private static bool DoesJobTargetAnimal(Job job)
        {
            if (job == null)
            {
                return false;
            }

            return JobTargetIsAnimal(job.targetA)
                || JobTargetIsAnimal(job.targetB)
                || JobTargetIsAnimal(job.targetC);
        }

        private static bool JobTargetIsAnimal(LocalTargetInfo target)
        {
            return target.HasThing && target.Thing is Pawn targetPawn && targetPawn.RaceProps?.Animal == true;
        }

        private static bool JobNameMatchesTaming(string jobName)
        {
            return !string.IsNullOrEmpty(jobName)
                && (jobName.IndexOf("Tame", StringComparison.OrdinalIgnoreCase) >= 0
                    || jobName.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Pawn FindNearestHumanlikeThreat(Pawn pawn, float radius)
        {
            if (pawn?.Map == null)
            {
                return null;
            }

            try
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                ThreatMapCacheData snapshot = ThreatMapCache.Get(pawn.Map, currentTick);
                if (snapshot == null
                    || !TryGetPawnThreatCandidatesForCell(snapshot.HumanlikeThreatsByCell, pawn, out List<Pawn> candidates))
                {
                    return null;
                }

                return FindNearestHumanlikeThreatFromSnapshot(candidates, pawn, radius);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestHumanlikeThreat failed: {ex}");
                return null;
            }
        }

        private static Pawn FindNearestHumanlikeThreatFromSnapshot(List<Pawn> candidates, Pawn pawn, float radius)
        {
            if (candidates == null || pawn == null)
            {
                return null;
            }

            float bestDistanceSquared = radius * radius;
            Pawn bestThreat = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                float distanceSquared = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                if (distanceSquared > bestDistanceSquared || !IsHumanlikeThreatCandidate(candidate, pawn))
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestThreat = candidate;
            }

            return bestThreat;
        }

        private static bool IsHumanlikeThreatCandidate(Pawn human, Pawn animal)
        {
            if (human == null || animal == null)
            {
                return false;
            }

            if (human == animal || !human.Spawned || human.Dead || human.Destroyed || human.Downed)
            {
                return false;
            }

            if (human.Map != animal.Map || !PawnThreatUtility.IsHumanlikeOrMechanoid(human))
            {
                return false;
            }

            if (human.RaceProps?.Humanlike == true && IsHumanDoingAnimalTamingJob(human))
            {
                return false;
            }

            return HasLineOfSightAndReach(human, animal);
        }

        private static bool HasLineOfSightAndReach(Pawn threat, Pawn pawn)
        {
            if (threat == null || pawn == null || threat.Map == null || threat.Map != pawn.Map)
            {
                return false;
            }

            if (threat.Position == pawn.Position)
            {
                return true;
            }

            bool hasLineOfSight;
            try
            {
                hasLineOfSight = GenSight.LineOfSight(pawn.Position, threat.Position, pawn.Map);
            }
            catch
            {
                hasLineOfSight = false;
            }

            if (!hasLineOfSight)
            {
                return false;
            }

            try
            {
                return threat.CanReach(pawn, PathEndMode.Touch, Danger.Deadly);
            }
            catch
            {
                return false;
            }
        }

        private static Pawn FindNearestPredatorThreat(Pawn pawn, float radius, bool allowNonHostilePredators)
        {
            if (pawn?.Map == null)
            {
                return null;
            }

            try
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                ThreatMapCacheData snapshot = ThreatMapCache.Get(pawn.Map, currentTick);
                if (snapshot == null)
                {
                    return null;
                }

                List<ActivePredatorThreatEntry> activeCandidates = null;
                TryGetActivePredatorThreatCandidatesForCell(snapshot.ActivePredatorsByCell, pawn, out activeCandidates);
                Pawn activeThreat = FindNearestActivePredatorThreatFromSnapshot(activeCandidates, pawn, radius);
                if (activeThreat != null || !allowNonHostilePredators)
                {
                    return activeThreat;
                }

                List<Pawn> passiveCandidates = null;
                TryGetPawnThreatCandidatesForCell(snapshot.PassivePredatorsByCell, pawn, out passiveCandidates);
                return FindNearestPassivePredatorThreatFromSnapshot(passiveCandidates, pawn, radius * NonHostilePredatorSearchRadiusFactor);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestPredatorThreat failed: {ex}");
                return null;
            }
        }

        private static bool TryGetPawnThreatCandidatesForCell(Dictionary<int, List<Pawn>> byCell, Pawn pawn, out List<Pawn> candidates)
        {
            candidates = null;
            if (byCell == null || pawn?.Map == null)
            {
                return false;
            }

            return byCell.TryGetValue(GetCellIndex(pawn.Map, pawn.Position), out candidates) && candidates != null && candidates.Count > 0;
        }

        private static bool TryGetActivePredatorThreatCandidatesForCell(
            Dictionary<int, List<ActivePredatorThreatEntry>> byCell,
            Pawn pawn,
            out List<ActivePredatorThreatEntry> candidates)
        {
            candidates = null;
            if (byCell == null || pawn?.Map == null)
            {
                return false;
            }

            return byCell.TryGetValue(GetCellIndex(pawn.Map, pawn.Position), out candidates) && candidates != null && candidates.Count > 0;
        }

        private static int GetCellIndex(Map map, IntVec3 cell)
        {
            return cell.z * map.Size.x + cell.x;
        }

        private static Pawn FindNearestActivePredatorThreatFromSnapshot(List<ActivePredatorThreatEntry> candidates, Pawn prey, float radius)
        {
            if (candidates == null || prey == null)
            {
                return null;
            }

            float bestDistanceSquared = radius * radius;
            Pawn bestThreat = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                ActivePredatorThreatEntry entry = candidates[i];
                Pawn predator = entry.Predator;
                if (predator == null
                    || predator == prey
                    || predator.Dead
                    || predator.Destroyed
                    || predator.Downed
                    || !predator.Spawned
                    || predator.Map != prey.Map
                    || predator.RaceProps?.predator != true)
                {
                    continue;
                }

                float distanceSquared = (predator.Position - prey.Position).LengthHorizontalSquared;
                if (distanceSquared > bestDistanceSquared)
                {
                    continue;
                }

                Pawn targetPawn = entry.TargetPawn;
                if (targetPawn == null || targetPawn.Dead)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestThreat = predator;
            }

            return bestThreat;
        }

        private static Pawn FindNearestPassivePredatorThreatFromSnapshot(List<Pawn> candidates, Pawn prey, float radius)
        {
            if (candidates == null || prey == null)
            {
                return null;
            }

            float bestDistanceSquared = radius * radius;
            Pawn bestThreat = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn predator = candidates[i];
                if (predator == null
                    || predator == prey
                    || predator.Dead
                    || predator.Destroyed
                    || predator.Downed
                    || !predator.Spawned
                    || predator.Map != prey.Map
                    || predator.RaceProps?.predator != true)
                {
                    continue;
                }

                float distanceSquared = (predator.Position - prey.Position).LengthHorizontalSquared;
                if (distanceSquared > bestDistanceSquared || !IsAcceptablePrey(predator, prey))
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestThreat = predator;
            }

            return bestThreat;
        }

        private static void CleanupThreatCachesIfNeeded(int currentTick)
        {
            if (currentTick - lastThreatCacheCleanupTick < ThreatCacheCleanupIntervalTicks)
            {
                return;
            }

            lastThreatCacheCleanupTick = currentTick;
            CleanupNoThreatScanCache(currentTick);
            CleanupAcceptablePreyCache(currentTick);
        }

        private static void CleanupNoThreatScanCache(int currentTick)
        {
            List<int> staleKeys = null;
            foreach (KeyValuePair<int, NoThreatScanCacheEntry> entry in noThreatScanCacheByPawnId)
            {
                if (currentTick < entry.Value.NextScanTick)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<int>(32);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                noThreatScanCacheByPawnId.Remove(staleKeys[i]);
            }
        }

        private static void CleanupAcceptablePreyCache(int currentTick)
        {
            List<long> staleKeys = null;
            foreach (KeyValuePair<long, AcceptablePreyCacheEntry> entry in acceptablePreyCacheByPairKey)
            {
                if (currentTick - entry.Value.Tick <= AcceptablePreyCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<long>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                acceptablePreyCacheByPairKey.Remove(staleKeys[i]);
            }
        }

        private static bool IsActivePredatorThreat(Pawn predator, Pawn prey, out bool targetsPreyDirectly)
        {
            targetsPreyDirectly = false;
            if (!TryGetThreatTargetPawn(predator, out Pawn targetedPawn))
            {
                return false;
            }

            targetsPreyDirectly = ReferenceEquals(targetedPawn, prey);
            return true;
        }

        private static bool IsThreatTargetingPawn(Pawn predator, Pawn prey)
        {
            return IsActivePredatorThreat(predator, prey, out bool targetsPreyDirectly) && targetsPreyDirectly;
        }

        private static bool TryGetThreatTargetPawn(Pawn predator, out Pawn targetPawn)
        {
            targetPawn = null;

            Job curJob = predator?.CurJob;
            if (curJob == null || !curJob.targetA.HasThing || !IsThreatJob(curJob, predator.jobs?.curDriver))
            {
                return false;
            }

            targetPawn = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
            return targetPawn != null && !targetPawn.Dead;
        }

        private static bool IsThreatJob(Job curJob, JobDriver curDriver)
        {
            if (curJob == null || curJob.def == null)
            {
                return false;
            }

            if (curJob.def == JobDefOf.AttackMelee)
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            if (driverClass != null && typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass))
            {
                return true;
            }

            return ProtectPreyState.IsProtectPreyJob(curJob, curDriver);
        }

        private static bool IsAcceptablePrey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long pairKey = PairKey(predator, prey);
            if (pairKey != 0L
                && acceptablePreyCacheByPairKey.TryGetValue(pairKey, out AcceptablePreyCacheEntry cached)
                && currentTick - cached.Tick <= AcceptablePreyCacheDurationTicks)
            {
                return cached.IsAcceptable;
            }

            bool isAcceptable;
            try
            {
                isAcceptable = FoodUtility.IsAcceptablePreyFor(predator, prey);
            }
            catch
            {
                isAcceptable = true;
            }

            if (pairKey != 0L)
            {
                acceptablePreyCacheByPairKey[pairKey] = new AcceptablePreyCacheEntry(isAcceptable, currentTick);
                CleanupThreatCachesIfNeeded(currentTick);
            }

            return isAcceptable;
        }

        private static long PairKey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return 0L;
            }

            uint predatorId = (uint)predator.thingIDNumber;
            uint preyId = (uint)prey.thingIDNumber;
            return ((long)predatorId << 32) | preyId;
        }

        private static void HandlePursuitAllowanceIfNeeded(Pawn predator, Pawn prey)
        {
            try
            {
                var comp = ZoologyPursuitGameComponent.Instance;
                if (comp == null)
                {
                    TryEnsurePursuitComponentRegistered();
                    comp = ZoologyPursuitGameComponent.Instance;
                    if (comp == null)
                    {
                        if (Prefs.DevMode)
                        {
                            Log.Message("[Zoology] WARNING: ZoologyPursuitGameComponent.Instance is null - cannot AllowPursuit.");
                        }
                        return;
                    }
                }

                if (comp.IsPairAllowedNow(predator, prey) || comp.IsPairBlockedNow(predator, prey))
                {
                    return;
                }

                float predatorSpeed = predator.GetStatValue(StatDefOf.MoveSpeed, true);
                float preySpeed = prey.GetStatValue(StatDefOf.MoveSpeed, true);
                if (preySpeed <= predatorSpeed)
                {
                    return;
                }

                comp.AllowPursuit(predator, prey, 2);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] HandlePursuitAllowanceIfNeeded error: {ex}");
            }
        }

        private static void TryEnsurePursuitComponentRegistered()
        {
            try
            {
                if (ZoologyPursuitGameComponent.Instance != null)
                {
                    return;
                }

                Game game = Current.Game;
                if (game?.components == null)
                {
                    return;
                }

                bool exists = false;
                for (int i = 0; i < game.components.Count; i++)
                {
                    if (game.components[i] is ZoologyPursuitGameComponent)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    game.components.Add(new ZoologyPursuitGameComponent(game));
                    Log.Message("[Zoology] Dynamically added ZoologyPursuitGameComponent to Current.Game.components.");
                }

                var _ = ZoologyPursuitGameComponent.Instance;
            }
            catch (Exception exAdd)
            {
                Log.Error($"[Zoology] Failed to dynamically add ZoologyPursuitGameComponent: {exAdd}");
            }
        }

        private static bool IsPhotonozoaPairInTheirFaction(Pawn a, Pawn b)
        {
            try
            {
                if (a == null || b == null)
                {
                    return false;
                }

                return PredationCacheUtility.IsPhotonozoaPairInTheirFaction(a, b);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] IsPhotonozoaPairInTheirFaction error: {ex}");
                return false;
            }
        }
    }
}
