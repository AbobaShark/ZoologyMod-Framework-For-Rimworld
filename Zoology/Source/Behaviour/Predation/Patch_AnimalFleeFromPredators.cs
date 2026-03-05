// Patch_AnimalFleeFromPredators.cs

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
        const float SEARCH_RADIUS = 12f;
        const int FLEE_DISTANCE_DEFAULT = 12;
        const int FLEE_DISTANCE_TARGET = 16;
        const float MELEE_ADJACENT_SQ = 2f * 2f; // adjacency approximation

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                if (!ModConstants.Settings.EnablePreyFleeFromPredators)
                    return;

                // Убрали ранний return при __result != null — нам нужно иметь возможность отменять уже назначенный flee-job
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

                // Если пара НЕ фотонозой — применяем стандартную проверку ShouldAnimalFlee.
                // Если пара — фотонозой, то пропускаем ShouldAnimalFleeDanger, но не даём уезжать упавшим (downed).
                if (!bothPhotonozoaInTheirFaction)
                {
                    if (!FleeUtility.ShouldAnimalFleeDanger(pawn)) return;
                }
                else
                {
                    // Для фотонозой: ручная проверка, эквивалентная оригинальному ShouldAnimalFleeDanger,
                    // но без вызова самого метода (он патчен).
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

                    // Если хищник целится в жертву и уже в ближнем бою (или выполняет melee-атаку),
                    if (threatAimingAtPawn && (threatIsDoingMelee || inMeleeProximity))
                    {
                        // Отменяем побег и даём ванильной логике разбираться
                        __result = null;
                        return;
                    }
                }
                catch (Exception exMelee)
                {
                    Log.Error($"[ZoologyMod] Error while trying to handle melee-proximity logic for pawn {pawn?.LabelShort}: {exMelee}");
                    // fallthrough -> give flee job
                }

                // В остальных случаях даём нормальную задачу убегания (может переопределять любой предыдущий __result)
                __result = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Patch_AnimalFleeFromPredators failed: {e}");
            }
        }

        // helper: проверяет, нацелен ли job.targetA на данного pawn
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

                        // Определяем тип job: hunt/protect driver или melee
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

                                // учитываем наш защитный драйвер как hunt-подобный
                                try
                                {
                                    if (!isHuntDriver && typeof(JobDriver_ProtectPrey).IsAssignableFrom(driverClass))
                                        isHuntDriver = true;
                                }
                                catch { }
                            }

                            // fallback по defName
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

                        // Если это ни hunt/protect, ни melee — отбрасываем
                        if (!isHuntDriver && !isMeleeJob)
                            return false;

                        // Для всех случаев (hunt/protect или melee) требуем, чтобы predator считал pawn приемлемой добычей.
                        // Это предотвращает ложные срабатывания, когда хищник бьёт не по "пищевой" цели.
                        bool acceptablePrey = true;
                        try { acceptablePrey = FoodUtility.IsAcceptablePreyFor(p, pawn); } catch { acceptablePrey = true; }
                        if (!acceptablePrey) return false;

                        // Если дошли до сюда — считаем predator релевантной угрозой
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

                // Если комп не зарегистрирован — пробуем зарегистрировать динамически
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
                // быстрый выход, если уже корректный экземпляр есть
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

        // Безопасный проверочный метод: true только если и predator, и prey имеют Photonozoa ModExtension (проверка по имени типа)
        // И принадлежат фракции с defName == "Photonozoa" (проверка через GetNamedSilentFail)
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