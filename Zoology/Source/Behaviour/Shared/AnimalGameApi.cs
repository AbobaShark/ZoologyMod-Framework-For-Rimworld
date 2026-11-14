using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Bind interval trackers once. The legacy build does not hard-reference the new tracker methods.
    internal static class AnimalGameApi
    {
        private static Action<T, int> Interval<T>(string name) => AccessTools.Method(typeof(T), name, new[] { typeof(int) }) is MethodInfo method
            ? (Action<T, int>)Delegate.CreateDelegate(typeof(Action<T, int>), method) : null;
        private static Action<T> Legacy<T>(string name) => AccessTools.Method(typeof(T), name, Type.EmptyTypes) is MethodInfo method
            ? (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), method) : null;
        private static readonly Action<Pawn_HealthTracker, int> healthInterval = Interval<Pawn_HealthTracker>("HealthTickInterval");
        private static readonly Action<Pawn_NeedsTracker, int> needsInterval = Interval<Pawn_NeedsTracker>("NeedsTrackerTickInterval");
        private static readonly Action<Pawn_AgeTracker, int> ageInterval = Interval<Pawn_AgeTracker>("AgeTickInterval");
        private static readonly Action<Pawn_NeedsTracker> needsLegacy = Legacy<Pawn_NeedsTracker>("NeedsTrackerTick");
        private static readonly Action<Pawn_AgeTracker> ageLegacy = Legacy<Pawn_AgeTracker>("AgeTick");
        private static readonly MethodInfo findPath = typeof(PathFinder).GetMethods().FirstOrDefault(m =>
            (m.Name == "FindPathNow" || m.Name == "FindPath") && m.GetParameters().Length >= 3 && m.GetParameters()[2].ParameterType == typeof(Pawn));
        public static void TickContainedPhysiology(Pawn pawn)
        {
            // Containment simulates sleeping, not an indefinite forced-rest
            // job. Stop marking the pawn as resting once fully rested, as a
            // vanilla sleeper would wake up. This matters when mods allow
            // needs to exceed their normal maximum.
            Need_Rest rest = pawn.needs?.rest;
            if (rest != null && rest.CurLevel < rest.MaxLevel)
            {
                rest.TickResting(1f);
            }

            pawn.health.HealthTick();
            if (pawn.Dead) return;
            healthInterval?.Invoke(pawn.health, 1);
            if (pawn.Dead) return;
            if (needsInterval != null) needsInterval(pawn.needs, 1); else needsLegacy?.Invoke(pawn.needs);
            if (pawn.Dead) return;

            // Resting may remain true briefly after the final TickResting
            // (until the pawn's update interval expires). Enforce the
            // natural sleep ceiling after the needs tick, independently of
            // third-party changes to Need.CurLevel's setter.
            if (rest != null && rest.CurLevel > rest.MaxLevel)
            {
                rest.CurLevel = rest.MaxLevel;
            }

            if (ageInterval != null) ageInterval(pawn.ageTracker, 1); else ageLegacy?.Invoke(pawn.ageTracker);
            // The pouch of a parent held inside a barn/den still requires
            // manual offspring physiology. The parent's normal CompTick does
            // not run while it is unspawned.
            pawn.TryGetComp<CompAnimalPouch>()?.TickContainedPhysiology();
        }
        public static PawnPath FindPath(Pawn pawn, IntVec3 target)
        {
#if RIMWORLD17
            return pawn.Map.pathFinder.FindPathNow(pawn.Position, target, pawn);
#else
            if (findPath == null) return PawnPath.NotFound;
            ParameterInfo[] parameters = findPath.GetParameters();
            object[] args = new object[parameters.Length];
            args[0] = pawn.Position; args[1] = (LocalTargetInfo)target; args[2] = pawn;
            for (int i = 3; i < args.Length; i++)
                args[i] = parameters[i].ParameterType == typeof(PathEndMode) ? (object)PathEndMode.OnCell
                    : parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            return (PawnPath)findPath.Invoke(pawn.Map.pathFinder, args);
#endif
        }
    }
}
