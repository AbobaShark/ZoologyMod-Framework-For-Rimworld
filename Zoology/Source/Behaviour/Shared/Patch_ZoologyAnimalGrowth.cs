using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn), "get_BodySize")]
    internal static class Patch_ZoologyDailyBodySize
    {
        private static void Postfix(Pawn __instance, ref float __result)
        {
            // Preserve vanilla, gene, hediff and other-mod modifiers on the
            // original result. Only replace the already applied stage factor.
            __result *= ZoologyAnimalGrowth.BodyRatio(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), "get_HealthScale")]
    internal static class Patch_ZoologyDailyHealthScale
    {
        private static void Postfix(Pawn __instance, ref float __result)
        {
            __result *= ZoologyAnimalGrowth.HealthRatio(__instance);
        }
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyDailyHungerRate
    {
        private static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(Need_Food), "FoodFallPerTick") != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Need_Food), "FoodFallPerTick");
        }

        private static void Postfix(Need_Food __instance, ref float __result)
        {
            if (__instance?.pawn != null)
                __result *= ZoologyAnimalGrowth.HungerRatio(__instance.pawn);
        }
    }

    // Modify the root render transform exactly once, not each child mesh.
    // All descendant body parts inherit this scale and no graphics are rebuilt.
    [HarmonyPatch(typeof(PawnRenderNodeWorker), "ScaleFor")]
    internal static class Patch_ZoologyDailyAnimalRenderScale
    {
        private static readonly FieldInfo RootField =
            AccessTools.Field(typeof(PawnRenderTree), "rootNode");
        private static readonly PropertyInfo RootProperty = RootField == null
            ? AccessTools.Property(typeof(PawnRenderTree), "RootNode") : null;

        private static bool Prepare()
        {
            if (RootField == null && RootProperty == null)
            {
                Log.Warning("[Zoology] PawnRenderTree root node not found; "
                    + "daily graphic scaling disabled for this RimWorld build.");
                return false;
            }
            return true;
        }

        private static void Postfix(PawnRenderNode node, PawnDrawParms parms,
            ref Vector3 __result)
        {
            Pawn pawn = parms.pawn;
            if (pawn == null || pawn.RaceProps?.Animal != true)
                return;
            var tree = pawn.Drawer?.renderer?.renderTree;
            if (tree == null)
                return;

            object root = RootField != null ? RootField.GetValue(tree)
                : RootProperty.GetValue(tree, null);
            if (!ReferenceEquals(root, node))
                return;

            Vector2 scale = ZoologyAnimalGrowth.DrawRatio(pawn);
            __result.x *= scale.x;
            __result.z *= scale.y;
        }
    }
}
