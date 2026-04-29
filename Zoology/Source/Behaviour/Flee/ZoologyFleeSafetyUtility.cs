using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZoologyMod
{
    internal static class ZoologyFleeSafetyUtility
    {
        private readonly struct ThreatMeleeAttackPairCacheEntry
        {
            public ThreatMeleeAttackPairCacheEntry(Pawn threat, Pawn prey, Job job, JobDriver driver, bool isThreatMeleeAttacking)
            {
                Threat = threat;
                Prey = prey;
                Job = job;
                Driver = driver;
                IsThreatMeleeAttacking = isThreatMeleeAttacking;
            }

            public Pawn Threat { get; }

            public Pawn Prey { get; }

            public Job Job { get; }

            public JobDriver Driver { get; }

            public bool IsThreatMeleeAttacking { get; }
        }

        private static readonly Dictionary<long, ThreatMeleeAttackPairCacheEntry> meleeAttackPairCacheByPairKey =
            new Dictionary<long, ThreatMeleeAttackPairCacheEntry>(256);
        private static readonly Dictionary<JobDef, bool> meleeAttackJobByDefCache = new Dictionary<JobDef, bool>(64);
        private static readonly Dictionary<Type, bool> protectPreyDriverTypeCache = new Dictionary<Type, bool>(16);
        private static readonly Dictionary<Type, bool> protectYoungDriverTypeCache = new Dictionary<Type, bool>(16);
        private static readonly Dictionary<Type, bool> predatorHuntDriverTypeCache = new Dictionary<Type, bool>(16);

        private static int meleeAttackPairCacheTick = int.MinValue;
        private static Game meleeAttackPairCacheGame;

        public static bool IsStandardFleeBlockedByExtensions(Pawn pawn)
        {
            if (pawn == null)
            {
                return true;
            }

            return NoFleeUtil.IsNoFlee(pawn)
                || ZoologyCacheUtility.HasFleeFromCarrierExtension(pawn.def);
        }

        public static bool CanUseForcedThreatFlee(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!pawn.IsAnimal || !pawn.Spawned || pawn.Dead || pawn.Destroyed || pawn.Downed || pawn.InMentalState)
            {
                return false;
            }

            if (pawn.GetLord() != null || ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
            {
                return false;
            }

            JobDef currentJobDef = pawn.CurJob?.def;
            return currentJobDef == null || !currentJobDef.neverFleeFromEnemies;
        }

        public static bool IsValidThreatForFlee(Pawn threat, Pawn prey)
        {
            return threat != null
                && prey != null
                && threat != prey
                && threat.Spawned
                && !threat.Dead
                && !threat.Destroyed
                && !threat.Downed
                && threat.Map != null
                && threat.Map == prey.Map;
        }

        public static bool IsPawnUnderMeleeAttack(Pawn pawn)
        {
            return TryGetMeleeAttackerOnPawn(pawn, out _);
        }

        public static bool TryGetMeleeAttackerOnPawn(Pawn pawn, out Pawn attacker)
        {
            attacker = null;
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Map == null)
            {
                return false;
            }

            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;
            for (int i = 0; i < GenAdj.AdjacentCellsAndInside.Length; i++)
            {
                IntVec3 cell = pos + GenAdj.AdjacentCellsAndInside[i];
                if (!cell.InBounds(map))
                {
                    continue;
                }

                var things = cell.GetThingList(map);
                for (int t = 0; t < things.Count; t++)
                {
                    Pawn candidate = things[t] as Pawn;
                    if (!IsThreatMeleeAttackingPawn(candidate, pawn))
                    {
                        continue;
                    }

                    attacker = candidate;
                    return true;
                }
            }

            Pawn rememberedThreat = pawn.mindState?.meleeThreat;
            if (rememberedThreat != null
                && pawn.mindState.MeleeThreatStillThreat
                && IsValidThreatForFlee(rememberedThreat, pawn)
                && rememberedThreat.Position.AdjacentTo8WayOrInside(pawn.Position)
                && IsThreatMeleeAttackingPawn(rememberedThreat, pawn))
            {
                attacker = rememberedThreat;
                return true;
            }

            return false;
        }

        public static bool IsThreatMeleeAttackingPawn(Pawn threat, Pawn prey)
        {
            if (threat == null
                || prey == null
                || ReferenceEquals(threat, prey))
            {
                return false;
            }

            Job curJob = threat.CurJob;
            Thing targetThing = curJob?.targetA.Thing;
            if (curJob == null || !ReferenceEquals(targetThing, prey))
            {
                return false;
            }

            JobDriver curDriver = threat.jobs?.curDriver;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (TryGetCachedMeleeAttackResult(threat, prey, curJob, curDriver, currentTick, out bool cachedResult))
            {
                return cachedResult;
            }

            if (!IsMeleeAttackingJob(curJob, curDriver))
            {
                StoreMeleeAttackResult(threat, prey, curJob, curDriver, false, currentTick);
                return false;
            }

            if (!threat.Spawned
                || !prey.Spawned
                || threat.Dead
                || threat.Destroyed
                || threat.Downed)
            {
                return false;
            }

            Map threatMap = threat.Map;
            if (threatMap == null
                || !ReferenceEquals(threatMap, prey.Map)
                || !threat.Position.AdjacentTo8WayOrInside(prey.Position))
            {
                return false;
            }

            StoreMeleeAttackResult(threat, prey, curJob, curDriver, true, currentTick);
            return true;
        }

        private static bool IsMeleeAttackingJob(Job curJob, JobDriver curDriver)
        {
            JobDef curJobDef = curJob?.def;
            if (curJobDef == JobDefOf.AttackMelee)
            {
                return true;
            }

            Type curDriverType = curDriver?.GetType();
            if (curDriver is JobDriver_PredatorHunt
                || curDriver is JobDriver_ProtectYoung
                || IsProtectPreyDriverType(curDriverType))
            {
                return true;
            }

            if (curJobDef == null)
            {
                return false;
            }

            if (meleeAttackJobByDefCache.TryGetValue(curJobDef, out bool cached))
            {
                return cached;
            }

            bool isMeleeAttackJob = false;
            string defName = curJobDef.defName;
            if (!string.IsNullOrEmpty(defName))
            {
                isMeleeAttackJob = defName.Equals(ProtectPreyState.ProtectPreyDefName, StringComparison.OrdinalIgnoreCase)
                    || defName.Equals(ProtectYoungUtility.ProtectYoungDefName, StringComparison.OrdinalIgnoreCase);
            }

            if (!isMeleeAttackJob)
            {
                Type driverClass = curJobDef.driverClass;
                isMeleeAttackJob = IsProtectYoungDriverType(driverClass)
                    || IsPredatorHuntDriverType(driverClass);
            }

            meleeAttackJobByDefCache[curJobDef] = isMeleeAttackJob;
            return isMeleeAttackJob;
        }

        private static bool IsProtectPreyDriverType(Type driverType)
        {
            if (driverType == null)
            {
                return false;
            }

            if (protectPreyDriverTypeCache.TryGetValue(driverType, out bool cached))
            {
                return cached;
            }

            string fullName = driverType.FullName;
            bool result = driverType == typeof(JobDriver_ProtectPrey)
                || driverType.Name == nameof(JobDriver_ProtectPrey)
                || (!string.IsNullOrEmpty(fullName) && fullName.EndsWith($".{nameof(JobDriver_ProtectPrey)}", StringComparison.Ordinal))
                || typeof(JobDriver_ProtectPrey).IsAssignableFrom(driverType);

            protectPreyDriverTypeCache[driverType] = result;
            return result;
        }

        private static bool IsProtectYoungDriverType(Type driverType)
        {
            if (driverType == null)
            {
                return false;
            }

            if (protectYoungDriverTypeCache.TryGetValue(driverType, out bool cached))
            {
                return cached;
            }

            bool result = driverType == typeof(JobDriver_ProtectYoung)
                || typeof(JobDriver_ProtectYoung).IsAssignableFrom(driverType);
            protectYoungDriverTypeCache[driverType] = result;
            return result;
        }

        private static bool IsPredatorHuntDriverType(Type driverType)
        {
            if (driverType == null)
            {
                return false;
            }

            if (predatorHuntDriverTypeCache.TryGetValue(driverType, out bool cached))
            {
                return cached;
            }

            bool result = driverType == typeof(JobDriver_PredatorHunt)
                || typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverType);
            predatorHuntDriverTypeCache[driverType] = result;
            return result;
        }

        private static bool TryGetCachedMeleeAttackResult(
            Pawn threat,
            Pawn prey,
            Job curJob,
            JobDriver curDriver,
            int currentTick,
            out bool result)
        {
            result = false;
            if (currentTick <= 0)
            {
                return false;
            }

            EnsureMeleeAttackPairCacheState(currentTick);
            long pairKey = PairKey(threat, prey);
            if (pairKey == 0L
                || !meleeAttackPairCacheByPairKey.TryGetValue(pairKey, out ThreatMeleeAttackPairCacheEntry cached))
            {
                return false;
            }

            if (!ReferenceEquals(cached.Threat, threat)
                || !ReferenceEquals(cached.Prey, prey)
                || !ReferenceEquals(cached.Job, curJob)
                || !ReferenceEquals(cached.Driver, curDriver))
            {
                return false;
            }

            result = cached.IsThreatMeleeAttacking;
            return true;
        }

        private static void StoreMeleeAttackResult(
            Pawn threat,
            Pawn prey,
            Job curJob,
            JobDriver curDriver,
            bool isThreatMeleeAttacking,
            int currentTick)
        {
            if (currentTick <= 0)
            {
                return;
            }

            EnsureMeleeAttackPairCacheState(currentTick);
            long pairKey = PairKey(threat, prey);
            if (pairKey == 0L)
            {
                return;
            }

            meleeAttackPairCacheByPairKey[pairKey] = new ThreatMeleeAttackPairCacheEntry(
                threat,
                prey,
                curJob,
                curDriver,
                isThreatMeleeAttacking);
        }

        private static void EnsureMeleeAttackPairCacheState(int currentTick)
        {
            Game currentGame = Current.Game;
            if (!ReferenceEquals(meleeAttackPairCacheGame, currentGame) || meleeAttackPairCacheTick != currentTick)
            {
                meleeAttackPairCacheGame = currentGame;
                meleeAttackPairCacheTick = currentTick;
                meleeAttackPairCacheByPairKey.Clear();
            }
        }

        private static long PairKey(Pawn threat, Pawn prey)
        {
            if (threat == null || prey == null)
            {
                return 0L;
            }

            uint threatId = (uint)threat.thingIDNumber;
            uint preyId = (uint)prey.thingIDNumber;
            return ((long)threatId << 32) | preyId;
        }
    }
}
