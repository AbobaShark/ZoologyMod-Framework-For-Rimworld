using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace ZoologyMod
{
    [HarmonyPatch(
        typeof(ThinkNode_ConditionalShouldFollowMaster),
        nameof(ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster))]
    internal static class Patch_ThinkNode_ShouldFollowMaster_NpcAnimalCompanion
    {
        private static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result && NpcAnimalCompanionVanillaAdapter.ShouldFollowMaster(pawn))
            {
                // JobGiver_AnimalFlee and both vanilla shot/damage flee paths call
                // this exact gate. Exposing the NPC link here gives companions the
                // same immunity to ordinary animal fear as a following player animal.
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Lord_AddPawnInternal_NpcAnimalCompanion
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Lord),
                "AddPawnInternal",
                new[] { typeof(Pawn), typeof(bool) });
        }

        private static bool Prefix(Pawn p)
        {
            // This is deliberately not a global animal/Lord rule. Only pawns which
            // were registered by the scoped PawnGroupMaker patches are intercepted.
            return !NpcAnimalCompanionUtility.SystemEnabled
                || NpcAnimalCompanionManager.Current?.IsCompanion(p) != true;
        }
    }

    [HarmonyPatch(typeof(Lord), nameof(Lord.GotoToil))]
    internal static class Patch_Lord_GotoToil_NpcAnimalCompanion
    {
        private static void Postfix(Lord __instance)
        {
            if (NpcAnimalCompanionUtility.SystemEnabled)
            {
                NpcAnimalCompanionManager.Current?.NotifyLordToilChanged(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TickRare))]
    internal static class Patch_Pawn_TickRare_NpcAnimalCompanion
    {
        private static void Postfix(Pawn __instance)
        {
            if (!NpcAnimalCompanionUtility.SystemEnabled)
            {
                return;
            }

            NpcAnimalCompanionManager manager = NpcAnimalCompanionManager.Current;
            if (manager?.TryGetLink(__instance, out NpcAnimalCompanionLink link) == true)
            {
                manager.ValidateOrResolve(link, allowDespawnGrace: true);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_Downed))]
    internal static class Patch_Pawn_NotifyDowned_NpcAnimalCompanion
    {
        private static void Postfix(Pawn __instance)
        {
            if (NpcAnimalCompanionUtility.SystemEnabled)
            {
                NpcAnimalCompanionManager.Current?.NotifyPawnDowned(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn), new[] { typeof(DestroyMode) })]
    internal static class Patch_Pawn_DeSpawn_NpcAnimalCompanion
    {
        private static void Postfix(Pawn __instance)
        {
            if (NpcAnimalCompanionUtility.SystemEnabled)
            {
                NpcAnimalCompanionManager.Current?.NotifyPawnDespawned(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap), new[] { typeof(bool), typeof(Rot4) })]
    internal static class Patch_Pawn_ExitMap_NpcAnimalCompanion
    {
        private static void Prefix(Pawn __instance)
        {
            if (NpcAnimalCompanionUtility.SystemEnabled)
            {
                NpcAnimalCompanionManager.Current?.NotifyPawnExitingMap(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill), new[] { typeof(DamageInfo?), typeof(Hediff) })]
    internal static class Patch_Pawn_Kill_NpcAnimalCompanion
    {
        private static void Postfix(Pawn __instance)
        {
            if (NpcAnimalCompanionUtility.SystemEnabled)
            {
                NpcAnimalCompanionManager.Current?.NotifyPawnKilledOrDestroyed(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy), new[] { typeof(DestroyMode) })]
    internal static class Patch_Pawn_Destroy_NpcAnimalCompanion
    {
        private static void Postfix(Pawn __instance)
        {
            if (NpcAnimalCompanionUtility.SystemEnabled)
            {
                NpcAnimalCompanionManager.Current?.NotifyPawnKilledOrDestroyed(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction), new[] { typeof(Faction), typeof(Pawn) })]
    internal static class Patch_Pawn_SetFaction_NpcAnimalCompanion
    {
        private static void Prefix(Pawn __instance, out Faction __state)
        {
            __state = __instance?.Faction;
        }

        private static void Postfix(Pawn __instance, Faction __state)
        {
            // Pawn.SetFaction can be called with its current faction and returns
            // without changing anything. A postfix still runs in that case, so only
            // notify the companion manager about an actual transition.
            if (NpcAnimalCompanionUtility.SystemEnabled && __state != __instance?.Faction)
            {
                NpcAnimalCompanionManager.Current?.NotifyPawnFactionChanged(__instance);
            }
        }
    }
}
