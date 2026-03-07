using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    
    [HarmonyPatch(typeof(Toils_Ingest))]
    [HarmonyPatch("ChewIngestible", new Type[] { typeof(Pawn), typeof(float), typeof(TargetIndex), typeof(TargetIndex) })]
    public static class Patch_ToilsIngest_ChewIngestible
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnablePredatorDefendCorpse;
        }

        
        
        
        public static void Postfix(Toil __result, Pawn chewer, float durationMultiplier, TargetIndex ingestibleInd, TargetIndex eatSurfaceInd)
        {
            try
            {
                if (__result == null) return;

                
                Action originalInit = __result.initAction;

                
                __result.initAction = () =>
                {
                    try
                    {
                        
                        Pawn actor = __result.actor as Pawn;
                        if (actor == null) actor = chewer; 
                        if (actor != null)
                        {
                            Job curJob = actor.CurJob;
                            if (curJob != null)
                            {
                                LocalTargetInfo targ = curJob.GetTarget(ingestibleInd);
                                if (targ.HasThing)
                                {
                                    Thing t = targ.Thing;
                                    if (t != null)
                                    {
                                        
                                        Corpse corp = t as Corpse;
                                        if (corp != null)
                                        {
                                            var comp = PredatorPreyPairGameComponent.Instance;
                                            if (comp != null)
                                            {
                                                comp.TryTriggerDefendFor(corp, actor);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Zoology: Patch_ToilsIngest_ChewIngestible initAction exception: {ex}");
                    }

                    
                    
                    try
                    {
                        originalInit?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Zoology: Patch_ToilsIngest_ChewIngestible: original initAction threw: {ex}");
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Patch_ToilsIngest_ChewIngestible Postfix exception: {ex}");
            }
        }
    }
}
