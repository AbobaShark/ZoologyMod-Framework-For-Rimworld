

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_AnimalFleeFromPredators
    {
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
                if (!ModConstants.Settings.EnablePreyFleeFromPredators)
                    return;

                
                if (pawn == null || pawn.Map == null) return;
                if (!pawn.RaceProps.Animal) return;

                int fleeDistance = FLEE_DISTANCE_DEFAULT;

                Pawn threat = FindNearestPredatorHuntingLivePrey(pawn, SEARCH_RADIUS);

                if (threat != null)
                {
                    try
                    {
                        var threatJob = threat.CurJob;
                        if (JobTargetsPawn(threatJob, pawn))
                        {
                            fleeDistance = FLEE_DISTANCE_TARGET;
                        }
                    }
                    catch
                    {
                        fleeDistance = FLEE_DISTANCE_DEFAULT;
                    }
                }

                if (threat == null) return;
                
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
                        && pawn.jobs.curJob.startTick == Find.TickManager.TicksGame)
                        return;
                }

                try
                {
                    var threatJob = threat.CurJob;
                    bool threatAimingAtPawn = JobTargetsPawn(threatJob, pawn);

                    float distSq = (threat.Position - pawn.Position).LengthHorizontalSquared;
                    bool inMeleeProximity = distSq <= MELEE_ADJACENT_SQ;

                    if (threatAimingAtPawn)
                    {
                        HandlePursuitAllowanceIfNeeded(threat, pawn);
                    }

                    bool threatIsDoingMelee = threatJob != null
                                            && threatJob.def == JobDefOf.AttackMelee
                                            && JobTargetsPawn(threatJob, pawn);

                    
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
                return GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.Pawn),
                    PathEndMode.OnCell,
                    TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn),
                    radius,
                    (Thing t) =>
                    {
                        if (t is not Pawn p) return false;
                        if (!p.RaceProps.predator || p == pawn || p.Downed) return false;

                        Job curJob = p.CurJob;
                        if (curJob == null || !curJob.targetA.HasThing) return false;

                        Thing target = curJob.GetTarget(TargetIndex.A).Thing;
                        if (target is not Pawn preyPawn || preyPawn.Dead) return false;

                        
                        bool isHuntDriver = false;
                        bool isMeleeJob = false;
                        try
                        {
                            isMeleeJob = curJob.def == JobDefOf.AttackMelee;
                            var driverClass = curJob.def.driverClass;

                            if (driverClass != null)
                            {
                                if (typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass))
                                    isHuntDriver = true;

                                
                                try
                                {
                                    if (!isHuntDriver && typeof(JobDriver_ProtectPrey).IsAssignableFrom(driverClass))
                                        isHuntDriver = true;
                                }
                                catch { }
                            }

                            
                            if (!isHuntDriver)
                            {
                                var defName = curJob.def?.defName;
                                if (!string.IsNullOrEmpty(defName) &&
                                    string.Equals(defName, "Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                                {
                                    isHuntDriver = true;
                                }
                            }
                        }
                        catch
                        {
                            isHuntDriver = false;
                            isMeleeJob = false;
                        }

                        
                        if (!isHuntDriver && !isMeleeJob)
                            return false;

                        
                        
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
                float predatorSpeed = predator.GetStatValue(StatDefOf.MoveSpeed, true);
                float preySpeed = prey.GetStatValue(StatDefOf.MoveSpeed, true);

                if (preySpeed <= predatorSpeed) return;

                
                TryEnsurePursuitComponentRegistered();

                var comp = ZoologyPursuitGameComponent.Instance;
                if (comp == null)
                {
                    Log.Message("[Zoology] WARNING: ZoologyPursuitGameComponent.Instance is null - cannot AllowPursuit.");
                    return;
                }

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

                if (Current.Game?.components == null) return;

                bool exists = Current.Game.components.Exists(c => c.GetType() == typeof(ZoologyPursuitGameComponent));
                if (!exists)
                {
                    Current.Game.components.Add(new ZoologyPursuitGameComponent());
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

                bool aIsPhot = false;
                bool bIsPhot = false;
                try
                {
                    aIsPhot = a.def.modExtensions != null && a.def.modExtensions.Any(me =>
                        me != null && (me.GetType().Name == "PhotonozoaProperties" || me.GetType().FullName.EndsWith(".PhotonozoaProperties")));
                    bIsPhot = b.def.modExtensions != null && b.def.modExtensions.Any(me =>
                        me != null && (me.GetType().Name == "PhotonozoaProperties" || me.GetType().FullName.EndsWith(".PhotonozoaProperties")));
                }
                catch { aIsPhot = false; bIsPhot = false; }

                if (!aIsPhot || !bIsPhot) return false;

                var photFactionDef = DefDatabase<FactionDef>.GetNamedSilentFail("Photonozoa");
                if (photFactionDef == null) return false;

                if (a.Faction == null || b.Faction == null) return false;
                if (a.Faction.def != photFactionDef || b.Faction.def != photFactionDef) return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] IsPhotonozoaPairInTheirFaction error: {ex}");
                return false;
            }
        }
    }
}
