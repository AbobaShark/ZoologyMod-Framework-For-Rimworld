using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZoologyMod;

namespace ZoologyMod.HarmonyPatches
{
    [HarmonyPatch]
    public static class Patch_ScavengeringCore
    {
        private struct ScavengerCorpseState
        {
            public Corpse Corpse;
            public Pawn Eater;
            public ModExtension_IsScavenger Scavenger;
            public bool HasScavengerEater;
            public bool CannotChewBlocks;
            public bool IsBugged;
            public bool DefAllowsNutrition;
            public bool IsFlesh;
            public RotStage RotStage;
        }

        private static readonly Dictionary<int, ScavengerCorpseState> scavengerCorpseStateById =
            new Dictionary<int, ScavengerCorpseState>(64);
        private static int scavengerCorpseStateTick = int.MinValue;
        private static readonly Dictionary<int, int> adjacentFallbackScansByMapId = new Dictionary<int, int>(4);
        private static int adjacentFallbackScansTick = int.MinValue;
        private const int MaxAdjacentFallbackScansPerMapPerTick = 1;

        private static bool TryConsumeAdjacentFallbackBudget(Map map)
        {
            if (map == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (adjacentFallbackScansTick != currentTick)
            {
                adjacentFallbackScansTick = currentTick;
                adjacentFallbackScansByMapId.Clear();
            }

            int mapId = map.uniqueID;
            adjacentFallbackScansByMapId.TryGetValue(mapId, out int used);
            if (used >= MaxAdjacentFallbackScansPerMapPerTick)
            {
                return false;
            }

            adjacentFallbackScansByMapId[mapId] = used + 1;
            return true;
        }

        private static bool TryGetScavengerEaterByAdjacentScan(Corpse corpse, out Pawn eater, out ModExtension_IsScavenger scav)
        {
            eater = null;
            scav = null;

            if (corpse == null)
            {
                return false;
            }

            Map map = corpse.Map;
            if (map == null || !corpse.Spawned)
            {
                return false;
            }

            IntVec3 pos = corpse.Position;
            for (int i = 0; i < GenAdj.AdjacentCellsAndInside.Length; i++)
            {
                IntVec3 c = pos + GenAdj.AdjacentCellsAndInside[i];
                if (!c.InBounds(map))
                {
                    continue;
                }

                List<Thing> list = map.thingGrid.ThingsListAtFast(c);
                for (int j = 0; j < list.Count; j++)
                {
                    if (!(list[j] is Pawn pawn) || pawn.Dead || pawn.Destroyed)
                    {
                        continue;
                    }

                    Job curJob = pawn.CurJob;
                    if (curJob == null || curJob.def != JobDefOf.Ingest)
                    {
                        continue;
                    }

                    if (!ReferenceEquals(curJob.targetA.Thing, corpse)
                        && !ReferenceEquals(curJob.targetB.Thing, corpse)
                        && !ReferenceEquals(curJob.targetC.Thing, corpse))
                    {
                        continue;
                    }

                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out scav) || scav == null)
                    {
                        continue;
                    }

                    eater = pawn;
                    ScavengerEatingContext.SetEating(pawn, corpse);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetScavengerEater(Corpse corpse, out Pawn eater, out ModExtension_IsScavenger scav)
        {
            eater = null;
            scav = null;

            if (corpse == null)
            {
                return false;
            }

            eater = ScavengerEatingContext.GetEatingPawnForCorpse(corpse);
            if (eater != null
                && !eater.Dead
                && !eater.Destroyed
                && DefModExtensionCache<ModExtension_IsScavenger>.TryGet(eater, out scav)
                && scav != null)
            {
                return true;
            }

            try
            {
                eater = null;
                scav = null;

                if (!corpse.Spawned || corpse.Map == null)
                {
                    return false;
                }

                if (!ScavengerEatingContext.HasAnyActiveEatingContext())
                {
                    return false;
                }

                if (!TryConsumeAdjacentFallbackBudget(corpse.Map))
                {
                    return false;
                }

                return TryGetScavengerEaterByAdjacentScan(corpse, out eater, out scav);
            }
            catch
            {
                eater = null;
                scav = null;
                return false;
            }
        }

        private static bool TryGetScavengerCorpseState(Corpse corpse, out ScavengerCorpseState state)
        {
            state = default;
            if (corpse == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (scavengerCorpseStateTick != currentTick)
            {
                scavengerCorpseStateTick = currentTick;
                scavengerCorpseStateById.Clear();
            }

            int corpseId = corpse.thingIDNumber;
            if (scavengerCorpseStateById.TryGetValue(corpseId, out state)
                && ReferenceEquals(state.Corpse, corpse))
            {
                return state.HasScavengerEater;
            }

            state = BuildScavengerCorpseState(corpse);
            if (state.HasScavengerEater)
            {
                scavengerCorpseStateById[corpseId] = state;
            }
            else
            {
                scavengerCorpseStateById.Remove(corpseId);
            }
            return state.HasScavengerEater;
        }

        private static ScavengerCorpseState BuildScavengerCorpseState(Corpse corpse)
        {
            var state = new ScavengerCorpseState
            {
                Corpse = corpse
            };

            if (!TryGetScavengerEater(corpse, out Pawn eater, out ModExtension_IsScavenger scav))
            {
                return state;
            }

            state.Eater = eater;
            state.Scavenger = scav;
            state.HasScavengerEater = true;
            state.IsBugged = corpse.Bugged;
            state.DefAllowsNutrition = corpse.def != null && corpse.def.IsNutritionGivingIngestible;

            Pawn innerPawn = corpse.InnerPawn;
            state.IsFlesh = innerPawn != null && innerPawn.RaceProps != null && innerPawn.RaceProps.IsFlesh;

            bool cannotChew = CannotChewUtility.HasCannotChew(eater);
            state.CannotChewBlocks = cannotChew && CannotChewUtility.IsCorpseTooLarge(eater, corpse);

            CompRottable rotComp = corpse.GetComp<CompRottable>();
            state.RotStage = rotComp != null ? rotComp.Stage : corpse.GetRotStage();
            return state;
        }
        
        
        [HarmonyPatch(typeof(Corpse))]
        [HarmonyPatch("IngestibleNow", MethodType.Getter)]
        private static class Inner_Corpse_IngestibleNow
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Corpse __instance, ref bool __result)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return true;

                    if (!TryGetScavengerCorpseState(__instance, out ScavengerCorpseState state))
                    {
                        return true;
                    }

                    if (state.CannotChewBlocks)
                    {
                        __result = false;
                        return false;
                    }

                    if (state.IsBugged)
                    {
                        __result = false;
                        return false;
                    }

                    if (!state.DefAllowsNutrition || !state.IsFlesh)
                    {
                        __result = false;
                        return false;
                    }

                    if (state.RotStage == RotStage.Dessicated && !state.Scavenger.allowVeryRotten)
                    {
                        __result = false;
                        return false;
                    }

                    __result = true;
                    return false;
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Corpse.IngestibleNow prefix: " + e);
                    return true;
                }
            }
        }

        
        static MethodBase TargetMethod_TryGetIsFreshFactor()
        {
            try
            {
                return AccessTools.Method(
                    typeof(StatPart_IsCorpseFresh),
                    "TryGetIsFreshFactor",
                    new Type[] { typeof(StatRequest), typeof(float).MakeByRefType() }
                );
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Failed resolving TryGetIsFreshFactor: " + e);
                return null;
            }
        }

