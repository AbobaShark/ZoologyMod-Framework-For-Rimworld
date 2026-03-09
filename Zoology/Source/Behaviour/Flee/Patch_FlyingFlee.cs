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
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_FlightTracker), "pawn");
        private static readonly FieldInfo FlightCooldownField = AccessTools.Field(typeof(Pawn_FlightTracker), "flightCooldownTicks");
        private static readonly MethodInfo StartFlyingInternalMethod = AccessTools.Method(typeof(Pawn_FlightTracker), "StartFlyingInternal");

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableFlyingFleeStart;
        }

        public static bool Prefix(Pawn_FlightTracker __instance, Job job)
        {
            try
            {
                var settings = ModConstants.Settings;
                if (settings != null && !settings.EnableFlyingFleeStart)
                    return true;

                
                if (job == null || job.def == null)
                    return true; 

                
                if (job.def != JobDefOf.Flee)
                    return true;

                
                if (PawnField == null)
                    return true; 

                var pawn = PawnField.GetValue(__instance) as Pawn;
                if (pawn == null)
                    return true; 

                if (pawn.RaceProps == null || !pawn.RaceProps.Animal)
                    return true;
                if (!__instance.CanEverFly)
                    return true;

                
                
                
                job.flying = true;

                
                if (FlightCooldownField != null)
                {
                    FlightCooldownField.SetValue(__instance, 0);
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
                    if (StartFlyingInternalMethod != null)
                    {
                        StartFlyingInternalMethod.Invoke(__instance, null);
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
