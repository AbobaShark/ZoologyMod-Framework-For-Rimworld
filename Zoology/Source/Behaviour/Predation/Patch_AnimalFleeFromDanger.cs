using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class FleeMeleeThreatTracker
    {
        private const int RecentAttackWindowTicks = 30;
        private const int CleanupIntervalTicks = 600;

        private readonly struct RecentAttackRecord
        {
            public RecentAttackRecord(Pawn attacker, Pawn victim, int tick)
            {
                Attacker = attacker;
                Victim = victim;
                Tick = tick;
            }

            public Pawn Attacker { get; }
            public Pawn Victim { get; }
            public int Tick { get; }
        }

        private static readonly Dictionary<int, RecentAttackRecord> recentAttacksByVictimId = new Dictionary<int, RecentAttackRecord>(128);

        private static int lastCleanupTick = -CleanupIntervalTicks;

        public static bool ShouldTrack(ZoologyModSettings settings)
        {
            return settings == null
                || settings.EnablePreyFleeFromPredators
                || settings.AnimalsFreeFromHumans;
        }

        public static void Record(Pawn attacker, Pawn victim)
        {
            if (!IsRelevantVictim(victim) || !IsRelevantAttacker(attacker))
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick < 0)
            {
                return;
            }

            recentAttacksByVictimId[victim.thingIDNumber] = new RecentAttackRecord(attacker, victim, currentTick);
            if (recentAttacksByVictimId.Count >= 256 && currentTick - lastCleanupTick >= CleanupIntervalTicks)
            {
                Cleanup(currentTick);
            }
        }

        public static bool TryGetRecentHumanlikeAttacker(Pawn victim, out Pawn attacker)
        {
            return TryGetRecentAttacker(victim, candidate => candidate.RaceProps?.Humanlike == true, out attacker);
        }

        public static bool TryGetRecentPredatorAttacker(Pawn victim, out Pawn attacker)
        {
            return TryGetRecentAttacker(victim, IsPredatorLikeAttacker, out attacker);
        }

        public static void InterruptFleeIfNeeded(Pawn victim)
        {
            if (victim?.jobs?.curJob?.def != JobDefOf.Flee)
            {
                return;
            }

            try
            {
                victim.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ZoologyMod] Failed to interrupt flee job for {victim?.LabelShort}: {ex}");
            }
        }

        private static bool TryGetRecentAttacker(Pawn victim, Predicate<Pawn> predicate, out Pawn attacker)
        {
            attacker = null;
            if (victim == null || predicate == null)
            {
                return false;
            }

            int victimId = victim.thingIDNumber;
            if (!recentAttacksByVictimId.TryGetValue(victimId, out RecentAttackRecord record))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - record.Tick > RecentAttackWindowTicks)
            {
                recentAttacksByVictimId.Remove(victimId);
                return false;
            }

            Pawn recordedAttacker = record.Attacker;
            if (!IsRecentAttackStillRelevant(recordedAttacker, victim))
            {
                recentAttacksByVictimId.Remove(victimId);
                return false;
            }

            if (!predicate(recordedAttacker))
            {
                return false;
            }

            attacker = recordedAttacker;
            return true;
        }

        private static bool IsRelevantVictim(Pawn victim)
        {
            return victim != null
                && victim.RaceProps?.Animal == true
                && !victim.Dead
                && !victim.Destroyed;
        }

        private static bool IsRelevantAttacker(Pawn attacker)
        {
            if (attacker == null || attacker.Dead || attacker.Destroyed || attacker.Downed || !attacker.Spawned)
            {
                return false;
            }

            return attacker.RaceProps?.Humanlike == true || IsPredatorLikeAttacker(attacker);
        }

        private static bool IsPredatorLikeAttacker(Pawn attacker)
        {
            return attacker != null
                && ((attacker.RaceProps?.predator ?? false) || ProtectPreyState.IsProtectPreyJob(attacker));
        }

        private static bool IsRecentAttackStillRelevant(Pawn attacker, Pawn victim)
        {
            return attacker != null
                && victim != null
                && attacker != victim
                && attacker.Spawned
                && victim.Spawned
                && !attacker.Dead
                && !attacker.Destroyed
                && !attacker.Downed
                && attacker.Map == victim.Map
                && attacker.Position.AdjacentTo8WayOrInside(victim.Position);
        }

        private static void Cleanup(int currentTick)
        {
            lastCleanupTick = currentTick;

            List<int> staleKeys = null;
            foreach (KeyValuePair<int, RecentAttackRecord> entry in recentAttacksByVictimId)
            {
                bool remove = currentTick - entry.Value.Tick > RecentAttackWindowTicks
                    || !IsRecentAttackStillRelevant(entry.Value.Attacker, entry.Value.Victim);

                if (!remove)
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
                recentAttacksByVictimId.Remove(staleKeys[i]);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
    internal static class Patch_PawnMeleeVerbs_TryMeleeAttack_InterruptFlee
    {
        public static bool Prepare()
        {
            return FleeMeleeThreatTracker.ShouldTrack(ZoologyModSettings.Instance);
        }

        public static void Postfix(Pawn_MeleeVerbs __instance, Thing target, bool __result)
        {
            if (!__result)
            {
                return;
            }

            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (!FleeMeleeThreatTracker.ShouldTrack(settings))
            {
                return;
            }

            Pawn attacker = __instance?.Pawn;
            Pawn victim = target as Pawn;
            if (attacker == null || victim == null)
            {
                return;
            }

            try
            {
                FleeMeleeThreatTracker.Record(attacker, victim);
                FleeMeleeThreatTracker.InterruptFleeIfNeeded(victim);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Failed handling melee flee interruption for {victim?.LabelShort}: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_AnimalFleeFromPredators
    {
        private static readonly ThingRequest PawnRequest = ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        private const float PredatorSearchRadius = 12f;
        private const float NonHostilePredatorSearchRadiusFactor = 0.5f;

        private const float HumanSearchRadius = 6f;
        private const int FleeDistanceDefault = 12;
        private const int FleeDistanceTarget = 16;

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

                if (!fleeFromPredatorsEnabled || pawn == null || pawn.Map == null || !pawn.RaceProps.Animal || pawn.Dead || pawn.Destroyed)
                {
                    if (!fleeFromHumansEnabled || pawn == null || pawn.Map == null || !pawn.RaceProps.Animal || pawn.Dead || pawn.Destroyed || __result != null)
                    {
                        return;
                    }

                    Job humanFleeJob = TryCreateHumanFleeJob(pawn, settings);
                    if (humanFleeJob != null)
                    {
                        __result = humanFleeJob;
                    }
                    return;
                }

                if (TryHandlePredatorThreat(pawn, settings, out Job predatorFleeJob))
                {
                    __result = predatorFleeJob;
                    return;
                }

                if (!fleeFromHumansEnabled || __result != null)
                {
                    return;
                }

                Job humanJob = TryCreateHumanFleeJob(pawn, settings);
                if (humanJob != null)
                {
                    __result = humanJob;
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
            bool foodJobActive = IsFoodSeekingOrEatingJob(pawn);

            bool allowNonHostilePredators = settings != null
                && settings.EnablePreyFleeFromPredators
                && settings.AnimalsFleeFromNonHostlePredators;

            if (HasActiveCloseMeleeThreatFromPredator(pawn))
            {
                return true;
            }

            if (FleeMeleeThreatTracker.TryGetRecentPredatorAttacker(pawn, out _))
            {
                return true;
            }

            bool preyIsPhotonozoa = PredationCacheUtility.IsPhotonozoa(pawn.def);
            if (!preyIsPhotonozoa && !FleeUtility.ShouldAnimalFleeDanger(pawn))
            {
                return false;
            }

            Pawn threat = FindNearestPredatorThreat(pawn, PredatorSearchRadius, allowNonHostilePredators);
            if (threat == null)
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
                if (preyIsPhotonozoa && !FleeUtility.ShouldAnimalFleeDanger(pawn))
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

            if (FleeMeleeThreatTracker.TryGetRecentHumanlikeAttacker(pawn, out _))
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
            return pawn?.RaceProps?.Humanlike == true;
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
            try
            {
                var traverseParms = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn);
                return GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    PawnRequest,
                    PathEndMode.OnCell,
                    traverseParms,
                    radius,
                    t => IsHumanlikeThreatCandidate(t as Pawn, pawn)) as Pawn;
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestHumanlikeThreat failed: {ex}");
                return null;
            }
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

            if (human.Map != animal.Map || human.RaceProps?.Humanlike != true)
            {
                return false;
            }

            if (IsHumanDoingAnimalTamingJob(human))
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
            try
            {
                Pawn activeThreat = FindNearestPredatorThreatInRadius(pawn, radius, requireActiveThreat: true);
                if (activeThreat != null || !allowNonHostilePredators)
                {
                    return activeThreat;
                }

                return FindNearestPredatorThreatInRadius(pawn, radius * NonHostilePredatorSearchRadiusFactor, requireActiveThreat: false);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestPredatorThreat failed: {ex}");
                return null;
            }
        }

        private static Pawn FindNearestPredatorThreatInRadius(Pawn pawn, float radius, bool requireActiveThreat)
        {
            var traverseParms = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn);
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                PawnRequest,
                PathEndMode.OnCell,
                traverseParms,
                radius,
                t => IsPredatorThreatCandidate(t as Pawn, pawn, requireActiveThreat)) as Pawn;
        }

        private static bool IsPredatorThreatCandidate(Pawn predator, Pawn prey, bool requireActiveThreat)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            if (predator == prey || predator.Downed || predator.Dead || predator.Destroyed || !predator.Spawned || !predator.RaceProps.predator)
            {
                return false;
            }

            bool isActiveThreat = IsActivePredatorThreat(predator, prey, out bool targetsPreyDirectly);
            if (targetsPreyDirectly)
            {
                return true;
            }

            if (requireActiveThreat)
            {
                return false;
            }

            if (!IsAcceptablePrey(predator, prey))
            {
                return false;
            }

            return !isActiveThreat;
        }

        private static bool IsActivePredatorThreat(Pawn predator, Pawn prey, out bool targetsPreyDirectly)
        {
            targetsPreyDirectly = false;

            Job curJob = predator?.CurJob;
            if (curJob == null || !curJob.targetA.HasThing || !IsThreatJob(curJob, predator.jobs?.curDriver))
            {
                return false;
            }

            Pawn targetedPawn = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
            if (targetedPawn == null || targetedPawn.Dead)
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
            try
            {
                return FoodUtility.IsAcceptablePreyFor(predator, prey);
            }
            catch
            {
                return true;
            }
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
