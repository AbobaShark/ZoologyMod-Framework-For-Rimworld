using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Reflection;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    public static class Patch_FlyingFleeStart
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnFieldRef =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, int> FlightCooldownFieldRef =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, int>("flightCooldownTicks");
        private static readonly Func<Pawn_FlightTracker, bool> CanEverFlyGetter =
            AccessTools.PropertyGetter(typeof(Pawn_FlightTracker), "CanEverFly") is MethodInfo canEverFlyGetter
                ? (Func<Pawn_FlightTracker, bool>)Delegate.CreateDelegate(typeof(Func<Pawn_FlightTracker, bool>), canEverFlyGetter)
                : null;
        private static readonly Func<Pawn_FlightTracker, bool> FlyingGetter =
            AccessTools.PropertyGetter(typeof(Pawn_FlightTracker), "Flying") is MethodInfo flyingGetter
                ? (Func<Pawn_FlightTracker, bool>)Delegate.CreateDelegate(typeof(Func<Pawn_FlightTracker, bool>), flyingGetter)
                : null;
        private static readonly Action<Pawn_FlightTracker> StartFlyingInternalAction =
            AccessTools.Method(typeof(Pawn_FlightTracker), "StartFlyingInternal") is MethodInfo startFlyingInternal
                ? (Action<Pawn_FlightTracker>)Delegate.CreateDelegate(typeof(Action<Pawn_FlightTracker>), startFlyingInternal)
                : null;

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

                if (__instance == null)
                    return false;

                if (PawnFieldRef == null || CanEverFlyGetter == null || FlyingGetter == null || StartFlyingInternalAction == null)
                    return true;

                var pawn = PawnFieldRef(__instance);
                if (pawn == null)
                    return false; 

                
                if (job == null || job.def == null)
                    return false; 

                
                if (job.def != JobDefOf.Flee)
                    return true;

                
                if (pawn.RaceProps == null)
                    return false;
                if (!pawn.RaceProps.Animal)
                    return true;
                bool canEverFly = CanEverFlyGetter(__instance);
                if (!canEverFly)
                    return true;

                
                
                
                job.flying = true;

                
                FlightCooldownFieldRef(__instance) = 0;

                
                bool isFlying = FlyingGetter(__instance);

                if (!isFlying)
                {
                    StartFlyingInternalAction(__instance);
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
