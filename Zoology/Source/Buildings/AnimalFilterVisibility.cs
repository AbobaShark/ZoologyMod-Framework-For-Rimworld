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
        private static SpecialThingFilterDef adults;
        private static SpecialThingFilterDef[] adultFemaleFilters;

        private static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            adults = DefDatabase<SpecialThingFilterDef>
                .GetNamedSilentFail("Zoology_AllowReproductiveAdults");
            var lactating = DefDatabase<SpecialThingFilterDef>
                .GetNamedSilentFail("Zoology_AllowLactatingFemales");
            var pregnant = DefDatabase<SpecialThingFilterDef>
                .GetNamedSilentFail("Zoology_AllowPregnantFemales");
            var other = DefDatabase<SpecialThingFilterDef>
                .GetNamedSilentFail("Zoology_AllowNonLactatingFemales");
            foreach (SpecialThingFilterDef def in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
            {
                if (def.Worker is SpecialThingFilterWorker_AllowFemales)
                {
                    females = def;
                    break;
                }
            }
            if (lactating != null && pregnant != null && other != null)
                adultFemaleFilters = new[] { lactating, pregnant, other };
        }

        // Three adult-female selectors are subordinate to BOTH the
        // general age and sex filters. Hidden selectors retain their
        // stored selections but cannot override those parent filters.
        internal static IEnumerable<SpecialThingFilterDef> HiddenAdultFemaleFilters(
            ThingFilter filter)
        {
            Initialize();
            return filter != null && adultFemaleFilters != null
                && (adults != null && !filter.Allows(adults)
                    || females != null && !filter.Allows(females))
                ? adultFemaleFilters : null;
        }
    }

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
                forceHiddenFilters: AnimalFilterVisibility.HiddenAdultFemaleFilters(
                    pen.AnimalFilter),
                map: pen.parent.Map);
            return false;
        }
    }
}
#endif