        static bool Prefix_TryGetIsFreshFactor(StatRequest req, ref float factor)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableScavengering) return true;

                if (!req.HasThing) return true;
                var corpse = req.Thing as Corpse;
                if (corpse == null) return true;

                if (!TryGetScavengerCorpseState(corpse, out ScavengerCorpseState state)) return true;

                if (state.RotStage == RotStage.Fresh || state.RotStage == RotStage.Rotting)
                {
                    factor = 1f;
                    return false;
                }

                if (state.RotStage == RotStage.Dessicated)
                {
                    factor = state.Scavenger.allowVeryRotten ? 0.1f : 0f;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in TryGetIsFreshFactor prefix: " + e);
                return true;
            }
        }

        [HarmonyPatch]
        private static class Inner_TryGetIsFreshFactor
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static MethodBase TargetMethod() => TargetMethod_TryGetIsFreshFactor();
            static bool Prefix(StatRequest req, ref float factor) => Prefix_TryGetIsFreshFactor(req, ref factor);
        }

        
        [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetBodyPartNutrition), new[] { typeof(Corpse), typeof(BodyPartRecord) })]
        private static class Inner_FoodUtility_GetBodyPartNutrition
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Corpse corpse, BodyPartRecord part, ref float __result)
            {
                try
                {
                    if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnableScavengering) return true;

                    if (corpse == null || part == null) return true;
                    if (!TryGetScavengerCorpseState(corpse, out ScavengerCorpseState state)) return true;

                    if (state.RotStage == RotStage.Dessicated && !state.Scavenger.allowVeryRotten)
                    {
                        return true;
                    }

                    Pawn innerPawn = corpse.InnerPawn;
                    if (innerPawn == null) return true;

                    float nutritionRaw = corpse.GetStatValue(StatDefOf.Nutrition, false, -1);

                    float adjusted;
                    if (state.RotStage == RotStage.Dessicated)
                        adjusted = nutritionRaw * 0.1f;
                    else
                        adjusted = nutritionRaw;

                    __result = FoodUtility.GetBodyPartNutrition(adjusted, innerPawn, part);
                    return false;
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in GetBodyPartNutrition prefix: " + e);
                    return true;
                }
            }
        }

        
        [HarmonyPatch(typeof(Toils_Ingest), "FinalizeIngest")]
        private static class Inner_ToilsIngest_FinalizeIngest
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(ref Toil __result, Pawn ingester, TargetIndex ingestibleInd)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__result == null) return;
                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(ingester, out ModExtension_IsScavenger scav)) return; 

                    Toil toil = __result;
                    Action oldInit = toil.initAction;
                    toil.initAction = () =>
                    {
                        try
                        {
                            
                            try
                            {
                                Thing target = null;
                                Pawn actor = toil.actor;
                                Job actorJob = actor?.CurJob;
                                target = TryGetCorpseTarget(actorJob, ingestibleInd);

                                if (target == null && ingester != null)
                                {
                                    Job ingesterJob = ingester.CurJob;
                                    target = TryGetCorpseTarget(ingesterJob, ingestibleInd);
                                }

                                ScavengerEatingContext.SetEating(ingester, target);
                                if (!ReferenceEquals(actor, ingester))
                                {
                                    ScavengerEatingContext.SetHandFeeding(ingester, target);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error("[Zoology] Exception in FinalizeIngest init wrapper (SetEating): " + ex);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception in FinalizeIngest init wrapper (outer): " + ex);
                        }

                        try { oldInit?.Invoke(); }
                        catch (Exception ex) { Log.Error("[Zoology] Exception in FinalizeIngest oldInit: " + ex); }
                    };

                    toil.AddFinishAction(() =>
                    {
                        try
                        {
                            ScavengerEatingContext.Clear(ingester);
                            ScavengerEatingContext.ClearHandFeeding(ingester);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception clearing ScavengerEatingContext in finalize finish: " + ex);
                        }
                    });
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error wrapping FinalizeIngest: " + e);
                }
            }
        }

        
        static MethodBase TargetMethod_IngestedCalculateAmounts()
        {
            try
            {
                return AccessTools.Method(
                    typeof(Corpse),
                    "IngestedCalculateAmounts",
                    new Type[] {
                        typeof(Pawn),
                        typeof(float),
                        typeof(int).MakeByRefType(),
                        typeof(float).MakeByRefType()
                    }
                );
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Failed resolving Corpse.IngestedCalculateAmounts: " + e);
                return null;
            }
        }

        static void Postfix_IngestedCalculateAmounts(Pawn ingester)
        {
            try
            {
                
                ScavengerEatingContext.Clear(ingester);
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in Postfix(IngestedCalculateAmounts): " + e);
            }
        }

        [HarmonyPatch]
        private static class Inner_IngestedCalculateAmounts
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static MethodBase TargetMethod() => TargetMethod_IngestedCalculateAmounts();
            static void Postfix(Pawn ingester) => Postfix_IngestedCalculateAmounts(ingester);
        }

        
        [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.ChewIngestible))]
        private static class Inner_ToilsIngest_ChewIngestible
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(ref Toil __result, Pawn chewer, float durationMultiplier, TargetIndex ingestibleInd, TargetIndex eatSurfaceInd)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__result == null || chewer == null) return;

                    Toil toil = __result;
                    Action oldInit = toil.initAction;
                    toil.initAction = () =>
                    {
                        try
                        {
                            Thing target = null;
                            Pawn actor = toil.actor;
                            Job actorJob = actor?.CurJob;
                            target = TryGetCorpseTarget(actorJob, ingestibleInd);

                            if (target == null && chewer != null)
                            {
                                Job chewerJob = chewer.CurJob;
                                target = TryGetCorpseTarget(chewerJob, ingestibleInd);
                            }

                            ScavengerEatingContext.SetEating(chewer, target);
                            if (!ReferenceEquals(actor, chewer))
                            {
                                ScavengerEatingContext.SetHandFeeding(chewer, target);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception in ChewIngestible init wrapper (SetEating): " + ex);
                        }

                        try { oldInit?.Invoke(); }
                        catch (Exception ex) { Log.Error("[Zoology] Exception in ChewIngestible oldInit: " + ex); }
                    };

                    toil.AddFinishAction(() =>
                    {
                        try
                        {
                            ScavengerEatingContext.Clear(chewer);
                            ScavengerEatingContext.ClearHandFeeding(chewer);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception clearing ScavengerEatingContext in chew finish: " + ex);
                        }
                    });
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error wrapping ChewIngestible: " + e);
                }
            }
        }

        
        [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested), new[] { typeof(Pawn), typeof(float) })]
        private static class Inner_Thing_Ingested
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Prefix(Thing __instance, Pawn ingester, float nutritionWanted)
            {
                try
                {
                    if (ingester == null) return;
                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(ingester, out ModExtension_IsScavenger scav)) return;

                    ScavengerEatingContext.SetForceIngestible(ingester, __instance);
                    ScavengerEatingContext.SetEating(ingester, __instance);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Thing.Ingested prefix: " + e);
                }
            }

            static void Postfix(Thing __instance, Pawn ingester, float nutritionWanted)
            {
                try
                {
                    ScavengerEatingContext.Clear(ingester);
                    ScavengerEatingContext.ClearForceIngestible(ingester);
                    ScavengerEatingContext.ClearHandFeeding(ingester);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Thing.Ingested postfix: " + e);
                }
            }
        }

        private static Thing TryGetCorpseTarget(Job job, TargetIndex ingestibleInd)
        {
            if (job == null) return null;

            Thing target = null;
            try
            {
                target = job.GetTarget(ingestibleInd).Thing;
            }
            catch
            {
                target = null;
            }

            if (target is Corpse) return target;

            if (job.targetA.Thing is Corpse) return job.targetA.Thing;
            if (job.targetB.Thing is Corpse) return job.targetB.Thing;
            if (job.targetC.Thing is Corpse) return job.targetC.Thing;

            return target;
        }
    }
}
