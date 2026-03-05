

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    
    
    
    public class ModExtension_FleeFromCarrier : DefModExtension
    {
        
        public float fleeRadius = 18f;

        
        
        public float fleeBodySizeLimit = 0f;

        
        public int? fleeDistance = 24;
    }

    
    
    
    public class CompProperties_FleeFromCarrier : CompProperties
    {
        public float fleeRadius = 18f;
        public float fleeBodySizeLimit = 0f;
        public int? fleeDistance = 24;

        public CompProperties_FleeFromCarrier()
        {
            this.compClass = typeof(CompFleeFromCarrier);
        }
    }

    public class CompFleeFromCarrier : ThingComp
    {
        public CompProperties_FleeFromCarrier PropsFlee => (CompProperties_FleeFromCarrier)this.props;

        
        public bool enabled = true;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            
        }
    }

    
    
    
    public static class FleeFromCarrierUtil
    {
        
        public static bool IsCarrier(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.GetComp<CompFleeFromCarrier>() != null) return true;
            if (pawn.def?.GetModExtension<ModExtension_FleeFromCarrier>() != null) return true;
            return false;
        }

        public static float GetFleeRadius(Pawn carrier)
        {
            if (carrier == null) return 0f;
            var comp = carrier.GetComp<CompFleeFromCarrier>();
            if (comp != null && comp.enabled && comp.PropsFlee != null) return comp.PropsFlee.fleeRadius;
            var ext = carrier.def?.GetModExtension<ModExtension_FleeFromCarrier>();
            if (ext != null) return ext.fleeRadius;
            return 18f; 
        }

        public static float GetFleeBodySizeLimit(Pawn carrier)
        {
            if (carrier == null) return 0f;
            var comp = carrier.GetComp<CompFleeFromCarrier>();
            if (comp != null && comp.enabled && comp.PropsFlee != null) return comp.PropsFlee.fleeBodySizeLimit;
            var ext = carrier.def?.GetModExtension<ModExtension_FleeFromCarrier>();
            if (ext != null) return ext.fleeBodySizeLimit;
            return 0f; 
        }

        public static int GetFleeDistance(Pawn carrier)
        {
            if (carrier == null) return 24;
            var comp = carrier.GetComp<CompFleeFromCarrier>();
            if (comp != null && comp.enabled && comp.PropsFlee != null && comp.PropsFlee.fleeDistance.HasValue) return comp.PropsFlee.fleeDistance.Value;
            var ext = carrier.def?.GetModExtension<ModExtension_FleeFromCarrier>();
            if (ext != null && ext.fleeBodySizeLimit >= 0 && ext.fleeRadius >= 0 && ext.fleeBodySizeLimit != float.NaN)
            {
                if (ext.fleeRadius >= 0 && ext.fleeBodySizeLimit >= 0 && ext.fleeDistance.HasValue) return ext.fleeDistance.Value;
            }
            
            return 24;
        }
    }

    
    
    
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier
    {
        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                
                if (pawn != null && NoFleeUtil.IsNoFlee(pawn, out var noFleeExt))
                {
                    if (noFleeExt?.verboseLogging == true && Prefs.DevMode)
                        Log.Message($"[Zoology] Suppressed Carrier-induced flee for {pawn.LabelShort} due to ModExtension_NoFlee.");
                    return;
                }
                
                if (__result != null) return;

                if (pawn == null) return;
                if (!pawn.RaceProps.Animal) return;

                
                
                
                if (FleeFromCarrierUtil.IsCarrier(pawn)) return;

                
                
                
                const int MaxPossibleSearchRadius = 50; 

                Pawn threat = GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.Pawn),
                    PathEndMode.OnCell,
                    TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn),
                    MaxPossibleSearchRadius,
                    t =>
                    {
                        var p = t as Pawn;
                        if (p == null) return false;
                        if (p == pawn) return false;
                        if (!FleeFromCarrierUtil.IsCarrier(p)) return false;
                        if (p.Downed) return false;

                        
                        float realRadius = FleeFromCarrierUtil.GetFleeRadius(p);
                        if (realRadius <= 0f) return false; 

                        
                        if (!pawn.Position.InHorDistOf(p.Position, realRadius)) return false;

                        
                        

                        return true;
                    }
                ) as Pawn;

                if (threat == null) return;

                
                float bodySizeLimit = FleeFromCarrierUtil.GetFleeBodySizeLimit(threat);
                if (bodySizeLimit > 0f && pawn.BodySize > bodySizeLimit)
                {
                    
                    return;
                }

                
                if (!FleeUtility.ShouldAnimalFleeDanger(pawn)) return;

                
                int fleeDistance = FleeFromCarrierUtil.GetFleeDistance(threat);
                __result = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] Patch_JobGiver_AnimalFlee_TryGiveJob_FleeFromCarrier error: {e}");
            }
        }
    }
}
