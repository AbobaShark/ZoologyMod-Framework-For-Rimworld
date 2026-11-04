#if RIMWORLD17
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalFilterVisibility
    {
        private static bool initialized;
        private static SpecialThingFilterDef females;
        private static SpecialThingFilterDef lactating;
        private static SpecialThingFilterDef nonLactating;
        private static SpecialThingFilterDef[] hiddenLactationFilters;

        private static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            lactating = DefDatabase<SpecialThingFilterDef>
                .GetNamedSilentFail("Zoology_AllowLactatingFemales");
            nonLactating = DefDatabase<SpecialThingFilterDef>
                .GetNamedSilentFail("Zoology_AllowNonLactatingFemales");
            foreach (SpecialThingFilterDef def in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
            {
                if (def.Worker is SpecialThingFilterWorker_AllowFemales)
                {
                    females = def;
                    break;
                }
            }
            if (lactating != null && nonLactating != null)
                hiddenLactationFilters = new[] { lactating, nonLactating };
        }

        internal static IEnumerable<SpecialThingFilterDef> HideLactationWhenFemalesExcluded(
            ThingFilter filter)
        {
            Initialize();
            return filter != null && females != null
                && hiddenLactationFilters != null
                && !filter.Allows(females) ? hiddenLactationFilters : null;
        }
    }

    // The vanilla pen tab already uses ThingFilterUI. Preserve that GUI and
    // its scroll state, but hide the dependent lactation option whenever
    // its prerequisite "Females" filter is switched off. This replaces only
    // one pen-tab GUI call; no pawn ticking or extra pen scanning is added.
    [HarmonyPatch(typeof(ITab_PenAnimals), "FillTab")]
    internal static class Patch_PenAnimalFilterDependentLactation
    {
        private static readonly FieldInfo FilterStateField =
            AccessTools.Field(typeof(ITab_PenAnimals), "animalFilterState");

        private static bool Prepare() =>
            ModConstants.Settings?.DisableAllRuntimePatches != true
            && FilterStateField != null;

        private static bool Prefix(ITab_PenAnimals __instance)
        {
            CompAnimalPenMarker pen = __instance.SelectedCompAnimalPenMarker;
            ThingFilterUI.UIState state = FilterStateField.GetValue(__instance)
                as ThingFilterUI.UIState;
            if (pen?.parent?.Map == null || state == null)
                return true;

            Rect frame = new Rect(0f, 0f, 300f, 480f).ContractedBy(10f);
            frame.SplitHorizontally(18f, out _, out Rect list);
            ThingFilterUI.DoThingFilterConfigWindow(list, state, pen.AnimalFilter,
                AnimalPenUtility.GetFixedAnimalFilter(),
                openMask: 1,
                forceHiddenFilters: AnimalFilterVisibility.HideLactationWhenFemalesExcluded(
                    pen.AnimalFilter),
                map: pen.parent.Map);
            return false;
        }
    }
}
#endif
