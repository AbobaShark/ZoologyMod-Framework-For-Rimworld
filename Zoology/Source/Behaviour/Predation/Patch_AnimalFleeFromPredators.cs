using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_AnimalFleeFromPredators
    {
        private static readonly ThingRequest PawnRequest = ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnablePreyFleeFromPredators;
        }

        const float SEARCH_RADIUS = 12f;
        const int FLEE_DISTANCE_DEFAULT = 12;
        const int FLEE_DISTANCE_TARGET = 16;
        const float MELEE_ADJACENT_SQ = 2f * 2f; 

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnablePreyFleeFromPredators)
                    return;

                
                if (pawn == null || pawn.Map == null) return;
                if (!pawn.RaceProps.Animal) return;

                Pawn threat = FindNearestPredatorHuntingLivePrey(pawn, SEARCH_RADIUS);
                if (threat == null) return;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                Job threatJob = threat.CurJob;
                bool threatAimingAtPawn = JobTargetsPawn(threatJob, pawn);
                int fleeDistance = threatAimingAtPawn ? FLEE_DISTANCE_TARGET : FLEE_DISTANCE_DEFAULT;
                
                bool bothPhotonozoaInTheirFaction = IsPhotonozoaPairInTheirFaction(threat, pawn);

                
                
                if (!bothPhotonozoaInTheirFaction)
                {
                    if (!FleeUtility.ShouldAnimalFleeDanger(pawn)) return;
                }
                else
                {
                    
                    
                    if (!pawn.IsAnimal) return;
                    if (pawn.InMentalState) return;
                    if (pawn.IsFighting()) return;
                    if (pawn.Downed) return;
                    if (pawn.Dead) return;
                    if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn)) return;
                    if (pawn.Faction == Faction.OfPlayer && pawn.Map != null && pawn.Map.IsPlayerHome)
                        return;
                    if (pawn.jobs?.curJob != null
                        && pawn.jobs.curJob.def == JobDefOf.Flee
                        && pawn.jobs.curJob.startTick == currentTick)
                        return;
                }

                try
                {
                    float distSq = (threat.Position - pawn.Position).LengthHorizontalSquared;
                    bool inMeleeProximity = distSq <= MELEE_ADJACENT_SQ;

                    if (threatAimingAtPawn)
                    {
                        HandlePursuitAllowanceIfNeeded(threat, pawn);
                    }

                    bool threatIsDoingMelee = threatJob != null
                                            && threatJob.def == JobDefOf.AttackMelee
                                            && threatAimingAtPawn;

                    
                    if (threatAimingAtPawn && (threatIsDoingMelee || inMeleeProximity))
                    {
                        
                        __result = null;
                        return;
                    }
                }
                catch (Exception exMelee)
                {
                    Log.Error($"[ZoologyMod] Error while trying to handle melee-proximity logic for pawn {pawn?.LabelShort}: {exMelee}");
                    
                }

                
                __result = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Patch_AnimalFleeFromPredators failed: {e}");
            }
        }

        
        static bool JobTargetsPawn(Job job, Pawn pawn)
        {
            return job != null && job.targetA.HasThing && job.GetTarget(TargetIndex.A).Thing == pawn;
        }

        static Pawn FindNearestPredatorHuntingLivePrey(Pawn pawn, float radius)
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
                    (Thing t) =>
                    {
                        if (t is not Pawn p) return false;
                        if (!p.RaceProps.predator || p == pawn || p.Downed) return false;

                        Job curJob = p.CurJob;
                        if (curJob == null || !curJob.targetA.HasThing) return false;
                        JobDef curJobDef = curJob.def;
                        if (curJobDef == null) return false;

                        Thing target = curJob.GetTarget(TargetIndex.A).Thing;
                        if (target is not Pawn preyPawn || preyPawn.Dead) return false;

                        bool isMeleeJob = curJobDef == JobDefOf.AttackMelee;
                        bool isThreatJob = isMeleeJob
                            || typeof(JobDriver_PredatorHunt).IsAssignableFrom(curJobDef.driverClass)
                            || ProtectPreyState.IsProtectPreyJob(curJob, p.jobs?.curDriver);
                        if (!isThreatJob)
                            return false;

                        if (ReferenceEquals(preyPawn, pawn))
                            return true;

                        
                        
                        bool acceptablePrey = true;
                        try { acceptablePrey = FoodUtility.IsAcceptablePreyFor(p, pawn); } catch { acceptablePrey = true; }
                        if (!acceptablePrey) return false;

                        
                        return true;
                    }
                ) as Pawn;
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestPredatorHuntingLivePrey failed: {ex}");
                return null;
            }
        }

        static void HandlePursuitAllowanceIfNeeded(Pawn predator, Pawn prey)
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
                if (preySpeed <= predatorSpeed) return;

                comp.AllowPursuit(predator, prey, 2);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] HandlePursuitAllowanceIfNeeded error: {ex}");
            }
        }

        static void TryEnsurePursuitComponentRegistered()
        {
            try
            {
                
                if (ZoologyPursuitGameComponent.Instance != null) return;

                Game game = Current.Game;
                if (game?.components == null) return;

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

        
        
        static bool IsPhotonozoaPairInTheirFaction(Pawn a, Pawn b)
        {
            try
            {
                if (a == null || b == null) return false;

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
