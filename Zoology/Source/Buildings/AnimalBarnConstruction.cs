#if RIMWORLD17
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalBarnConstruction
    {
        // Change only this building's architect entry, preserving unrelated
        // custom designators injected into Misc by other mods.
        internal static void Sync()
        {
            ThingDef barn = Building_AnimalBarn.BarnDef;
            DesignationCategoryDef category =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Misc");
            if (barn == null || category == null) return;

            bool enabled = Building_AnimalBarn.Enabled;
            var designators = category.AllResolvedDesignators;
            bool visible = barn.designationCategory == category;
            bool present = designators.Exists(d =>
                d is Designator_Build build && build.PlacingDef == barn);
            if (visible == enabled && present == enabled) return;

            barn.designationCategory = enabled ? category : null;
            designators.RemoveAll(d =>
                d is Designator_Build build && build.PlacingDef == barn);
            if (enabled) designators.Add(new Designator_Build(barn));
            category.DirtyCache();

            // A currently open Architect tab needs to rebuild its cached
            // category panels. On startup, no game exists and the new category
            // state will be used when the UI is created.
            if (Current.Game != null
                && MainButtonDefOf.Architect?.TabWindow is MainTabWindow_Architect architect)
                AccessTools.Method(typeof(MainTabWindow_Architect), "CacheDesPanels")
                    ?.Invoke(architect, null);
        }
    }

    public sealed partial class Building_AnimalBarn
    {
        internal static void SyncConstructionAvailability()
        {
            AnimalBarnConstruction.Sync();
        }
    }
}
#endif
