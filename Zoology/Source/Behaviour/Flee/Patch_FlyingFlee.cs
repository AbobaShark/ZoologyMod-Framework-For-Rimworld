

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System.Reflection;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    public static class Patch_FlyingFleeStart
    {
        public static bool Prefix(Pawn_FlightTracker __instance, Job job)
        {
            try
            {
                
                if (job == null || job.def == null)
                    return true; 

                
                if (job.def != JobDefOf.Flee)
                    return true;

                
                var pawnField = AccessTools.Field(typeof(Pawn_FlightTracker), "pawn");
                if (pawnField == null)
                    return true; 

                var pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null)
                    return true; 

                
                if (!pawn.RaceProps?.Animal == true) 
                    return true;
                if (!__instance.CanEverFly)
                    return true;

                
                
                
                job.flying = true;

                
                var cooldownField = AccessTools.Field(typeof(Pawn_FlightTracker), "flightCooldownTicks");
                if (cooldownField != null)
                {
                    cooldownField.SetValue(__instance, 0);
                }

                
                bool isFlying;
                
                try
                {
                    isFlying = __instance.Flying;
                }
                catch
                {
                    
                    return false; 
                }

                if (!isFlying)
                {
                    var startFlyingInternalMethod = AccessTools.Method(typeof(Pawn_FlightTracker), "StartFlyingInternal");
                    if (startFlyingInternalMethod != null)
                    {
                        startFlyingInternalMethod.Invoke(__instance, null);
                    }
                }

                
                return false;
            }
            catch (System.Exception ex)
            {
                
                Log.ErrorOnce($"ZoologyMod: exception in Patch_FlyingFleeStart Prefix: {ex}\nPatch will defer to original method to avoid crashes.", 1234567);
                return true;
            }
        }
    }
}