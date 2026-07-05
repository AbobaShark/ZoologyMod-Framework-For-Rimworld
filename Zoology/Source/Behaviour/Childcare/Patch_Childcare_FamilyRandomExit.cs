using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(ThinkNode_ChancePerHour), nameof(ThinkNode_ChancePerHour.TryIssueJobPackage))]
    internal static class Patch_Childcare_FamilyRandomExit
    {
        private const float RandomWildExitMtbDays = 60f;

        private static readonly FieldInfo MtbDaysField = AccessTools.Field(typeof(ThinkNode_ChancePerHour_Constant), "mtbDays");

        public static bool Prepare()
        {
            return ChildcareUtility.IsChildcareEnabled && MtbDaysField != null;
        }

        public static void Postfix(ThinkNode_ChancePerHour __instance, Pawn pawn, ref ThinkResult __result)
        {
            try
            {
                if (!ChildcareUtility.IsChildcareEnabled
                    || !__result.IsValid
                    || !IsVanillaRandomWildExitNode(__instance, __result))
                {
                    return;
                }

                ChildcareFamilyExitMapUtility.TrySendFamilyWithRandomLeavingChild(pawn, __result.Job);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_Childcare_FamilyRandomExit Postfix exception: {ex}");
            }
        }

        private static bool IsVanillaRandomWildExitNode(ThinkNode_ChancePerHour node, ThinkResult result)
        {
            if (!(node is ThinkNode_ChancePerHour_Constant)
                || !(result.SourceNode is JobGiver_ExitMapRandom))
            {
                return false;
            }

            object value = MtbDaysField.GetValue(node);
            return value is float mtbDays && Math.Abs(mtbDays - RandomWildExitMtbDays) < 0.001f;
        }
    }

    internal static class ChildcareFamilyExitMapUtility
    {
        private const int FamilyExitCooldownTicks = 2500;

        private static readonly Dictionary<int, int> lastFamilyExitTickByMotherId = new Dictionary<int, int>(64);

        public static void TrySendFamilyWithRandomLeavingChild(Pawn child, Job childExitJob)
        {
            if (!CanBeRandomLeavingChild(child, childExitJob)
                || !TryGetChildcareMotherForChild(child, out Pawn mother)
                || !CanJoinFamilyExit(mother, child.Map))
            {
                return;
            }

            TryStartFamilyExit(mother, child.Map, child);
        }

        public static bool TryStartForcedWildExit(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (!CanJoinFamilyExit(pawn, map)
                || IsProtectedByGuardingEggMother(pawn))
            {
                return false;
            }

            if (ChildcareUtility.IsChildcareEnabled)
            {
                if (ChildcareUtility.IsAnimalChild(pawn)
                    && TryGetChildcareMotherForChild(pawn, out Pawn childMother))
                {
                    if (!CanJoinFamilyExit(childMother, map) || IsGuardingEggClutch(childMother))
                    {
                        return false;
                    }

                    return TryStartFamilyExit(childMother, map, null);
                }

                if (ChildcareUtility.HasChildcareExtension(pawn)
                    && HasAnyEligibleChildOfMother(pawn, map))
                {
                    return TryStartFamilyExit(pawn, map, null);
                }
            }

            return TryStartExitJob(pawn);
        }

        public static bool CanJoinFamilyExit(Pawn pawn, Map map)
        {
            return pawn != null
                && map != null
                && pawn.Spawned
                && pawn.Map == map
                && WildAnimalEcosystemUtility.IsWildAnimal(pawn)
                && !pawn.Dead
                && !pawn.Destroyed
                && !pawn.Downed
                && !pawn.InMentalState
                && pawn.jobs != null
                && !IsLeavingMap(pawn.CurJob);
        }

        public static bool IsProtectedByGuardingEggMother(Pawn pawn)
        {
            if (IsGuardingEggClutch(pawn))
            {
                return true;
            }

            return ChildcareUtility.IsChildcareEnabled
                && ChildcareUtility.IsAnimalChild(pawn)
                && TryGetChildcareMotherForChild(pawn, out Pawn mother)
                && IsGuardingEggClutch(mother);
        }

        public static bool IsGuardingEggClutch(Pawn pawn)
        {
            if (!ChildcareDefenseUtility.IsEggProtectionEnabled
                || pawn == null
                || pawn.gender != Gender.Female
                || !WildAnimalEcosystemUtility.IsWildAnimal(pawn)
                || !pawn.Spawned
                || pawn.Map == null
                || pawn.TryGetComp<CompEggLayer>() == null
                || !ChildcareUtility.HasChildcareExtension(pawn))
            {
                return false;
            }

            Thing egg = EggClutchDefenseGameComponent.Instance?.TryGetPairedEggForProtector(pawn);
            return egg != null
                && !egg.Destroyed
                && egg.SpawnedOrAnyParentSpawned
                && egg.MapHeld == pawn.Map;
        }

        private static bool TryStartFamilyExit(Pawn mother, Map map, Pawn alreadyLeavingChild)
        {
            if (!CanJoinFamilyExit(mother, map)
                || !ChildcareUtility.HasChildcareExtension(mother))
            {
                return false;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            int motherId = mother.thingIDNumber;
            if (lastFamilyExitTickByMotherId.TryGetValue(motherId, out int lastTick)
                && now - lastTick < FamilyExitCooldownTicks)
            {
                return false;
            }

            bool startedAny = TryStartExitJob(mother);

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn child = pawns[i];
                if (child == null
                    || ReferenceEquals(child, mother)
                    || !ChildcareUtility.IsAnimalChild(child))
                {
                    continue;
                }

                if (!ChildcareUtility.TryGetBiologicalMother(child, out Pawn childMother)
                    || !ReferenceEquals(childMother, mother))
                {
                    continue;
                }

                if (ReferenceEquals(child, alreadyLeavingChild))
                {
                    startedAny = true;
                    continue;
                }

                if (CanJoinFamilyExit(child, map))
                {
                    startedAny |= TryStartExitJob(child);
                }
            }

            if (startedAny)
            {
                lastFamilyExitTickByMotherId[motherId] = now;
            }

            return startedAny;
        }

        private static bool CanBeRandomLeavingChild(Pawn pawn, Job exitJob)
        {
            return exitJob != null
                && IsExitMapJob(exitJob)
                && CanJoinFamilyExit(pawn, pawn?.Map)
                && ChildcareUtility.IsAnimalChild(pawn);
        }

        private static bool TryGetChildcareMotherForChild(Pawn child, out Pawn mother)
        {
            mother = null;
            if (!ChildcareUtility.IsChildcareEnabled
                || !ChildcareUtility.IsAnimalChild(child)
                || !ChildcareUtility.TryGetBiologicalMother(child, out mother))
            {
                return false;
            }

            return mother != null && ChildcareUtility.HasChildcareExtension(mother);
        }

        private static bool HasAnyEligibleChildOfMother(Pawn mother, Map map)
        {
            if (mother == null
                || map?.mapPawns == null
                || !ChildcareUtility.HasChildcareExtension(mother))
            {
                return false;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn child = pawns[i];
                if (ReferenceEquals(child, mother)
                    || !CanJoinFamilyExit(child, map)
                    || !ChildcareUtility.IsAnimalChild(child)
                    || !ChildcareUtility.TryGetBiologicalMother(child, out Pawn childMother)
                    || !ReferenceEquals(childMother, mother))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsExitMapJob(Job job)
        {
            return job != null
                && (job.exitMapOnArrival || job.def == JobDefOf.ExitMapFlying);
        }

        private static bool IsLeavingMap(Job job)
        {
            return job != null
                && (job.exitMapOnArrival || job.def == JobDefOf.ExitMapFlying || job.def == JobDefOf.EnterPortal);
        }

        public static bool TryStartExitJob(Pawn pawn)
        {
            Job job = MakeFamilyExitJob(pawn);
            if (job == null)
            {
                return false;
            }

            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards: false, cancelBusyStances: true, null, JobTag.Misc);
            return IsLeavingMap(pawn.CurJob);
        }

        private static Job MakeFamilyExitJob(Pawn pawn)
        {
            if (pawn?.Map == null || !pawn.Map.CanEverExit)
            {
                return null;
            }

            if (CanLeaveMapFlying(pawn))
            {
                return JobMaker.MakeJob(JobDefOf.ExitMapFlying);
            }

            if (!RCellFinder.TryFindRandomExitSpot(pawn, out IntVec3 spot, TraverseMode.ByPawn))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.Goto, spot);
            job.exitMapOnArrival = true;
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 999999;
            return job;
        }

        private static bool CanLeaveMapFlying(Pawn pawn)
        {
            return pawn.RaceProps.canLeaveMapFlying
                && !pawn.Position.Roofed(pawn.Map)
                && pawn.Faction != Faction.OfPlayer
                && pawn.flight.CanEverFly
                && !pawn.IsQuestLodger();
        }
    }
}
