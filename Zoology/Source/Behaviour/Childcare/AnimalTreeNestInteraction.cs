using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Natural tree nests physically share the tree's cell. GenSpawn normally
    // wipes a plant when an edifice is created over it; this is deliberately
    // restricted to our non-blocking nest and an existing living tree.
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    internal static class Patch_AnimalTreeNestPreserveTree
    {
        private static bool Prefix(BuildableDef newEntDef, BuildableDef oldEntDef,
            ref bool __result)
        {
            if (newEntDef is ThingDef nest
                && nest.defName == "Zoology_AnimalTreeNest"
                && oldEntDef is ThingDef plant
                && plant.category == ThingCategory.Plant
                && plant.plant?.IsTree == true)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // Vanilla discovers buildings by the mouse's physical grid cell, but a
    // branch can project several cells beyond its tree trunk. Add only nests
    // whose actual visible sprite intersects the cursor and suppress a false
    // hit on the hidden construction cell. This is a click/targeting path,
    // never a per-tick scan.
    [HarmonyPatch]
    internal static class Patch_AnimalTreeNestSelection
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(GenUI).GetMethods(
                BindingFlags.Static | BindingFlags.Public))
            {
                if (method.Name != nameof(GenUI.ThingsUnderMouse)
                    || method.ReturnType != typeof(List<Thing>))
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 3 || parameters[0].ParameterType != typeof(Vector3)
                    || parameters[2].ParameterType != typeof(TargetingParameters))
                    continue;
                yield return method;
            }
        }

        private static void Postfix(Vector3 __0, TargetingParameters __2,
            ref List<Thing> __result)
        {
            Map map = Find.CurrentMap;
            if (map == null || __result == null || __2 == null)
                return;

            // Do not leave an invisible/selectable ground footprint.
            for (int i = __result.Count - 1; i >= 0; i--)
                if (__result[i] is Building_AnimalNursery nest && nest.TreeNest
                    && !nest.CanopyHitTest(__0))
                    __result.RemoveAt(i);

            AnimalRefugeMapComponent index =
                map.GetComponent<AnimalRefugeMapComponent>();
            if (index == null) return;
            foreach (Thing candidate in index.Nearby(
                IntVec3.FromVector3(__0), 8))
            {
                if (candidate is not Building_AnimalNursery nest
                    || !nest.TreeNest || !nest.CanopyHitTest(__0)
                    || !__2.CanTarget(nest) || __result.Contains(nest))
                    continue;
                __result.Insert(0, nest);
            }
        }
    }
}
