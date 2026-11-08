#if RIMWORLD17
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Filter only the Animals window's enumerator. The underlying
    // MapPawns.ColonyAnimals list remains intact for vanilla and Zoology
    // auto-slaughter, medical care, records, and other game systems.
    [HarmonyPatch]
    internal static class Patch_BarnAnimalTabVisibility
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(MainTabWindow_Animals), "Pawns");
        }

        private static bool Prepare()
        {
            ZoologyModSettings settings = ModConstants.Settings;
            return Building_AnimalBarn.Enabled && settings != null
                && settings.HideBarnAnimalsFromAnimalTab
                && TargetMethod() != null;
        }

        private static void Postfix(ref IEnumerable<Pawn> __result)
        {
            if (__result == null || !Building_AnimalBarn.Enabled
                || ModConstants.Settings?.HideBarnAnimalsFromAnimalTab != true)
            {
                return;
            }

            __result = VisibleAnimals(__result);
        }

        private static IEnumerable<Pawn> VisibleAnimals(IEnumerable<Pawn> animals)
        {
            foreach (Pawn animal in animals)
            {
                if (animal?.ParentHolder is Building_AnimalBarn)
                {
                    continue;
                }

                yield return animal;
            }
        }
    }
}
#endif
