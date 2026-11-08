using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Use vanilla region-based hearing: pursuing prey alone is not a noise alarm.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.HearClamor))]
    internal static class Patch_AnimalAlarmClamor
    {
        static void Postfix(Pawn __instance, Thing source, ClamorDef type)
        {
            if (type != ClamorDefOf.Harm && type != ClamorDefOf.Movement)
            {
                return;
            }

            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings != null && settings.DisableAllRuntimePatches)
            {
                return;
            }

            if (__instance?.IsAnimal != true || __instance.Dead || __instance.Downed
                || !__instance.Spawned || __instance.Awake()
                || source is not Pawn origin || origin == __instance || origin.Map != __instance.Map)
            {
                return;
            }

            if (type == ClamorDefOf.Movement
                && !origin.InAggroMentalState && origin.kindDef?.hostileToAll != true)
            {
                return;
            }

            __instance.mindState.canSleepTick = (Find.TickManager?.TicksGame ?? 0) + 1000;
            RestUtility.WakeUp(__instance);
        }
    }
}
